using System.Collections.Specialized;
using Filer.Core;

namespace Filer.Core.Tests;

public class BulkObservableCollectionTests
{
    [Fact]
    public void ReplaceAll_SetsContents()
    {
        var col = new BulkObservableCollection<int> { 1, 2, 3 };

        col.ReplaceAll(new[] { 10, 20 });

        Assert.Equal(new[] { 10, 20 }, col);
    }

    [Fact]
    public void ReplaceAll_RaisesSingleResetNotification()
    {
        var col = new BulkObservableCollection<int> { 1, 2, 3 };
        var events = new List<NotifyCollectionChangedAction>();
        col.CollectionChanged += (_, e) => events.Add(e.Action);

        col.ReplaceAll(Enumerable.Range(0, 10_000).ToList());

        Assert.Equal(new[] { NotifyCollectionChangedAction.Reset }, events);
        Assert.Equal(10_000, col.Count);
    }

    [Fact]
    public void ReplaceAll_RaisesCountAndIndexerPropertyChanged()
    {
        var col = new BulkObservableCollection<int>();
        var props = new List<string?>();
        ((System.ComponentModel.INotifyPropertyChanged)col).PropertyChanged += (_, e) => props.Add(e.PropertyName);

        col.ReplaceAll(new[] { 1 });

        Assert.Contains(nameof(col.Count), props);
        Assert.Contains("Item[]", props);
    }

    [Fact]
    public void ReplaceAll_WithEmpty_ClearsContents()
    {
        var col = new BulkObservableCollection<int> { 1, 2 };

        col.ReplaceAll(Array.Empty<int>());

        Assert.Empty(col);
    }

    [Fact]
    public void AddRange_AppendsWithSingleResetNotification()
    {
        var col = new BulkObservableCollection<int> { 1 };
        var events = new List<NotifyCollectionChangedAction>();
        col.CollectionChanged += (_, e) => events.Add(e.Action);

        col.AddRange(new[] { 2, 3 });

        Assert.Equal(new[] { 1, 2, 3 }, col);
        Assert.Equal(new[] { NotifyCollectionChangedAction.Reset }, events);
    }
}
