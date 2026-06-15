namespace Filer.Core;

/// <summary>
/// キー重複を排除する容量上限つき LIFO(後着優先)キュー。
/// 同じキーを再投入すると値を更新して最前面(最新)へ繰り上げる。容量超過時は最古(末尾)を捨てる。
/// サムネイル生成要求のスケジューリング用。スレッド安全ではない(呼び出し側がロックで守る)。
/// </summary>
/// <remarks>
/// 大量フォルダーのグリッド表示で、スクロールにより画面外へ流れた古い要求を無制限に溜め込み、
/// ワーカーがそれを延々と処理して占有する問題を防ぐ。容量で古い要求を捨て、見えている最新要求を優先する。
/// </remarks>
public sealed class BoundedLifoQueue<TKey, TValue> where TKey : notnull
{
    private readonly int _capacity;
    private readonly LinkedList<KeyValuePair<TKey, TValue>> _items = new();
    private readonly Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>> _index = new();

    public BoundedLifoQueue(int capacity) => _capacity = Math.Max(1, capacity);

    public int Count => _items.Count;

    public bool ContainsKey(TKey key) => _index.ContainsKey(key);

    /// <summary>
    /// 値を最前面(最新)に積む。既存キーは値を更新して最前面へ繰り上げる(順序は最新化、件数は増えない)。
    /// 容量を超えた場合は最古(末尾)を取り除き <paramref name="dropped"/> に返して true。捨てなければ false。
    /// </summary>
    public bool Push(TKey key, TValue value, out TValue dropped)
    {
        if (_index.TryGetValue(key, out var existing))
        {
            existing.Value = new KeyValuePair<TKey, TValue>(key, value);
            _items.Remove(existing);
            _items.AddFirst(existing);
            dropped = default!;
            return false;
        }

        var node = new LinkedListNode<KeyValuePair<TKey, TValue>>(new KeyValuePair<TKey, TValue>(key, value));
        _items.AddFirst(node);
        _index[key] = node;

        if (_items.Count > _capacity)
        {
            var last = _items.Last!;
            _items.RemoveLast();
            _index.Remove(last.Value.Key);
            dropped = last.Value.Value;
            return true;
        }

        dropped = default!;
        return false;
    }

    /// <summary>最前面(最新)の値を取り出す。空なら false。</summary>
    public bool TryPop(out TValue value)
    {
        var first = _items.First;
        if (first is null)
        {
            value = default!;
            return false;
        }
        _items.RemoveFirst();
        _index.Remove(first.Value.Key);
        value = first.Value.Value;
        return true;
    }
}
