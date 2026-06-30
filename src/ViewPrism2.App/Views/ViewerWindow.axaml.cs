using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ViewPrism2.App.Controls;
using ViewPrism2.App.ViewModels;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using ViewPrism2.Core.Services.Viewer;

namespace ViewPrism2.App.Views;

/// <summary>
/// ビューアの View(M-UI-014 / M-UI-018)。ナビゲーション・モード・送り計算は ViewerViewModel
/// (M-VIEWERCORE-017 経由)に集約し、ここは画像ロード・Source 代入・クリック面/スクロールの
/// 物理座標→ViewModel 呼び出しのみを行う(コードビハインドでモード判定をしない — K-AVALONIA)。
/// normal はフルサイズ表示キャッシュ(REQ-045)経由。scroll/spread は ViewerImage が各画像をロードする。
/// </summary>
public partial class ViewerWindow : Window
{
    private readonly ImageMemoryCache _cache;
    private ViewerViewModel? _viewModel;

    // XAML プレビュー/ランタイムローダ用(未使用経路)
    public ViewerWindow()
        : this(new ImageMemoryCache(new Core.Common.SystemClock()))
    {
    }

    public ViewerWindow(ImageMemoryCache cache)
    {
        _cache = cache;
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as ViewerViewModel);
        Opened += (_, _) => ViewerBody.Focus(); // 矢印キーが効くようフォーカスを本体へ(K-AVALONIA v2.0)

        // SHIFT 修飾の保持状態を ViewModel へ(見開きの 1 ページ送り解決。Tunnel で ScrollViewer より先に受ける)
        AddHandler(KeyDownEvent, OnKeyModifier, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnKeyModifier, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        // scroll は仮想化 ListBox(K-AVALONIA v2.0/Run2)。内部 ScrollViewer の ScrollChanged を購読し位置追跡(OC-11)。
        // ContainerClearing(仮想化でコンテナが画面外へリサイクルされる瞬間)で ViewerImage を Release し、
        // Bitmap を破棄・進行中ロードをキャンセルする(= 画面外アイテムは Bitmap を保持しない)。
        ScrollList.AddHandler(ScrollViewer.ScrollChangedEvent, OnScrollChanged);
        ScrollList.ContainerClearing += OnContainerClearing;
    }

