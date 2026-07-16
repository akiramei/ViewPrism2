using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ViewPrism2.App.Services;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;

namespace ViewPrism2.App.ViewModels;

/// <summary>
/// タグパレットの 1 行(色+名前+型チップ+候補値/範囲+編集/削除、ECO-009/E-UI-TAGS-026)。
/// ECO-009: テキスト型は候補値チップ、数値型は範囲ピル+刻みを提示する(モック CAD 準拠)。
/// </summary>
public sealed partial class TagPaletteRowViewModel : ObservableObject
{
    /// <summary>「ほか {n} 件」ラベル(Loc 解決済みで受け取る=K-AVALONIA 直書き禁止・ChipVM.UndefLabel と同流儀)。</summary>
    private readonly Func<int, string> _moreLabel;

    private int? _candidateVisibleCount;
    private double _lastCandidateWidth;
    private double? _measuredMoreWidth;

    public TagPaletteRowViewModel(
        Tag tag, string typeText, IReadOnlyList<string> predefinedValues, NumericTagSettings? numeric,
        Func<int, string> moreLabel)
    {
        Tag = tag;
        TypeText = typeText;
        CandidateValues = predefinedValues;
        RangeText = BuildRangeText(numeric);
        StepValue = numeric?.Step is { } step ? FormatNum(step) : null;
        _moreLabel = moreLabel;
        RebuildCandidateDisplay();
    }

    public Tag Tag { get; }

    public string TypeText { get; }

    public string Name => Tag.Name;

    // ECO-007/E2(DC-TAGPALETTE-001/DE-4 撤回): タグパレット行に説明を出さない。
    // Tag.Description はデータとして残し、作成/編集ダイアログでのみ参照する(行 VM では公開しない)。

    public string? Color => Tag.Color;

    /// <summary>color=NULL のタグは境界線色のリング表示(K-DESIGN)。</summary>
    public bool HasColor => Tag.Color is not null;

    /// <summary>色ドットの淡色リング(ECO-099・mock dotStyle boxShadow 16% α)。#RRGGBB → #29RRGGBB。</summary>
    public string? RingColor => Tag.Color is ['#', .. { Length: 6 }] c ? "#29" + c[1..] : null;

    public bool IsSimple => Tag.Type == TagType.Simple;

    public bool IsTextual => Tag.Type == TagType.Textual;

    public bool IsNumeric => Tag.Type == TagType.Numeric;

    /// <summary>テキスト型の候補値(順序保持)。ECO-009: パレット行に候補値チップで提示。</summary>
    public IReadOnlyList<string> CandidateValues { get; }

    public bool HasCandidateValues => IsTextual && CandidateValues.Count > 0;

    /// <summary>
    /// 候補値プレビューの表示層(ECO-092/REQ 候補・TAG-013=T-a・CAD VC-TAG-10):
    /// 最大 2 行に折り畳んだ候補値(定義順のまま=動的並べ替えなし)+末尾の非対話 ChipMoreVM。
    /// 全数の確認・編集は鉛筆→タグ編集ダイアログに一本化(展開/ポップオーバーは設けない)。
    /// </summary>
    public ObservableCollection<object> CandidateDisplay { get; } = [];

    /// <summary>
    /// View から候補値行の実描画レイアウトを受け取り折畳みを更新する(ECO-091 ChipStripViewModel と同流儀・
    /// 計算は ChipRowOverflow=unit 検査可能)。幅変更時は全表示へ戻して測り直す(選択状態に副作用なし)。
    /// </summary>
    public void ReportCandidateLayout(IReadOnlyList<Avalonia.Rect> chipRects, Avalonia.Rect? moreRect, double panelWidth)
    {
        // GF-092-02: パレットは ScrollViewer 内=折畳みでカード高さが変わるとバーが出没し
        // 幅が ±16px 級で変わる。微小変化で折畳みを解除すると「解除→伸長→バー出現→幅減→再折畳み→…」の
        // 発振で UI が沈黙的に重くなる(実機=en 切替で顕在化)— バー幅より大きいヒステリシスで縁を切る。
        // 縮小側の 2 行契約はヒステリシス内でも検証パス(VerifyFolded)が担保する。
        const double WidthHysteresis = 24;
        if (Math.Abs(panelWidth - _lastCandidateWidth) > WidthHysteresis)
        {
            _lastCandidateWidth = panelWidth;
            if (_candidateVisibleCount is not null)
            {
                _candidateVisibleCount = null;
                RebuildCandidateDisplay();
                return; // 全表示へ戻した=矩形が古い。次の描画パスで測り直す
            }
        }
        if (_candidateVisibleCount is null)
        {
            var k = ChipRowOverflow.ComputeVisibleCount(chipRects, panelWidth, _measuredMoreWidth ?? 74);
            if (k is not null)
            {
                _candidateVisibleCount = k;
                RebuildCandidateDisplay();
            }
            return;
        }
        if (moreRect is { } m) _measuredMoreWidth = m.Width;
        var dec = ChipRowOverflow.VerifyFolded(chipRects, moreRect, _candidateVisibleCount.Value);
        if (dec is not null)
        {
            _candidateVisibleCount = dec;
            RebuildCandidateDisplay();
        }
    }

