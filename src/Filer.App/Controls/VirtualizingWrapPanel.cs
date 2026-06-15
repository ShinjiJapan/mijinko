using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Filer.Core;

namespace Filer.App;

/// <summary>
/// 一定サイズのタイルを横に並べて折り返す UI 仮想化パネル。グリッド(サムネイル)表示用。
/// 画面に見えている行のコンテナだけを実体化するため、数万件フォルダーでも、表示中の範囲だけが
/// サムネイル生成要求を出す(後方のファイルもその行までスクロールすれば即要求される)。
/// タイルは全て同寸である前提(GridTileTemplate がそう作る)。縦スクロール専用。
/// レイアウト計算は UI 非依存の <see cref="GridVirtualization"/> に委譲してテスト可能にしている。
/// </summary>
public class VirtualizingWrapPanel : VirtualizingPanel, IScrollInfo
{
    // 先読み行数(スクロール時のちらつき軽減)。
    private const int BufferRows = 1;

    /// <summary>タイル外形(コンテナ1個ぶん)の幅(px)。VM の GridCellWidth をバインドする。</summary>
    public static readonly DependencyProperty CellWidthProperty = DependencyProperty.Register(
        nameof(CellWidth), typeof(double), typeof(VirtualizingWrapPanel),
        new FrameworkPropertyMetadata(GridTileMetrics.CellWidth(GridTileSize.Normal),
            FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>タイル外形(コンテナ1個ぶん)の高さ(px)。VM の GridCellHeight をバインドする。</summary>
    public static readonly DependencyProperty CellHeightProperty = DependencyProperty.Register(
        nameof(CellHeight), typeof(double), typeof(VirtualizingWrapPanel),
        new FrameworkPropertyMetadata(GridTileMetrics.CellHeight(GridTileSize.Normal),
            FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double CellWidth { get => (double)GetValue(CellWidthProperty); set => SetValue(CellWidthProperty, value); }
    public double CellHeight { get => (double)GetValue(CellHeightProperty); set => SetValue(CellHeightProperty, value); }

    // タイル1個の外形(全タイル同寸前提)。CellWidth/CellHeight から決める(コンテナ実測はしない。
    // 実測は Stretch=Uniform 画像が一瞬本来サイズに膨らんで寸法が乱高下し、列数・extent が毎パス
    // 変わってレイアウトが無限ループしたため)。
    private Size _itemSize = new(1, 1);
    private int _columns = 1;

    private Size _extent;
    private Size _viewport;
    private Point _offset;

    // measure 実行中フラグ。コンテナ実体化が誘発する RequestBringIntoView による
    // 再スクロール(=再measure)でレイアウトループになるのを防ぐ。
    private bool _isMeasuring;

    public ScrollViewer? ScrollOwner { get; set; }
    public bool CanHorizontallyScroll { get; set; }
    public bool CanVerticallyScroll { get; set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        _isMeasuring = true;
        try
        {
            var owner = ItemsControl.GetItemsOwner(this);
            var generator = ItemContainerGenerator;   // 触れてジェネレーターを初期化する
            int count = owner?.Items.Count ?? 0;

            double availWidth = double.IsInfinity(availableSize.Width) ? _viewport.Width : availableSize.Width;
            double availHeight = double.IsInfinity(availableSize.Height) ? _viewport.Height : availableSize.Height;

            // タイル寸法はバインドされた固定値から決める(同寸前提・実測しない=寸法が安定)。
            if (CellWidth > 0 && CellHeight > 0) _itemSize = new Size(CellWidth, CellHeight);

            _columns = GridVirtualization.Columns(availWidth, _itemSize.Width);
            var extent = new Size(
                _itemSize.Width * _columns,
                GridVirtualization.ExtentHeight(count, _columns, _itemSize.Height));
            var viewport = new Size(availWidth, availHeight);

            // 実体化の前に、最新の extent/viewport でオフセットを内容内へクランプする。
            // (最下部へジャンプした直後など、寸法確定前の SetVerticalOffset が下端を行き過ぎることがある。
            //  ここで補正しないと実体化が範囲外の行を対象にして空白になる。)
            _offset.Y = Math.Max(0, Math.Min(_offset.Y, Math.Max(0, extent.Height - viewport.Height)));

            var (first, last) = GridVirtualization.VisibleRange(
                _offset.Y, viewport.Height, _itemSize.Height, _columns, count, BufferRows);

            RealizeRange(first, last);
            CleanupContainers(first, last);

            UpdateScrollInfo(extent, viewport);

            return new Size(
                double.IsInfinity(availableSize.Width) ? extent.Width : availableSize.Width,
                double.IsInfinity(availableSize.Height) ? extent.Height : availableSize.Height);
        }
        finally
        {
            _isMeasuring = false;
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var generator = ItemContainerGenerator;
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            int index = generator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));
            if (index < 0 || _columns < 1) continue;

            int row = index / _columns;
            int col = index % _columns;
            var rect = new Rect(
                col * _itemSize.Width,
                row * _itemSize.Height - _offset.Y,
                _itemSize.Width,
                _itemSize.Height);
            child.Arrange(rect);
        }
        return finalSize;
    }

    /// <summary>[first,last] のコンテナを実体化して測定する。</summary>
    private void RealizeRange(int first, int last)
    {
        if (last < first) return;

        var generator = ItemContainerGenerator;
        var startPos = generator.GeneratorPositionFromIndex(first);
        int childIndex = startPos.Offset == 0 ? startPos.Index : startPos.Index + 1;

        using (generator.StartAt(startPos, GeneratorDirection.Forward, true))
        {
            for (int i = first; i <= last; i++, childIndex++)
            {
                if (generator.GenerateNext(out bool newlyRealized) is not UIElement child) break;
                if (newlyRealized)
                {
                    if (childIndex >= InternalChildren.Count)
                        AddInternalChild(child);
                    else
                        InsertInternalChild(childIndex, child);
                    generator.PrepareItemContainer(child);
                }
                child.Measure(_itemSize);
            }
        }
    }

    /// <summary>
    /// 表示範囲外のコンテナを片付ける(仮想化)。Recycling のときは破棄せず再利用プールへ戻す
    /// (重い画像タイルの作り直しを避け、連続スクロールのコスト・ちらつきを抑える)。
    /// </summary>
    private void CleanupContainers(int first, int last)
    {
        bool recycling = GetVirtualizationMode(this) == VirtualizationMode.Recycling;
        var generator = ItemContainerGenerator;
        var children = InternalChildren;
        for (int i = children.Count - 1; i >= 0; i--)
        {
            var pos = new GeneratorPosition(i, 0);
            int itemIndex = generator.IndexFromGeneratorPosition(pos);
            if (itemIndex < first || itemIndex > last)
            {
                if (recycling) ((IRecyclingItemContainerGenerator)generator).Recycle(pos, 1);
                else generator.Remove(pos, 1);
                RemoveInternalChildRange(i, 1);
            }
        }
    }

    private void UpdateScrollInfo(Size extent, Size viewport)
    {
        // オフセットは MeasureOverride 側で実体化前にクランプ済み。ここは寸法の保存と通知のみ。
        if (extent != _extent || viewport != _viewport)
        {
            _extent = extent;
            _viewport = viewport;
            ScrollOwner?.InvalidateScrollInfo();
        }
    }

    /// <summary>ScrollIntoView(キーボード移動)から呼ばれる。対象行を表示範囲へ入れる。</summary>
    protected override void BringIndexIntoView(int index)
    {
        if (_isMeasuring || index < 0 || _columns < 1) return;
        int row = index / _columns;
        double top = row * _itemSize.Height;
        double bottom = top + _itemSize.Height;

        if (top < _offset.Y) SetVerticalOffset(top);
        else if (bottom > _offset.Y + _viewport.Height) SetVerticalOffset(bottom - _viewport.Height);
    }

    // ---- IScrollInfo ----

    public double ExtentWidth => _extent.Width;
    public double ExtentHeight => _extent.Height;
    public double ViewportWidth => _viewport.Width;
    public double ViewportHeight => _viewport.Height;
    public double HorizontalOffset => 0;
    public double VerticalOffset => _offset.Y;

    private double LineDelta => _itemSize.Height;

    public void LineUp() => SetVerticalOffset(_offset.Y - LineDelta);
    public void LineDown() => SetVerticalOffset(_offset.Y + LineDelta);
    public void MouseWheelUp() => SetVerticalOffset(_offset.Y - LineDelta * SystemParameters.WheelScrollLines);
    public void MouseWheelDown() => SetVerticalOffset(_offset.Y + LineDelta * SystemParameters.WheelScrollLines);
    public void PageUp() => SetVerticalOffset(_offset.Y - _viewport.Height);
    public void PageDown() => SetVerticalOffset(_offset.Y + _viewport.Height);

    // 横スクロールは無効(折り返すため)。
    public void LineLeft() { }
    public void LineRight() { }
    public void MouseWheelLeft() { }
    public void MouseWheelRight() { }
    public void PageLeft() { }
    public void PageRight() { }
    public void SetHorizontalOffset(double offset) { }

    public void SetVerticalOffset(double offset)
    {
        double maxY = Math.Max(0, _extent.Height - _viewport.Height);
        offset = Math.Max(0, Math.Min(offset, maxY));
        // 要求値と報告値(VerticalOffset)を一致させる(デッドゾーンを設けるとドラッグ中に
        // サムが要求位置へ戻されず点滅・操作不能になる)。
        if (offset == _offset.Y) return;
        _offset.Y = offset;
        ScrollOwner?.InvalidateScrollInfo();
        InvalidateMeasure();
    }

    public Rect MakeVisible(System.Windows.Media.Visual visual, Rect rectangle)
    {
        if (_isMeasuring || visual is not UIElement child || _columns < 1) return rectangle;

        int childPos = InternalChildren.IndexOf(child);
        if (childPos < 0) return rectangle;
        int index = ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(childPos, 0));
        if (index < 0) return rectangle;

        int row = index / _columns;
        int col = index % _columns;
        double top = row * _itemSize.Height;
        double bottom = top + _itemSize.Height;

        if (top < _offset.Y) SetVerticalOffset(top);
        else if (bottom > _offset.Y + _viewport.Height) SetVerticalOffset(bottom - _viewport.Height);

        // 移動後のオフセット基準の可視矩形を返す。入力 rectangle のまま返すと
        // ScrollContentPresenter が「まだ見えていない」と判断して MakeVisible を呼び続け、
        // 点滅・操作不能のループになる(PgUp 連打・キー移動時)。
        return new Rect(col * _itemSize.Width, top - _offset.Y, _itemSize.Width, _itemSize.Height);
    }
}
