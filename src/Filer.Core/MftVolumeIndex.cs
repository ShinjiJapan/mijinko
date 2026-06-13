using System.Text;

namespace Filer.Core;

/// <summary>
/// 1ボリューム分の MFT 索引(Everything 方式のメモリ内インデックス)。
/// FRN(64bit ファイル参照番号 = 上位16bit シーケンス + 下位48bit レコード番号)をキーに
/// 親 FRN と名前を保持し、名前一致→祖先スコープ判定→フルパス構築で検索する。
/// メモリを抑えるため、レコード番号を添字とする並列配列 + UTF-8 名前ヒープで格納する
/// (Dictionary や per-record string を持たない。500万件で約 150〜250MB)。
/// I/O を持たないため合成レコードでテスト可能。スレッド安全ではない(呼び出し側でロック)。
/// </summary>
public sealed class MftVolumeIndex
{
    private const ulong RecnoMask = 0x0000FFFFFFFFFFFFUL;
    private const byte FlagUsed = 1;
    private const byte FlagDirectory = 2;

    // 名前ヒープ: 1MB チャンクの UTF-8 バイト列。オフセット = chunkIndex << 20 | chunkInner。
    private const int ChunkShift = 20;
    private const int ChunkSize = 1 << ChunkShift;

    private readonly string _rootPath;      // "C:\" 形式
    private readonly string _rootPrefix;    // パス連結用 "C:"(末尾 \ なし)
    private readonly ulong _rootFrn;

    // レコード番号を添字とする並列配列
    private long[] _parent = new long[1024];
    private int[] _nameOffset = new int[1024];
    private ushort[] _nameLength = new ushort[1024];   // UTF-8 バイト数
    private ushort[] _sequence = new ushort[1024];
    private byte[] _flags = new byte[1024];
    private long _maxRecno = -1;

    private readonly List<byte[]> _nameChunks = new() { new byte[ChunkSize] };
    private int _nameTail;   // 最終チャンク内の使用済みバイト数

    // ハードリンク(1 FRN に複数名)の 2 つ目以降。レコード番号 → (親 FRN, 名前ヒープ位置) の一覧。
    // ディレクトリはハードリンク不可(NTFS 仕様)なのでファイルのみ。該当ファイルは僅かなので辞書で持つ。
    private readonly Dictionary<long, List<(long ParentFrn, int NameOffset, ushort NameLength)>> _extraNames = new();

    /// <summary>差分更新用の USN ジャーナル位置(MftSearchService が管理)。</summary>
    public ulong JournalId { get; set; }
    public long NextUsn { get; set; }

    public MftVolumeIndex(string rootPath, ulong rootFrn)
    {
        _rootPath = rootPath;
        _rootPrefix = rootPath.TrimEnd('\\');
        _rootFrn = rootFrn;
    }

    /// <summary>レコードを登録/上書きする(作成・改名・移動)。追加名(ハードリンク)は破棄される。</summary>
    public void Set(ulong frn, ulong parentFrn, ReadOnlySpan<char> name, bool isDirectory)
    {
        var recno = (long)(frn & RecnoMask);
        EnsureCapacity(recno);

        _parent[recno] = unchecked((long)parentFrn);
        _sequence[recno] = (ushort)(frn >> 48);
        _flags[recno] = (byte)(FlagUsed | (isDirectory ? FlagDirectory : 0));
        (_nameOffset[recno], _nameLength[recno]) = AppendName(name);
        _extraNames.Remove(recno);
        if (recno > _maxRecno) _maxRecno = recno;
    }

