using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ViewPrism2.App.ViewModels;

namespace ViewPrism2.App.Views;

/// <summary>
/// 画像タブ製造(M2)の golden ハーネス View。シード VM(<see cref="ImageTabSeedViewModel"/>)を駆動し、
/// Components.axaml の画像タブ部品(M1)をモック準拠で描画する。選択は修飾キーを読むため
/// PointerPressed を code-behind で処理(タグタブ TagsTabView と同じ流儀)。
/// </summary>
public partial class ImageTabView : UserControl
{
    private readonly DoubleClickDetector _doubleClick = new();

    /// <summary>ツールバー Border の水平パディング(Padding="18,0" の左右計)。実測幅から控除して content 幅を得る。</summary>
    private const double ToolbarHorizontalPadding = 36;
    /// <summary>単一行に左右クラスタを収める際の最小間隔。これを割ると tier3 回り込みへ。</summary>
    private const double ClusterGap = 24;
    /// <summary>回り込みしきい値近傍のばたつき防止(戻すのは余裕が band を超えたとき)。</summary>
    private const double WrapHysteresis = 24;

    private Border? _toolbarRoot;
    private Panel? _leftCluster;
    private Panel? _rightCluster;

    private ScrollViewer? _gridScroll;
    private ItemsRepeater? _gridRepeater;
    private ImageTabViewModel? _hookedVm;
    private bool _lastShowRightPane;
    private int _trackedAnchorIndex = -1;
    private int _pendingAnchorIndex = -1;

    public ImageTabView()
    {
        InitializeComponent();

        // IMG-014: ツールバーの狭幅レスポンシブ収納。判定はビューポート幅でなく「ツールバー実測幅」で行う
        // (左ペイン折り畳み・右ペイン開閉で使える幅が変わる)。ResizeObserver 相当として、レイアウト確定ごとに
        // 実測して VM へ反映する。ラベル畳み/退避(段階収納)は content 幅、tier3 回り込みは左右クラスタ自然幅の
        // 合算で判定。View 側は状態を持たず VM を真実源にするため、VM 差し替え時も自己修復する。
        // チップ行(ECO-091 容量契約)の実測供給は共有部品 LabeledChipStrip 側(ECO-094)。
        _toolbarRoot = this.FindControl<Border>("ToolbarRoot");
        _leftCluster = this.FindControl<Panel>("LeftCluster");
        _rightCluster = this.FindControl<Panel>("RightCluster");

        // ECO-110: 右ペイン開閉のグリッドアンカー(CAD layoutInvariant「右ペインの開閉が中央を壊さない」の
        // 保存対象=可視コンテンツ)。VM は一括通知(string.Empty)のため ShowRightPane のエッジは View 側で検出する。
        _gridScroll = this.FindControl<ScrollViewer>("BrowseGridScroll");
        _gridRepeater = this.FindControl<ItemsRepeater>("BrowseGridRepeater");
        DataContextChanged += (_, _) => HookVm();
        HookVm();

        LayoutUpdated += (_, _) =>
        {
            EvaluateToolbar();
            if (_pendingAnchorIndex >= 0) RestoreGridAnchor();
            else TrackGridAnchor();
        };
    }

    private ImageTabViewModel? Vm => DataContext as ImageTabViewModel;

    /// <summary>VM 差し替えに自己修復で追随して PropertyChanged を購読し直す(IMG-014 と同じ流儀)。</summary>
    private void HookVm()
    {
        if (_hookedVm is { } old) old.PropertyChanged -= OnVmPropertyChanged;
        _hookedVm = Vm;
        _lastShowRightPane = _hookedVm?.ShowRightPane ?? false;
        _trackedAnchorIndex = -1;
        _pendingAnchorIndex = -1;
        if (_hookedVm is { } vm) vm.PropertyChanged += OnVmPropertyChanged;
    }

