using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ViewPrism2.Core.Services;

namespace ViewPrism2.App.ViewModels;

/// <summary>
/// チップ行の容量・overflow 状態(ECO-091・IMG-023A=A-b 裁定・CAD VC-IMG-9/10=VC-WORK-2/3)。
/// 画像タブ/作業タブの両ホスト VM が合成する(Trash/Organize サブ VM と同じ流儀)—
/// 意味論はここに単一実装し、チップ行の XAML は両タブ 2 面のまま(E-BOM 同期宣言=ECO-090。
/// UI 部品の共有= LabeledChipStrip 級は golden 後の DRY 判断)。
/// - 通常表示は最大 2 行(ChipRowOverflow.MaxRows)。溢れは「ほか N 件」→ポップオーバー。
/// - N=非表示項目数。幅変更で再計算(View が ResetFold を呼ぶ)。選択・ナビ状態には触れない。
/// - 折畳み時のみ優先配置(クリア→active→定義順)。非折畳み時は元順=1 行時の視覚不変。
/// - 実測供給(実描画矩形)は View の責務(ECO-027 流儀・計算は ChipRowOverflow で unit 検査可能)。
/// </summary>
public sealed partial class ChipStripViewModel : ObservableObject
{
    /// <summary>「ほか N 件」ボタン幅の初期見積り(初回折畳みの席確保)。実測後は直近の実幅を学習する。</summary>
    private const double MoreButtonWidthEstimate = 110;

    private readonly LocalizationService _localization;
    private readonly ObservableCollection<ChipVM> _source;
    private readonly Action<ChipVM> _onChipSelected;

    private int? _visibleCount;
    private double? _measuredMoreWidth;

    public ChipStripViewModel(LocalizationService localization, ObservableCollection<ChipVM> source, Action<ChipVM> onChipSelected)
    {
        _localization = localization;
        _source = source;
        _onChipSelected = onChipSelected;
        source.CollectionChanged += OnSourceChanged;
        RebuildDisplay();
    }

    /// <summary>表示層のアイテム(折畳み後の ChipVM 列+末尾の ChipMoreVM)。</summary>
    public ObservableCollection<object> DisplayItems { get; } = new();

    [ObservableProperty] private bool _popoverOpen;
    [ObservableProperty] private string _searchText = "";
    public ObservableCollection<ChipVM> PopoverItems { get; } = new();
    [ObservableProperty] private bool _popoverEmpty;

    partial void OnSearchTextChanged(string value) => RebuildPopover();

    partial void OnPopoverOpenChanged(bool value)
    {
        if (!value) return;
        SearchText = "";
        RebuildPopover();
    }

    [RelayCommand]
    private void TogglePopover() => PopoverOpen = !PopoverOpen;

    [RelayCommand]
    private void ClosePopover() => PopoverOpen = false;

    /// <summary>ポップオーバー内の選択: 単一選択/ナビをホスト面へ反映して閉じる(VC-IMG-10/VC-WORK-3)。</summary>
    [RelayCommand]
    private void SelectFromPopover(ChipVM chip)
    {
        PopoverOpen = false;
        SearchText = "";
        _onChipSelected(chip);
    }

    /// <summary>
    /// View から実描画レイアウトを受け取り折畳みを更新する(描画後に呼ばれる)。
    /// 非折畳み時= 全チップの矩形から 2 行超過なら可視件数を計算。
    /// 折畳み時= 検証パス(3 行目が出ていれば 1 減らして収束)+「ほか N 件」実幅の学習。
    /// </summary>
    public void ReportLayout(IReadOnlyList<Rect> chipRects, Rect? moreRect, double panelWidth)
    {
        if (_visibleCount is null)
        {
            var k = ChipRowOverflow.ComputeVisibleCount(chipRects, panelWidth, _measuredMoreWidth ?? MoreButtonWidthEstimate);
            if (k is not null)
            {
                _visibleCount = k;
                RebuildDisplay();
            }
            return;
        }
        if (moreRect is { } m) _measuredMoreWidth = m.Width;
        var dec = ChipRowOverflow.VerifyFolded(chipRects, moreRect, _visibleCount.Value);
        if (dec is not null)
        {
            _visibleCount = dec;
            RebuildDisplay();
        }
    }

    /// <summary>
    /// 幅変更・チップ集合変更時の再計算起点(全表示へ戻し、次の描画で測り直す)。選択状態には触れない。
    /// 戻り値=折畳みを解除したか(false なら表示は全チップのままなので、呼び出し側は同一描画パスで計測してよい)。
    /// </summary>
    public bool ResetFold()
    {
        if (_visibleCount is null) return false;
        _visibleCount = null;
        RebuildDisplay();
        return true;
    }

    /// <summary>言語切替への追随(「ほか N 件」ラベルの再生成)。</summary>
    public void OnCultureChanged() => RebuildDisplay();

    private void OnSourceChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // チップ集合が変わったら折畳みは無効(ナビ/フィルタで件数・幅が変わる)。次の描画実測で再計算。
        _visibleCount = null;
        if (PopoverOpen) PopoverOpen = false;
        RebuildDisplay();
    }

    private void RebuildDisplay()
    {
        DisplayItems.Clear();
        if (_visibleCount is int k && k < _source.Count)
        {
            var ordered = ChipRowOverflow.Prioritize([.. _source]);
            var take = Math.Clamp(k, 1, _source.Count - 1);
            foreach (var chip in ordered.Take(take)) DisplayItems.Add(chip);
            var hidden = _source.Count - take;
            DisplayItems.Add(new ChipMoreVM(hidden, _localization.T(
                "chip.moreItems", new Dictionary<string, string> { ["count"] = hidden.ToString() })));
        }
        else
        {
            foreach (var chip in _source) DisplayItems.Add(chip);
        }
        RebuildPopover();
    }

    private void RebuildPopover()
    {
        PopoverItems.Clear();
        if (!PopoverOpen) { PopoverEmpty = false; return; }
        var q = (SearchText ?? "").Trim();
        foreach (var chip in _source)
        {
            if (q.Length == 0 || chip.Label.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                PopoverItems.Add(chip);
            }
        }
        PopoverEmpty = PopoverItems.Count == 0;
    }
}