    /// <summary>
    /// レコードに名前を追加する。FRN 未登録なら <see cref="Set"/> と同じ。
    /// 登録済みなら追加名(ハードリンク)として持つ(同じ親+名前の重複は無視)。
    /// </summary>
    public void AddName(ulong frn, ulong parentFrn, ReadOnlySpan<char> name, bool isDirectory)
    {
        var recno = (long)(frn & RecnoMask);
        if (recno >= _flags.Length || (_flags[recno] & FlagUsed) == 0 ||
            _sequence[recno] != (ushort)(frn >> 48))
        {
            Set(frn, parentFrn, name, isDirectory);
            return;
        }

        var parent = unchecked((long)parentFrn);
        Span<char> buffer = stackalloc char[300];
        if (_parent[recno] == parent &&
            DecodeName(recno, buffer).Equals(name, StringComparison.Ordinal))
            return;
        _extraNames.TryGetValue(recno, out var list);
        if (list is not null)
        {
            foreach (var extra in list)
                if (extra.ParentFrn == parent &&
                    DecodeNameAt(extra.NameOffset, extra.NameLength, buffer).Equals(name, StringComparison.Ordinal))
                    return;
        }

        var (offset, length) = AppendName(name);
        (list ?? (_extraNames[recno] = new())).Add((parent, offset, length));
    }

    /// <summary>レコードを削除する(ファイル削除)。追加名も消える。</summary>
    public void Remove(ulong frn)
    {
        var recno = (long)(frn & RecnoMask);
        if (recno < _flags.Length)
        {
            _flags[recno] = 0;
            _extraNames.Remove(recno);
        }
    }

    /// <summary>FRN の現在のフルパス(主名)。未登録・孤児は null。差分更新時の名前再解決に使う。</summary>
    public string? PrimaryPathOf(ulong frn)
    {
        if (!TryGetSlot(frn, out var recno)) return null;
        Span<char> buffer = stackalloc char[300];
        var cache = new Dictionary<ulong, string?>();
        var parentPath = PathOf(unchecked((ulong)_parent[recno]), cache);
        return parentPath is null ? null : parentPath + "\\" + DecodeName(recno, buffer).ToString();
    }

    /// <summary>
    /// 一致通知。matched=名前がパターン一致、isZip=zip 内検索用の .zip ファイル
    /// (名前不一致でも needZips 時は通知される)。
    /// </summary>
    public delegate void MatchSink(string fullPath, bool isDirectory, bool matched, bool isZip);

    /// <summary>
    /// 全レコードを走査し、名前一致かつ baseFrn 配下のエントリのフルパスを通知する。
    /// baseFrn がボリュームルートなら全体。基準ディレクトリ自身は含まない。
    /// </summary>
    public void Scan(FileNameMatcher matcher, bool needZips, ulong baseFrn,
        bool includeFiles, bool includeDirectories, MatchSink sink, CancellationToken token = default)
    {
        // 1回の走査内で使い回すキャッシュ(ディレクトリのパスとスコープ判定)
        var pathCache = new Dictionary<ulong, string?>();
        var scopeCache = new Dictionary<ulong, bool>();
        var wholeVolume = baseFrn == _rootFrn;

        Span<char> nameBuffer = stackalloc char[300];   // NTFS 名は最大 255 文字

        for (long recno = 0; recno <= _maxRecno; recno++)
        {
            if ((recno & 0xFFFF) == 0 && token.IsCancellationRequested) return;

            var flags = _flags[recno];
            if ((flags & FlagUsed) == 0) continue;
            var isDir = (flags & FlagDirectory) != 0;

            EmitIfHit(DecodeName(recno, nameBuffer), unchecked((ulong)_parent[recno]), isDir);

            // ハードリンクの追加名も同様に判定する(名前・親ごとに独立)。
            if (_extraNames.TryGetValue(recno, out var extras))
                foreach (var extra in extras)
                    EmitIfHit(DecodeNameAt(extra.NameOffset, extra.NameLength, nameBuffer),
                        unchecked((ulong)extra.ParentFrn), isDir);
        }

        void EmitIfHit(ReadOnlySpan<char> name, ulong parentFrn, bool isDir)
        {
            bool matched;
            var isZip = false;
            if (isDir)
            {
                if (!includeDirectories) return;
                matched = matcher(name);
            }
            else
            {
                matched = includeFiles && matcher(name);
                isZip = needZips && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
            }
            if (!matched && !isZip) return;

            if (!wholeVolume && !IsUnderScope(parentFrn, baseFrn, scopeCache)) return;

            if (PathOf(parentFrn, pathCache) is not { } parentPath) return;   // 祖先が索引にない(孤児)
            sink(parentPath + "\\" + name.ToString(), isDir, matched, isZip);
        }
    }