    private void Attach(ViewerViewModel? viewModel)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.CloseRequested -= OnCloseRequested;
            _viewModel.CurrentIndexChanged -= OnCurrentIndexChanged;
        }

        _viewModel = viewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.CloseRequested += OnCloseRequested;
            _viewModel.CurrentIndexChanged += OnCurrentIndexChanged;
            ApplyFitMode();
            _ = LoadNormalAsync();
            UpdateSpread();
            Dispatcher.UIThread.Post(ScrollToCurrent, DispatcherPriority.Background);
        }
    }

    /// <summary>
    /// 仮想化コンテナが画面外へリサイクルされる瞬間。当該コンテナ内の ViewerImage を Release し、
    /// Bitmap を描画から外して Dispose・進行中ロードをキャンセルする(K-AVALONIA v2.0/Run2)。
    /// これにより画面外アイテムは Bitmap を保持せず、メモリは画面内+先読み分のみに比例する。
    /// </summary>
    private static void OnContainerClearing(object? sender, ContainerClearingEventArgs e)
    {
        // ListBoxItem(ContentControl)の ContentPresenter.Child が ItemTemplate のルート = ViewerImage。
        if (e.Container is ContentControl { Presenter.Child: ViewerImage image })
        {
            image.Release();
        }
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();

    private void OnKeyModifier(object? sender, KeyEventArgs e)
    {
        // SHIFT 押下中(見開きの 1 ページ送り)。KeyDown は SHIFT 同時押下、KeyUp で SHIFT 解放を反映
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (e.Key is Key.LeftShift or Key.RightShift)
        {
            shift = e.RoutedEvent == KeyDownEvent;
        }

        _viewModel?.SetShift(shift);
    }

    /// <summary>
    /// normal モードの画像外余白クリックで閉じる(REQ-044)。scroll/spread では無効(ViewModel が判定)。
    /// </summary>
    private void OnNormalBackgroundPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel is null || !_viewModel.CanCloseOnBackgroundClick ||
            !e.GetCurrentPoint(NormalHost).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var point = e.GetPosition(NormalImage);
        var source = NormalImage.Source;
        if (ViewerViewModel.IsBackgroundPoint(
                NormalImage.Bounds.Width, NormalImage.Bounds.Height,
                source?.Size.Width ?? 0, source?.Size.Height ?? 0,
                point.X, point.Y))
        {
            _viewModel.CloseCommand.Execute(null);
        }
    }

    /// <summary>見開きの左半面クリック(余白・画像・空白ページを含む面全体 — REQ-057)。</summary>
    private void OnSpreadLeftPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(SpreadLeftHalf).Properties.IsLeftButtonPressed)
        {
            _viewModel?.OnPageClick(isLeftHalf: true);
        }
    }

    private void OnSpreadRightPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(SpreadRightHalf).Properties.IsLeftButtonPressed)
        {
            _viewModel?.OnPageClick(isLeftHalf: false);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ViewerViewModel.CurrentImagePath):
                _ = LoadNormalAsync();
                break;
            case nameof(ViewerViewModel.CurrentSpreadPair):
            case nameof(ViewerViewModel.Mode):
                UpdateSpread();
                break;
            case nameof(ViewerViewModel.FitMode):
                ApplyFitMode();
                break;
        }
    }

    /// <summary>
    /// 単一(normal)のスクロール host のフィット方式を反映する(モック改善)。
    /// Fit はフィット Panel 側(常に Uniform 縮小のみ)。Width=横幅基準(縦スクロール)・One=原寸(両スクロール)。
    /// </summary>
    private void ApplyFitMode()
    {
        if (_viewModel is null)
        {
            return;
        }

        switch (_viewModel.FitMode)
        {
            case FitMode.Width:
                NormalScrollImage.Stretch = Avalonia.Media.Stretch.Uniform;
                NormalScrollImage.StretchDirection = Avalonia.Media.StretchDirection.Both;
                NormalScrollImage.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
                NormalScroll.HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
                NormalScroll.VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
                break;
            case FitMode.One:
                NormalScrollImage.Stretch = Avalonia.Media.Stretch.None;
                NormalScrollImage.StretchDirection = Avalonia.Media.StretchDirection.Both;
                NormalScrollImage.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
                NormalScroll.HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
                NormalScroll.VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
                break;
        }
    }

    private void OnCurrentIndexChanged(object? sender, EventArgs e)
    {
        UpdateSpread();
        ScrollToCurrent();
    }

    private async Task LoadNormalAsync()
    {
        if (_viewModel is null || !_viewModel.IsNormal)
        {
            return;
        }

        var path = _viewModel.CurrentImagePath;
        if (path is null)
        {
            NormalImage.Source = null;
            NormalScrollImage.Source = null;
            return;
        }

        try
        {
            // フルサイズ表示キャッシュ(REQ-045): メモリ LRU 50 枚・TTL 3 分
            var bitmap = await _cache.GetOrAddAsync(path, () => Task.Run(() => new Bitmap(path)));
            if (string.Equals(_viewModel?.CurrentImagePath, path, StringComparison.Ordinal))
            {
                // Fit 用とスクロール(Width/One)用の両 host に同じ Bitmap を割り当てる(切替で再ロードしない)
                NormalImage.Source = bitmap;
                NormalScrollImage.Source = bitmap;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            NormalImage.Source = null; // 壊れた画像・消失でもクラッシュしない
            NormalScrollImage.Source = null;
        }
    }

    /// <summary>見開きの両面に CurrentSpreadPair の画像パスを割り当てる(空白=null=無地)。</summary>
    private void UpdateSpread()
    {
        if (_viewModel is null || !_viewModel.IsSpread)
        {
            return;
        }

        var pair = _viewModel.CurrentSpreadPair;
        SpreadLeftImage.SourcePath = PathAt(pair.LeftIndex);
        SpreadRightImage.SourcePath = PathAt(pair.RightIndex);
    }

    private string? PathAt(int? index)
    {
        if (index is not { } i || _viewModel is null || i < 0 || i >= _viewModel.Items.Count)
        {
            return null;
        }

        return _viewModel.Items[i].AbsolutePath;
    }

    // ---- scroll の位置追跡(OC-11)と復元 ----
    // 仮想化下では実体化コンテナは可視+先読み分のみの疎な部分集合。FindCurrent には
    // item index 昇順で並べた実体化コンテナの矩形(content 座標)を渡し、戻り値(部分集合内の位置)を
    // 実 item index へ写し戻す。これにより「総枚数に依存しない数の矩形」だけを使って中央最近傍を求める。

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_viewModel is null || !_viewModel.IsScroll || ScrollList.Scroll is not { } scroll)
        {
            return;
        }

        var scrollOffsetY = scroll.Offset.Y;
        var (indices, rects) = CollectRealizedItemRects(scrollOffsetY);
        if (rects.Count == 0)
        {
            return;
        }

        // FindCurrent は渡した rects 内の位置を返す。実 item index は indices で写し戻す。
        var posInSubset = ScrollPositionTracker.FindCurrent(rects, scroll.Viewport.Height, scrollOffsetY);
        var itemIndex = indices[posInSubset];
        _viewModel.UpdateScrollPositionByIndex(itemIndex);
    }

    /// <summary>
    /// 実体化済みコンテナの (item index 昇順) と、それぞれの content 座標での (Top, Height) を集める。
    /// Top は viewport 相対の Y に scrollOffset を足して content 座標へ揃える(FindCurrent の
    /// viewportCenter = scrollOffset + viewportHeight/2 と同じ座標系にする)。
    /// </summary>
    private (List<int> Indices, List<(double Top, double Height)> Rects) CollectRealizedItemRects(double scrollOffsetY)
    {
        var realized = new List<(int Index, double Top, double Height)>();
        foreach (var container in ScrollList.GetRealizedContainers())
        {
            var index = ScrollList.IndexFromContainer(container);
            if (index < 0)
            {
                continue;
            }

            // コンテナ上端を ScrollList(viewport)座標へ変換し、scrollOffset を足して content 座標にする
            var topInViewport = container.TranslatePoint(default, ScrollList)?.Y ?? container.Bounds.Top;
            realized.Add((index, topInViewport + scrollOffsetY, container.Bounds.Height));
        }

        realized.Sort((a, b) => a.Index.CompareTo(b.Index));

        var indices = new List<int>(realized.Count);
        var rects = new List<(double, double)>(realized.Count);
        foreach (var (index, top, height) in realized)
        {
            indices.Add(index);
            rects.Add((top, height));
        }

        return (indices, rects);
    }

    /// <summary>現在 index の画像を表示領域へ(モード復元・キー移動。仮想化対応で ScrollIntoView を使う)。</summary>
    private void ScrollToCurrent()
    {
        if (_viewModel is null || !_viewModel.IsScroll)
        {
            return;
        }

        var index = _viewModel.CurrentIndex;
        if (index < 0 || index >= _viewModel.Items.Count)
        {
            return;
        }

        // 仮想化下では対象が未実体化でも実体化してスクロールする(画像単位の粒度 — 仕様 §4)
        ScrollList.ScrollIntoView(index);
    }
}