    /// <summary>
    /// ECO-110: 開閉エッジの検出。モード切替は Recompute で Items を再構築してから一括通知するため、
    /// 通知時点では旧段組のセルが既に破棄されている — アンカーはここでは実測せず、直前のレイアウト確定時に
    /// 追跡済みの index(TrackGridAnchor)を採用する。
    /// </summary>
    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (Vm is not { } vm) return;
        var show = vm.ShowRightPane;
        if (show == _lastShowRightPane) return;
        _lastShowRightPane = show;
        _pendingAnchorIndex = _trackedAnchorIndex;
    }

    /// <summary>
    /// ECO-110: レイアウト確定ごとに先頭完全可視アイテム(ビューポート上端以下で最上段・最左の実体化セル)の
    /// index を追跡する(実体化セル数だけの走査= EvaluateToolbar と同水準の毎レイアウト評価)。
    /// 先頭表示中(offset 0)は補正不要なので -1(余計なオフセット注入を避ける)。
    /// </summary>
    private void TrackGridAnchor()
    {
        _trackedAnchorIndex = -1;
        if (Vm is not { ShowBrowseGrid: true } || _gridScroll is not { } scroll || _gridRepeater is not { } rep) return;
        if (scroll.Offset.Y <= 0) return;

        Control? best = null;
        var bestPos = new Point(double.MaxValue, double.MaxValue);
        foreach (var child in rep.Children)
        {
            if (!child.IsVisible) continue;
            if (child.TranslatePoint(new Point(0, 0), scroll) is not { } pt || pt.Y < -0.5) continue;
            if (best is null || pt.Y < bestPos.Y - 0.5 ||
                (Math.Abs(pt.Y - bestPos.Y) <= 0.5 && pt.X < bestPos.X))
            {
                best = child;
                bestPos = pt;
            }
        }
        if (best is { } el)
        {
            var idx = rep.GetElementIndex(el);
            if (idx >= 0) _trackedAnchorIndex = idx;
        }
    }

    /// <summary>
    /// ECO-110: 開閉後の最初のレイアウト確定で、記録済みアンカーをビューポート先頭へ戻す。
    /// 仮想化で実体化から外れていても GetOrCreateElement で実体化してから位置を実測する。
    /// 補正は一度きり(失敗時も再試行しない=毎レイアウト評価での発振防止・IMG-014 ヒステリシスと同趣旨)。
    /// </summary>
    private void RestoreGridAnchor()
    {
        if (_pendingAnchorIndex < 0) return;
        var idx = _pendingAnchorIndex;
        _pendingAnchorIndex = -1;
        if (_gridScroll is not { } scroll || _gridRepeater is not { } rep) return;

        var el = rep.TryGetElement(idx) ?? rep.GetOrCreateElement(idx);
        if (el is null) return;
        rep.UpdateLayout();
        if (el.TranslatePoint(new Point(0, 0), scroll) is { } pt && Math.Abs(pt.Y) > 0.5)
            scroll.Offset = new Vector(scroll.Offset.X, Math.Max(0, scroll.Offset.Y + pt.Y));
    }

    /// <summary>
    /// レイアウト確定後にツールバー実測幅で段階収納(ラベル/退避)と tier3 回り込みを更新する。
    /// VM 側メソッドは変化時のみ通知するので、毎レイアウトの評価でも実質コストは無視できる。
    /// </summary>
    private void EvaluateToolbar()
    {
        if (Vm is not { } vm || _toolbarRoot is not { } tb) return;
        var available = tb.Bounds.Width - ToolbarHorizontalPadding;
        if (available <= 0) return;

        // 段階収納(ラベル畳み <約820 / 整理退避 <約640): content 幅で判定。
        vm.ReportToolbarWidth(available);

        // tier3 回り込み: 左右クラスタが単一行に収まらない(自然幅合算 > 使える幅)なら右クラスタを下段へ。
        // 左=WrapPanel(内部折り返しあり)なので子合算で自然幅を安定化。右=StackPanel(折り返さない)なので
        // DesiredSize.Width が Spacing 込みの自然幅として安定。
        var need = NaturalWidth(_leftCluster) + (_rightCluster?.DesiredSize.Width ?? 0) + ClusterGap;
        var wrapped = vm.ToolbarWrapped; // VM を真実源に(差し替え自己修復)
        if (!wrapped && need > available) wrapped = true;
        else if (wrapped && need < available - WrapHysteresis) wrapped = false;
        vm.SetToolbarWrapped(wrapped); // 冪等(変化時のみ通知)
    }

    /// <summary>
    /// クラスタの「単一行に並べた時の自然幅」。可視な直下子の DesiredSize.Width(Margin 込み)の合算で求める。
    /// パネル自身の DesiredSize は WrapPanel だと折り返し後の幅(≒制約幅)になり不安定なため、子合算で安定させる
    /// (回り込み判定が配置に依存して発振しないための肝)。
    /// </summary>
    private static double NaturalWidth(Panel? panel)
    {
        if (panel is null) return 0;
        double w = 0;
        foreach (var child in panel.Children)
            if (child.IsVisible)
                w += child.DesiredSize.Width;
        return w;
    }

    private void OnCollectionPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: CollectionRowVM row } && Vm is { } vm)
            vm.SelectCollectionCommand.Execute(row.Id);
    }

    private void OnItemPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: ImageItemVM item } control && Vm is { } vm &&
            e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
            var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            // ダブルクリック判定は ClickCount に加え自前検出で補完する(DF-4 堅牢化・原典と同流儀)
            var detected = _doubleClick.ObserveClick(item, (long)e.Timestamp, SystemDoubleClickTimeMs, ctrl || shift);
            vm.HandleItemClick(item, ctrl, shift, e.ClickCount >= 2 || detected);
        }
    }

    /// <summary>OS のダブルクリック時間(ms)。本アプリは Windows 専用(仕様 §1)。</summary>
    private static double SystemDoubleClickTimeMs
    {
        get
        {
            try { return GetDoubleClickTime(); }
            catch (EntryPointNotFoundException) { return 500; }
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    private void OnAddRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: AddRowVM row } && Vm is { } vm)
            vm.ClickAddRowCommand.Execute(row);
    }

    private void OnValueChipPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: ValueChipVM chip } && Vm is { } vm)
            vm.ApplyTextValueCommand.Execute(chip);
    }

    private void OnNumCellPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: NumCellVM cell } && Vm is { } vm)
            vm.ApplyRatingCommand.Execute(cell);
    }

    private void OnMenuClosed(object? sender, System.EventArgs e) => Vm?.CloseMenusFromDismiss();
}

