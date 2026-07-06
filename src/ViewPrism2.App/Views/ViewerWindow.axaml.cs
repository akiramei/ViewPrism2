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

    private const double ScrollTrackingEpsilon = 0.5;

    /// <summary>
    /// scroll 位置追跡の合流キュー(OC-11/GF-V2)。画像ロードで item 高さ/extent だけが変わるイベントを
    /// ScrollChanged の同期経路で処理すると、TranslatePoint によるレイアウト確定と仮想化再測定が連鎖しうる。
    /// そのため offset/viewport 変化だけを後段 Dispatcher へ合流し、dirty layout の最中に位置追跡しない。
    /// </summary>
    private bool _scrollTrackingQueued;
    private bool _scrollTrackingRunning;
    private bool _scrollTrackingRequested;
    private double? _lastTrackedOffsetY;
    private double? _lastTrackedViewportHeight;

    /// <summary>ウィンドウ生存期間。閉じると寸法先読み(GF-V2)をキャンセルする。</summary>
    private readonly CancellationTokenSource _lifetimeCts = new();
    private bool _dimsPrewarmStarted;

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
            MaybePrewarmDimensions();
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

    /// <summary>
    /// 縦スクロール時のみ・現在 index 近傍順に・Window lifetime のキャンセル付きで 画像寸法を背景先読みする
    /// (GF-V2/①-lite)。scroll item が実体化する前にキャッシュを温め、最初から正しい高さを予約させて
    /// extent の揺れ(=スクロール暴走)を防ぐ。1 度だけ起動し、normal/spread で開いた時は走らせない(P2)。
    /// </summary>
    private void MaybePrewarmDimensions()
    {
        if (_dimsPrewarmStarted || _viewModel is null || !_viewModel.IsScroll)
        {
            return;
        }

        if (ViewerImage.DimensionCache is not { } cache)
        {
            return;
        }

        var items = _viewModel.Items;
        if (items.Count == 0)
        {
            return;
        }

        _dimsPrewarmStarted = true;

        // 現在 index に近い順(閉じても近傍=見られやすい分から温まる)
        var current = _viewModel.CurrentIndex < 0 ? 0 : _viewModel.CurrentIndex;
        var ordered = Enumerable.Range(0, items.Count)
            .OrderBy(i => Math.Abs(i - current))
            .Select(i => items[i].AbsolutePath)
            .Where(p => !string.IsNullOrEmpty(p))
            .ToList();

        _ = cache.PrewarmAsync(ordered, cancellationToken: _lifetimeCts.Token);
    }

    protected override void OnClosed(EventArgs e)
    {
        // 寸法先読みを止める(閉じた後に I/O を残さない — P2)
        _lifetimeCts.Cancel();
        _lifetimeCts.Dispose();
        base.OnClosed(e);
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

    /// <summary>タグ制御マッピングモーダルの暗幕クリックで閉じる(ECO-019 in-tab popup 同型)。</summary>
    private void OnTagControlBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _viewModel?.CloseTagControlMappingCommand.Execute(null);
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
                UpdateSpread();
                break;
            case nameof(ViewerViewModel.Mode):
                UpdateSpread();
                MaybePrewarmDimensions(); // scroll へ切り替えた時に温める(開いた時が scroll でなければここで)
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
            // フルサイズ表示キャッシュ(REQ-045): メモリ LRU 50 枚・TTL 3 分。
            // ECO-049(REQ-085): EXIF Orientation を適用した正立読込(TopLeft は従来の直読)
            var bitmap = await _cache.GetOrAddAsync(path, () => Task.Run(() => Controls.OrientedBitmaps.Load(path)));
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

        // タグ制御 spread 占有: 両列にまたがる単一画像を表示し、左右半面はクリア(§2.12)
        if (_viewModel.CurrentIsSpreadOccupy)
        {
            var occupy = _viewModel.CurrentSpreadOccupyImage;
            SpreadOccupyImage.SourcePath = occupy >= 0 ? PathAt(occupy) : null;
            SpreadLeftImage.SourcePath = null;
            SpreadRightImage.SourcePath = null;
            return;
        }

        SpreadOccupyImage.SourcePath = null;
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
        if (_viewModel is null || !_viewModel.IsScroll || ScrollList.Scroll is null)
        {
            return;
        }

        // 画像ロードや仮想化の実体化/解放で extent だけが動く ScrollChanged は、現在位置追跡の入力にしない。
        // ユーザー操作/ScrollIntoView/リサイズによる offset・viewport 変化だけを後段でまとめて処理する。
        if (!HasMeaningfulScrollTrackingDelta(e))
        {
            return;
        }

        RequestScrollTracking();
    }

    private void RequestScrollTracking()
    {
        _scrollTrackingRequested = true;
        if (_scrollTrackingQueued || _scrollTrackingRunning)
        {
            return;
        }

        _scrollTrackingQueued = true;
        Dispatcher.UIThread.Post(ProcessQueuedScrollTracking, DispatcherPriority.Background);
    }

    private void ProcessQueuedScrollTracking()
    {
        _scrollTrackingQueued = false;
        if (!_scrollTrackingRequested)
        {
            return;
        }

        _scrollTrackingRequested = false;
        _scrollTrackingRunning = true;
        try
        {
            TrackCurrentScrollPosition();
        }
        finally
        {
            _scrollTrackingRunning = false;
        }

        if (_scrollTrackingRequested)
        {
            RequestScrollTracking();
        }
    }

    private void TrackCurrentScrollPosition()
    {
        if (_viewModel is null || !_viewModel.IsScroll || ScrollList.Scroll is not { } scroll)
        {
            return;
        }

        var scrollOffsetY = scroll.Offset.Y;
        var viewportHeight = scroll.Viewport.Height;
        if (_lastTrackedOffsetY is { } lastOffset &&
            _lastTrackedViewportHeight is { } lastViewport &&
            Math.Abs(scrollOffsetY - lastOffset) < ScrollTrackingEpsilon &&
            Math.Abs(viewportHeight - lastViewport) < ScrollTrackingEpsilon)
        {
            return;
        }

        var (indices, rects) = CollectRealizedItemRects(scrollOffsetY);
        if (rects.Count == 0)
        {
            return;
        }

        _lastTrackedOffsetY = scrollOffsetY;
        _lastTrackedViewportHeight = viewportHeight;

        // FindCurrent は渡した rects 内の位置を返す。実 item index は indices で写し戻す。
        var posInSubset = ScrollPositionTracker.FindCurrent(rects, viewportHeight, scrollOffsetY);
        var itemIndex = indices[posInSubset];
        _viewModel.UpdateScrollPositionByIndex(itemIndex);
    }

    private static bool HasMeaningfulScrollTrackingDelta(ScrollChangedEventArgs e) =>
        Math.Abs(e.OffsetDelta.Y) >= ScrollTrackingEpsilon ||
        Math.Abs(e.ViewportDelta.Y) >= ScrollTrackingEpsilon;

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