    private void RebuildCandidateDisplay()
    {
        CandidateDisplay.Clear();
        if (!HasCandidateValues)
        {
            return;
        }
        if (_candidateVisibleCount is int k && k < CandidateValues.Count)
        {
            var take = Math.Clamp(k, 1, CandidateValues.Count - 1);
            foreach (var v in CandidateValues.Take(take))
            {
                CandidateDisplay.Add(v);
            }
            CandidateDisplay.Add(new ChipMoreVM(CandidateValues.Count - take, _moreLabel(CandidateValues.Count - take)));
        }
        else
        {
            foreach (var v in CandidateValues)
            {
                CandidateDisplay.Add(v);
            }
        }
    }

    /// <summary>数値型の範囲表示(例: "1–5 ★")。null は範囲表示なし。</summary>
    public string? RangeText { get; }

    public bool HasRange => IsNumeric && RangeText is not null;

    /// <summary>数値型の刻み値(例: "1")。null は刻み表示なし。</summary>
    public string? StepValue { get; }

    public bool HasStep => IsNumeric && StepValue is not null;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>配置モード中のカード強調(ECO-099・VC-TAG-12①)。</summary>
    [ObservableProperty]
    private bool _isPlacing;

    /// <summary>
    /// 範囲ラベル: "{min}–{max}"(+単位)。min/max・単位とも無ければ null。INV-007 不変表現。
    /// ECO-099: 階層行の数値メタ(mock node.meta)と共用するため internal(表現を二重定義しない)。
    /// </summary>
    internal static string? BuildRangeText(NumericTagSettings? n)
    {
        if (n is null)
        {
            return null;
        }

        var min = FormatNum(n.Min);
        var max = FormatNum(n.Max);
        if (min is null && max is null && string.IsNullOrEmpty(n.Unit))
        {
            return null;
        }

        var range = $"{min ?? "—"}–{max ?? "—"}";
        return string.IsNullOrEmpty(n.Unit) ? range : $"{range} {n.Unit}";
    }