    /// <summary>frn(基準候補の親)の祖先に baseFrn が含まれるか。経路上の判定結果をキャッシュする。</summary>
    private bool IsUnderScope(ulong frn, ulong baseFrn, Dictionary<ulong, bool> cache)
    {
        var chain = new List<ulong>();
        var current = frn;
        bool result;
        for (var depth = 0; ; depth++)
        {
            if (current == baseFrn) { result = true; break; }
            if (current == _rootFrn || depth > 512) { result = false; break; }
            if (cache.TryGetValue(current, out var known)) { result = known; break; }
            if (!TryGetSlot(current, out var recno)) { result = false; break; }
            chain.Add(current);
            current = unchecked((ulong)_parent[recno]);
        }
        foreach (var frnOnChain in chain)
            cache[frnOnChain] = result;
        return result;
    }

    /// <summary>frn のフルパス(孤児・無効参照は null)。ディレクトリのパスはキャッシュする。</summary>
    private string? PathOf(ulong frn, Dictionary<ulong, string?> cache)
    {
        if (frn == _rootFrn) return _rootPrefix;
        if (cache.TryGetValue(frn, out var cached)) return cached;

        string? path = null;
        if (TryGetSlot(frn, out var recno))
        {
            Span<char> buffer = stackalloc char[300];
            var parentPath = PathOf(unchecked((ulong)_parent[recno]), cache);
            if (parentPath is not null)
                path = parentPath + "\\" + DecodeName(recno, buffer).ToString();
        }
        cache[frn] = path;
        return path;
    }

    /// <summary>frn が現在有効なレコードを指していればレコード番号を返す(シーケンス番号も照合)。</summary>
    private bool TryGetSlot(ulong frn, out long recno)
    {
        recno = (long)(frn & RecnoMask);
        return recno <= _maxRecno
            && (_flags[recno] & FlagUsed) != 0
            && _sequence[recno] == (ushort)(frn >> 48);
    }

    private ReadOnlySpan<char> DecodeName(long recno, Span<char> buffer) =>
        DecodeNameAt(_nameOffset[recno], _nameLength[recno], buffer);

    private ReadOnlySpan<char> DecodeNameAt(int offset, ushort length, Span<char> buffer)
    {
        var bytes = _nameChunks[offset >> ChunkShift].AsSpan(offset & (ChunkSize - 1), length);
        var chars = Encoding.UTF8.GetChars(bytes, buffer);
        return buffer[..chars];
    }

    /// <summary>名前を UTF-8 でヒープへ追記する(チャンク跨ぎはしない)。</summary>
    private (int Offset, ushort Length) AppendName(ReadOnlySpan<char> name)
    {
        var max = Encoding.UTF8.GetMaxByteCount(name.Length);
        if (_nameTail + max > ChunkSize)
        {
            _nameChunks.Add(new byte[ChunkSize]);
            _nameTail = 0;
        }
        var chunkIndex = _nameChunks.Count - 1;
        var written = Encoding.UTF8.GetBytes(name, _nameChunks[chunkIndex].AsSpan(_nameTail));
        var offset = (chunkIndex << ChunkShift) | _nameTail;
        _nameTail += written;
        return (offset, (ushort)written);
    }

    private void EnsureCapacity(long recno)
    {
        if (recno < _parent.Length) return;
        var size = _parent.Length;
        while (size <= recno) size *= 2;
        Array.Resize(ref _parent, size);
        Array.Resize(ref _nameOffset, size);
        Array.Resize(ref _nameLength, size);
        Array.Resize(ref _sequence, size);
        Array.Resize(ref _flags, size);
    }
}
