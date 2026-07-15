using System;
using Avalonia.Controls;
using Avalonia.Input;
using ViewPrism2.App.ViewModels;

namespace ViewPrism2.App.Views;

/// <summary>
/// 固定クローム チップ行の共有部品(ECO-094・SC-CHIPSTRIP-001)。DataContext はホスト VM
/// (<see cref="IChipStripHost"/>)を継承する。旧 ImageTabView/WorkTabView code-behind の
/// バイト一致重複(EvaluateChipRow ほか)をここへ単一化 — 意味論は ChipStripViewModel/
/// ChipRowOverflow(ECO-091)のまま、実測供給(実描画矩形)だけを部品が担う(ECO-027 流儀)。
/// </summary>
public partial class LabeledChipStrip : UserControl
{
    private ItemsControl? _chipDisplay;
    private Avalonia.Controls.Primitives.Popup? _chipPopover;
    private TextBox? _chipSearchBox;
    private double _lastChipPanelWidth;

    public LabeledChipStrip()
    {
        InitializeComponent();

        _chipDisplay = this.FindControl<ItemsControl>("ChipDisplay");
        _chipPopover = this.FindControl<Avalonia.Controls.Primitives.Popup>("ChipPopover");
        _chipSearchBox = this.FindControl<TextBox>("ChipSearchBox");
        if (_chipPopover is { } pop)
        {
            // 開いたら検索欄へフォーカス(VC-IMG-10/VC-WORK-3 キーボード契約: 検索欄→一覧の順)
            pop.Opened += (_, _) => _chipSearchBox?.Focus();
        }
        LayoutUpdated += (_, _) => EvaluateChipRow();
    }

    private IChipStripHost? Host => DataContext as IChipStripHost;

    /// <summary>
    /// ECO-091(IMG-023A=A-b): チップ行の実描画からの折畳み評価。全表示時に 3 行目が出るなら
    /// ChipStripViewModel が可視件数を計算して「ほか N 件」へ畳む。幅変更時は全表示へ戻して測り直す
    /// (表示件数と N の再計算=選択・ナビ状態は不変)。計算は ChipRowOverflow(unit 検査可能)。
    /// </summary>
    private void EvaluateChipRow()
    {
        if (Host is not { } host || !host.ShowChips) return;
        var panel = _chipDisplay?.ItemsPanelRoot;
        if (panel is null || panel.Bounds.Width <= 0) return;

        var width = panel.Bounds.Width;
        if (Math.Abs(width - _lastChipPanelWidth) > 0.5)
        {
            _lastChipPanelWidth = width;
            // 折畳み中だった場合のみ次パスへ(全表示へ戻した=矩形が古い)。未折畳みなら同一パスで計測可
            if (host.ChipStrip.ResetFold()) return;
        }

        var chipRects = new System.Collections.Generic.List<Avalonia.Rect>();
        Avalonia.Rect? moreRect = null;
        foreach (var child in panel.Children)
        {
            if (!child.IsVisible) continue;
            if (child.DataContext is ChipVM) chipRects.Add(child.Bounds);
            else if (child.DataContext is ChipMoreVM) moreRect = child.Bounds;
        }
        host.ChipStrip.ReportLayout(chipRects, moreRect, width);
    }

    /// <summary>通常領域チップの直接クリック(ジェスチャ起点は direct ハンドラ=ECO-087 教訓)。</summary>
    private void OnChipPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: ChipVM chip } && Host is { } host)
            host.ClickChip(chip);
    }

    /// <summary>Escape でポップオーバーを閉じ「ほか N 件」へフォーカスを戻す(VC-IMG-10/VC-WORK-3)。</summary>
    private void OnChipPopoverKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || Host is not { } host) return;
        host.ChipStrip.ClosePopoverCommand.Execute(null);
        FocusChipMoreButton();
        e.Handled = true;
    }

    private void FocusChipMoreButton()
    {
        if (_chipDisplay is null) return;
        foreach (var btn in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(_chipDisplay))
        {
            if (btn is Button b && b.Classes.Contains("chipMore")) { b.Focus(); return; }
        }
    }
}
