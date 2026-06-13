using System.Buffers.Binary;
using System.Text.Json;

namespace Filer.Core;

/// <summary>
/// 高速検索(昇格ヘルパー)の名前付きパイプ越し IPC。
/// フレーム形式は <c>[int32 length(LE)][byte type][payload = UTF-8 JSON]</c>。
/// length は payload のバイト数(type は含まない)。
/// </summary>
public static class SearchIpcProtocol
{
    /// <summary>メッセージ種別。</summary>
    public enum MessageType : byte
    {
        /// <summary>本体→ヘルパー: 検索条件(<see cref="SearchRequestDto"/>)。</summary>
        Request = 0x01,
        /// <summary>本体→ヘルパー: 実行中検索の中断(payload なし)。</summary>
        Cancel = 0x02,
        /// <summary>ヘルパー→本体: 発見バッチ(<see cref="FileEntryDto"/>[])。</summary>
        Batch = 0x10,
        /// <summary>ヘルパー→本体: 完了(<see cref="DoneDto"/>)。</summary>
        Done = 0x11,
        /// <summary>ヘルパー→本体: 失敗(<see cref="ErrorDto"/>)。</summary>
        Error = 0x12,
    }

    /// <summary>1フレーム分のメッセージ。</summary>
    public sealed record Message(MessageType Type, byte[] Payload);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.General);

    /// <summary>DTO を UTF-8 JSON のバイト列へ。</summary>
    public static byte[] ToJsonUtf8<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Json);

    /// <summary>UTF-8 JSON のバイト列から DTO へ。</summary>
    public static T FromJsonUtf8<T>(byte[] payload) => JsonSerializer.Deserialize<T>(payload, Json)!;

    // ---- フレーム入出力 ----

    /// <summary>1フレームを同期で書き出す(発見バッチを複数ワーカーから直列化して送るため)。</summary>
    public static void WriteMessage(Stream stream, MessageType type, ReadOnlySpan<byte> payload)
    {
        Span<byte> header = stackalloc byte[5];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        header[4] = (byte)type;
        stream.Write(header);
        if (!payload.IsEmpty) stream.Write(payload);
        stream.Flush();
    }

    /// <summary>1フレームを非同期で書き出す。</summary>
    public static async Task WriteMessageAsync(Stream stream, MessageType type,
        ReadOnlyMemory<byte> payload, CancellationToken token = default)
    {
        var header = new byte[5];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        header[4] = (byte)type;
        await stream.WriteAsync(header, token).ConfigureAwait(false);
        if (!payload.IsEmpty) await stream.WriteAsync(payload, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }

    /// <summary>
    /// 1フレームを読み取る。ストリーム終端(パイプ切断)なら <c>null</c> を返す
    /// (例外を投げず、呼び出し側が切断を検知できるようにする)。
    /// </summary>
    public static async Task<Message?> ReadMessageAsync(Stream stream, CancellationToken token = default)
    {
        var header = new byte[5];
        if (!await ReadExactlyOrEofAsync(stream, header, token).ConfigureAwait(false))
            return null;

        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        var type = (MessageType)header[4];
        if (length < 0) return null;

        var payload = length == 0 ? Array.Empty<byte>() : new byte[length];
        if (length > 0 && !await ReadExactlyOrEofAsync(stream, payload, token).ConfigureAwait(false))
            return null;   // 途中切断も終端扱い

        return new Message(type, payload);
    }

    /// <summary>バッファを満たすまで読む。最初の読みで終端なら false(クリーン EOF)。</summary>
    private static async Task<bool> ReadExactlyOrEofAsync(Stream stream, Memory<byte> buffer,
        CancellationToken token)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer[total..], token).ConfigureAwait(false);
            if (n == 0) return false;
            total += n;
        }
        return true;
    }
}

/// <summary>検索条件の転送用 DTO(<see cref="FileSearchOptions"/> のプロセス境界版)。</summary>
public sealed record SearchRequestDto(
    string Pattern, string BaseDirectory, bool UseRegex,
    bool IncludeFiles, bool IncludeDirectories, bool SearchArchives)
{
    public static SearchRequestDto FromOptions(FileSearchOptions o) =>
        new(o.Pattern, o.BaseDirectory, o.UseRegex, o.IncludeFiles, o.IncludeDirectories, o.SearchArchives);

    /// <summary>
    /// ドメイン型へ。ヘルパーは管理者権限で動くため MFT 直読みを優先する(<c>PreferMft=true</c>)。
    /// </summary>
    public FileSearchOptions ToOptions() =>
        new(Pattern, BaseDirectory)
        {
            UseRegex = UseRegex,
            IncludeFiles = IncludeFiles,
            IncludeDirectories = IncludeDirectories,
            SearchArchives = SearchArchives,
            PreferMft = true,
        };
}

/// <summary>発見エントリの転送用 DTO(<see cref="FileEntry"/> のプロセス境界版)。</summary>
public sealed record FileEntryDto(
    string Name, string FullPath, bool IsDirectory, long Size, DateTime LastModified, bool IsArchive)
{
    public static FileEntryDto FromEntry(FileEntry e) =>
        new(e.Name, e.FullPath, e.IsDirectory, e.Size, e.LastModified, e.IsArchive);

    public FileEntry ToEntry() =>
        new(Name, FullPath, IsDirectory, Size, LastModified) { IsArchive = IsArchive };
}

/// <summary>完了通知(使用エンジン・補足・件数)。</summary>
public sealed record DoneDto(string Engine, string? Note, int Count);

/// <summary>失敗通知。</summary>
public sealed record ErrorDto(string Message);