    private static string? FormatNum(double? v)
    {
        if (v is not { } d)
        {
            return null;
        }

        return d == Math.Floor(d)
            ? ((long)d).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : d.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// タグタブ右「タグパレット」(M-UI-013 v1.2、E-UI-TAGS-026、G-6)。
/// 検索(名前の部分一致・大文字小文字無視)・「追加」→タグ作成ダイアログ・一覧(編集/削除)。
/// 階層エディタへの D&D/ボタン追加のドラッグ元。
/// </summary>
public sealed partial class TagPaletteViewModel : ObservableObject
{
    private readonly TagService _tagService;
    private readonly LocalizationService _localization;
    private readonly IWindowService _windows;
    private List<PaletteTagItem> _all = [];

    public TagPaletteViewModel(TagService tagService, LocalizationService localization, IWindowService windows)
    {
        _tagService = tagService;
        _localization = localization;
        _windows = windows;
        Loc = new LocalizationProxy(localization);
        localization.CultureChanged += (_, _) =>
        {
            // DF-3: Loc 差し替えで全文言バインディングを再評価させる(K-AVALONIA の罠対策)
            Loc = new LocalizationProxy(localization);
            OnPropertyChanged(nameof(Loc));
            ApplyFilter();
        };
    }

    public LocalizationProxy Loc { get; private set; }

    public ObservableCollection<TagPaletteRowViewModel> Tags { get; } = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private TagPaletteRowViewModel? _selectedTag;

    /// <summary>配置モード中のタグ id(ECO-099)。エディタ所有の状態をホストが同期し、カード強調に使う。</summary>
    [ObservableProperty]
    private string? _placingTagId;

    [ObservableProperty]
    private string? _statusMessage;

    public bool IsEmpty => Tags.Count == 0;

    /// <summary>絞り込み後の件数(ECO-009: 件数/凡例行)。</summary>
    public int ItemCount => Tags.Count;

    /// <summary>全件数(検索前)。</summary>
    public int TotalCount => _all.Count;

    /// <summary>"{count}/{total} アイテム"(ECO-009)。i18n キー経由。</summary>
    public string ItemCountText => _localization.T("tag.palette.itemCount", new Dictionary<string, string>
    {
        ["count"] = ItemCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["total"] = TotalCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
    });

    /// <summary>タグの作成・編集・削除があった(シェル・エディタの再読込用)。</summary>
    public event EventHandler? TagsChanged;

    /// <summary>
    /// 未保存の階層編集に載っているタグか(REQ-083/ECO-046 U-a)。ホスト(TagsTabViewModel)が
    /// エディタ状態への判定を配線する。DB 参照ガード(TagService=ECO-045)は未コミットの
    /// 編集状態を関知できないため、この UI 層判定が谷間を塞ぐ。
    /// </summary>
    public Func<string, bool>? IsTagInUnsavedEdit { get; set; }

    public async Task LoadAsync()
    {
        // 一覧は name 昇順(REQ-029)。ECO-009: 候補値/数値範囲を含めて取得しパレット行に提示
        _all = (await _tagService.GetPaletteItemsAsync()).ToList();
        ApplyFilter();
    }

    [RelayCommand]
    private async Task NewTagAsync()
    {
        if (await _windows.ShowTagEditorAsync(null))
        {
            await LoadAsync();
            TagsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    private async Task EditAsync(TagPaletteRowViewModel row)
    {
        if (await _windows.ShowTagEditorAsync(row.Tag))
        {
            await LoadAsync();
            TagsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(TagPaletteRowViewModel row)
    {
        // ECO-046(U-a 裁定): 未保存の階層編集に載っているタグは確認ダイアログの前に拒否(TAG-008 の外延)
        if (IsTagInUnsavedEdit?.Invoke(row.Tag.Id) == true)
        {
            StatusMessage = _localization.T("error.tagInUnsavedEdit");
            return;
        }

        var message = _localization.T("tag.deleteTagConfirmation", new Dictionary<string, string>
        {
            ["tagName"] = row.Tag.Name,
        });
        if (!await _windows.ConfirmAsync(_localization.T("tag.deleteTag"), message))
        {
            return;
        }

        var result = await _tagService.DeleteAsync(row.Tag.Id);
        StatusMessage = result.IsSuccess ? null : ErrorMessages.Resolve(_localization, result.Error);
        await LoadAsync();
        TagsChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSelectedTagChanged(TagPaletteRowViewModel? value)
    {
        foreach (var row in Tags)
        {
            row.IsSelected = ReferenceEquals(row, value);
        }
    }

    partial void OnPlacingTagIdChanged(string? value) => SyncPlacing();

    /// <summary>配置中カードの強調フラグを同期する(検索での行再生成後も維持=ApplyFilter 末尾からも呼ぶ)。</summary>
    private void SyncPlacing()
    {
        foreach (var row in Tags)
        {
            row.IsPlacing = string.Equals(row.Tag.Id, PlacingTagId, StringComparison.Ordinal);
        }
    }

    private void ApplyFilter()
    {
        var selectedId = SelectedTag?.Tag.Id;
        Tags.Clear();
        foreach (var item in _all)
        {
            // 検索: 名前の部分一致・大文字小文字無視(仕様 §2.6)
            if (SearchText.Length > 0 && !item.Tag.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Tags.Add(new TagPaletteRowViewModel(
                item.Tag,
                _localization.T(item.Tag.Type switch
                {
                    TagType.Simple => "tag.type.simple",
                    TagType.Textual => "tag.type.textual",
                    _ => "tag.type.numeric",
                }),
                item.PredefinedValues,
                item.Numeric,
                // ECO-092(TAG-013=T-a): 「ほか {n} 件」は Loc 解決済みで行 VM へ(culture 変更は ApplyFilter 再実行で追随)
                n => _localization.T("chip.moreItems", new Dictionary<string, string> { ["count"] = n.ToString(System.Globalization.CultureInfo.InvariantCulture) })));
        }

        SelectedTag = selectedId is null
            ? null
            : Tags.FirstOrDefault(t => string.Equals(t.Tag.Id, selectedId, StringComparison.Ordinal));
        SyncPlacing();
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(ItemCount));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(ItemCountText));
    }
}
