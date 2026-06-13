using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Filer.Core;

/// <summary>
/// 一括差し替え/一括追加を1回の Reset 通知で行う ObservableCollection。
/// バインド済み一覧へ大量件数を1件ずつ Add すると件数分の変更通知処理で
/// UI スレッドが長時間ブロックされるため、それを避ける用途で使う。
/// </summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void AddRange(IEnumerable<T> items)
    {
        foreach (var item in items)
            Items.Add(item);
        RaiseReset();
    }

    public void ReplaceAll(IEnumerable<T> items)
    {
        Items.Clear();
        foreach (var item in items)
            Items.Add(item);
        RaiseReset();
    }

    private void RaiseReset()
    {
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
