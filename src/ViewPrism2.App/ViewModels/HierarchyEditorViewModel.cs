using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ViewPrism2.App.Services;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;

namespace ViewPrism2.App.ViewModels;

/// <summary>
/// 階層エディタの編集中ノード(メモリ内、仕様 §2.6 v1.2)。
/// 保存(バッチコミット)までは DB へ書かない。alias=null なら tag.name を表示(REQ-034)。
/// </summary>
public sealed partial class EditNodeViewModel : ObservableObject
{
    private readonly LocalizationService? _localization;

    public EditNodeViewModel(string id, string tagId, Tag? tag, LocalizationService? localization = null)
    {
        Id = id;
        TagId = tagId;
        Tag = tag;
        _localization = localization;
        // ECO-099: 行テンプレートの子数バッジ/子ブロック表示は Children の増減へ追随する
        Children.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasChildren));
            OnPropertyChanged(nameof(ChildCount));
            OnPropertyChanged(nameof(ShowChildren));
            OnPropertyChanged(nameof(ShowLeafSpacer));
        };
    }

    /// <summary>階層ノード id(既存ノードは DB の id、新規ノードは採番済み)。</summary>
    public string Id { get; }

    public string TagId { get; }

    /// <summary>参照先タグ(参照切れは null — INV-008: 表示は TagId、削除可能)。</summary>
    public Tag? Tag { get; }

    public EditNodeViewModel? Parent { get; set; }

    public ObservableCollection<EditNodeViewModel> Children { get; } = [];

    [ObservableProperty]
    private string? _alias;

    /// <summary>ホームタグ(REQ-037)。設定中ノードは強調表示(G-6)。</summary>
    [ObservableProperty]
    private bool _isHome;

    /// <summary>展開/折畳(仕様 §2.6 のノード操作。UI 状態のみ、保存対象外)。</summary>
    [ObservableProperty]
    private bool _isExpanded = true;

    /// <summary>別名のインライン編集中。</summary>
    [ObservableProperty]
    private bool _isEditingAlias;

    [ObservableProperty]
    private string _aliasEditText = string.Empty;

    public HierarchyConditionType? ConditionType { get; private set; }

    public string? ConditionValue { get; private set; }

    /// <summary>展開モード(REQ-096/ECO-086)。既定 Observed=従来挙動。</summary>
    public HierarchyExpansionMode ExpansionMode { get; private set; } = HierarchyExpansionMode.Observed;

    /// <summary>0 件の値ノードを隠す(REQ-096/裁定 d)。</summary>
    public bool HideEmptyValues { get; private set; }

    public string DisplayName => Alias ?? Tag?.Name ?? TagId;

    /// <summary>条件設定は textual/numeric のみ(仕様 §2.6)。</summary>
    public bool CanHaveCondition => Tag?.Type is TagType.Textual or TagType.Numeric;

    public bool HasCondition => ConditionType is not null;

    // ---- ECO-009 ③: 行の視覚要素(色ドット/型チップ/条件チップ配色) ----
    public string? Color => Tag?.Color;

    public bool HasColor => Tag?.Color is not null;

    public bool HasType => Tag is not null;

    public bool IsSimple => Tag?.Type == TagType.Simple;

    public bool IsTextual => Tag?.Type == TagType.Textual;

    public bool IsNumeric => Tag?.Type == TagType.Numeric;

    public string TypeText => _localization is null || Tag is null
        ? string.Empty
        : _localization.T(Tag.Type switch
        {
            TagType.Simple => "tag.type.simple",
            TagType.Textual => "tag.type.textual",
            _ => "tag.type.numeric",
        });

    // ECO-099(VC-TAG-13⑤): 条件チップは琥珀単一へ集約 — 旧 3 配色プロパティ(IsConditionAmber/Mono)は撤去。

    /// <summary>
    /// 条件サマリの整形表示(GF-05・REQ-060(e)、仕様 §2.6)。i18n テンプレートで人間可読化し、
    /// 生 JSON や Unicode エスケープを露出しない(ConditionSummaryFormatter)。
    /// localization 未注入(後方互換)時はサマリなし(空文字列)。
    /// </summary>
    public string ConditionSummary => _localization is null
        ? string.Empty
        : ConditionSummaryFormatter.Format(ConditionType, ConditionValue, _localization);

    public void SetCondition(HierarchyConditionType? type, string? valueJson)
    {
        ConditionType = type;
        ConditionValue = type is null ? null : valueJson;
        OnPropertyChanged(nameof(HasCondition));
        OnPropertyChanged(nameof(ConditionSummary));
    }

    /// <summary>展開モードの設定(REQ-096)。行バッジ(非既定のみ表示=CAD VC-TAG-3)を再評価する。</summary>
    public void SetExpansion(HierarchyExpansionMode mode, bool hideEmptyValues)
    {
        ExpansionMode = mode;
        HideEmptyValues = hideEmptyValues;
        OnPropertyChanged(nameof(HasExpansionBadge));
        OnPropertyChanged(nameof(ExpansionBadgeText));
    }

    /// <summary>展開バッジは非既定(観測値以外)のときだけ表示(ノイズ回避=CAD VC-TAG-3)。</summary>
    public bool HasExpansionBadge => ExpansionMode != HierarchyExpansionMode.Observed;

    public string ExpansionBadgeText
    {
        get
        {
            if (_localization is null)
            {
                return string.Empty;
            }

            var mode = _localization.T(ExpansionMode switch
            {
                HierarchyExpansionMode.Manual => "hierarchy.expansionMode.manual",
                HierarchyExpansionMode.Defined => "hierarchy.expansionMode.defined",
                HierarchyExpansionMode.DefinedAndObserved => "hierarchy.expansionMode.definedAndObserved",
                _ => "hierarchy.expansionMode.observed",
            });
            return HideEmptyValues ? mode + _localization.T("hierarchy.expansionBadge.hideEmptySuffix") : mode;
        }
    }

    partial void OnAliasChanged(string? value) => OnPropertyChanged(nameof(DisplayName));

    // ---- ECO-099: 配置モデル統一+行操作「⋯」メニュー(mock v3 行テンプレートの表示要素) ----

    /// <summary>行の選択強調(配置実行後の配置ノード選択=VC-TAG-12。エディタが単一選択を維持)。</summary>
    [ObservableProperty]
    private bool _isSelected;

    public bool HasChildren => Children.Count > 0;

    /// <summary>子数バッジ(mock: 親行の右端ピル)。</summary>
    public int ChildCount => Children.Count;

    /// <summary>子ブロック(インデント+左ガイド線)の表示= 子あり かつ 展開中。</summary>
    public bool ShowChildren => HasChildren && IsExpanded;

    /// <summary>ルート階層の行か(挿入ポイント寸法・行ハンドル有無の切替=mock 2 階層構造の一般化)。</summary>
    public bool IsRootLevel => Parent is null;

    /// <summary>ルート階層の葉行はカレット幅ぶんのスペーサーを置く(mock isLeaf)。</summary>
    public bool ShowLeafSpacer => IsRootLevel && !HasChildren;

    /// <summary>数値タグの定義域メタ(例 "1–5 ★"・mock node.meta)。エディタが生成時に解決して与える。</summary>
    public string? NumericMeta { get; set; }

    public bool HasNumericMeta => NumericMeta is not null;

    /// <summary>色ドットの淡色リング(mock dotStyle boxShadow 16% α)。#RRGGBB → #29RRGGBB。</summary>
    public string? RingColor => Tag?.Color is ['#', .. { Length: 6 }] c ? "#29" + c[1..] : null;

    /// <summary>行ホバー家アイコンの title(VC-TAG-14④: 現ホーム行=解除/他行=設定〔移動〕)。</summary>
    public string HomeButtonTitle => _localization is null
        ? string.Empty
        : _localization.T(IsHome ? "hierarchy.homeUnsetTitle" : "hierarchy.homeSetTitle");

    // 「⋯」メニュー項目文言(VC-TAG-13②)。Flyout(Popup)内は $parent[UserControl] 到達が保証されない
    // ため、DataContext(=本 VM)経由で Loc 解決済み文言を供給する(ChipVM.UndefLabel と同流儀)。
    public string MenuSetHomeText => T("hierarchy.menu.setHome");

    public string MenuRenameText => T("hierarchy.menu.rename");

    public string MenuSetConditionText => T("hierarchy.menu.setCondition");

    public string MenuRemoveText => T("hierarchy.menu.removePlacement");

    private string T(string key) => _localization?.T(key) ?? string.Empty;

    partial void OnIsHomeChanged(bool value) => OnPropertyChanged(nameof(HomeButtonTitle));

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(ShowChildren));

    /// <summary>ロケール切替時の VM 算出文言の再評価(DF-3。エディタの CultureChanged から呼ぶ)。</summary>
    internal void RaiseLocalizedTexts()
    {
        OnPropertyChanged(nameof(HomeButtonTitle));
        OnPropertyChanged(nameof(TypeText));
        OnPropertyChanged(nameof(ExpansionBadgeText));
        OnPropertyChanged(nameof(ConditionSummary));
        OnPropertyChanged(nameof(MenuSetHomeText));
        OnPropertyChanged(nameof(MenuRenameText));
        OnPropertyChanged(nameof(MenuSetConditionText));
        OnPropertyChanged(nameof(MenuRemoveText));
    }
}

/// <summary>
/// タグタブ中央「階層構造」エディタ(M-UI-013 v1.2、E-UI-NODEGRAPH-025、G-6)。
/// 編集はメモリ内で行い「保存」で一括コミット(REQ-032 の modified_at 更新は保存時に 1 回)、
/// 「キャンセル」は確認後に破棄。未保存変更がある間のみ保存/キャンセルを活性化(ダーティ追跡)。
/// ノード操作: 追加(パレットから D&D またはボタン)・展開/折畳・ホームタグ設定/解除・
/// 別名インライン編集・条件設定(textual/numeric)・削除。ロジックは unit 検査可能。
/// </summary>
public sealed partial class HierarchyEditorViewModel : ObservableObject
{
    private readonly ViewService _views;
    private readonly LocalizationService _localization;
    private readonly IWindowService _windows;
    private readonly Core.Repositories.ITagRepository? _tags;
    private IReadOnlyDictionary<string, Tag> _tagById = new Dictionary<string, Tag>(StringComparer.Ordinal);
    private View? _view;

    public HierarchyEditorViewModel(
        ViewService views, LocalizationService localization, IWindowService windows,
        Core.Repositories.ITagRepository? tags = null)
    {
        _views = views;
        _localization = localization;
        _windows = windows;
        _tags = tags; // ECO-086: 定義値の生成可否判定(裁定 e 警告)。null なら判定不能=警告なしで開く
        Loc = new LocalizationProxy(localization);
        localization.CultureChanged += (_, _) =>
        {
            // DF-3: Loc 差し替えで全文言バインディングを再評価させる(K-AVALONIA の罠対策)
            Loc = new LocalizationProxy(localization);
            OnPropertyChanged(nameof(Loc));
            // ECO-099: VM 算出文言(配置中チップ・家アイコン title 等)も再評価(ECO-079 の第 2 層)
            OnPropertyChanged(nameof(PlacingBannerText));
            OnPropertyChanged(nameof(NodeCountText));
            foreach (var node in Flatten())
            {
                node.RaiseLocalizedTexts();
            }
        };
    }

    public LocalizationProxy Loc { get; private set; }

    public ObservableCollection<EditNodeViewModel> Roots { get; } = [];

    [ObservableProperty]
    private EditNodeViewModel? _selectedNode;

    /// <summary>未保存変更あり(保存/キャンセルの活性条件、G-6)。</summary>
    [ObservableProperty]
    private bool _isDirty;

    // ---- ECO-099: 配置モデル統一(クリック配置)。CAD= tag_tab.md「配置モデル統一」+VC-TAG-12 ----

    /// <summary>配置モード中のタグ(VC-TAG-12)。null=非配置。配置モード自体は編集ではない(ダーティ不変)。</summary>
    [ObservableProperty]
    private Tag? _placingTag;

    public bool IsPlacing => PlacingTag is not null;

    /// <summary>ヘッダ帯の配置中チップ文言(VC-TAG-12②)。</summary>
    public string PlacingBannerText => PlacingTag is null
        ? string.Empty
        : _localization.T("hierarchy.placingBanner", new Dictionary<string, string>
        {
            ["tagName"] = PlacingTag.Name,
        });

    /// <summary>数値タグの定義域メタ("1–5 ★" 級)の解決子。ホスト(TagsTabViewModel)が配線する。</summary>
    public Func<string, string?>? NumericMetaResolver { get; set; }

    partial void OnPlacingTagChanged(Tag? value)
    {
        OnPropertyChanged(nameof(IsPlacing));
        OnPropertyChanged(nameof(PlacingBannerText));
    }

    partial void OnSelectedNodeChanged(EditNodeViewModel? oldValue, EditNodeViewModel? newValue)
    {
        // 行テンプレートの選択強調は per-node フラグで描く(単一選択の同期)
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
        }

        if (newValue is not null)
        {
            newValue.IsSelected = true;
        }
    }

    /// <summary>
    /// 配置モードのトグル(パレットカードのクリック)。同タグ再クリック=解除、別タグ=切替(mock select)。
    /// ビュー未選択時は挿入先が存在しないため入らない(ECO-099 実装判断)。
    /// </summary>
    public void TogglePlacing(Tag tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        if (!HasView)
        {
            return;
        }

        PlacingTag = string.Equals(PlacingTag?.Id, tag.Id, StringComparison.Ordinal) ? null : tag;
    }

    /// <summary>解除(Esc / ヘッダ帯の解除ボタン)。ツリーは不変。</summary>
    [RelayCommand]
    private void CancelPlacing() => PlacingTag = null;

    /// <summary>行間の挿入ポイント: その行の前へ兄弟として挿入(title「ここに挿入」)。</summary>
    [RelayCommand]
    private void InsertBefore(EditNodeViewModel target)
    {
        if (PlacingTag is not { } tag)
        {
            return;
        }

        var owner = target.Parent?.Children ?? Roots;
        var index = owner.IndexOf(target);
        if (index < 0)
        {
            return;
        }

        CommitPlacement(tag, target.Parent, index);
    }

    /// <summary>子リスト末尾の挿入ポイント(title「末尾に挿入」)。</summary>
    [RelayCommand]
    private void InsertChildEnd(EditNodeViewModel parent)
    {
        if (PlacingTag is { } tag)
        {
            CommitPlacement(tag, parent, parent.Children.Count);
        }
    }

    /// <summary>ルート末尾の挿入ポイント(title「末尾に挿入」)。</summary>
    [RelayCommand]
    private void InsertRootEnd()
    {
        if (PlacingTag is { } tag)
        {
            CommitPlacement(tag, null, Roots.Count);
        }
    }

    /// <summary>行末「＋ 子にする」: その行の子として末尾挿入(親は自動展開)。</summary>
    [RelayCommand]
    private void PlaceAsChild(EditNodeViewModel parent)
    {
        if (PlacingTag is { } tag)
        {
            CommitPlacement(tag, parent, parent.Children.Count);
        }
    }

    /// <summary>配置実行: 挿入 → 配置モード解除+配置ノードを選択(CAD「配置モデル統一」)。</summary>
    private void CommitPlacement(Tag tag, EditNodeViewModel? parent, int index)
    {
        InsertNodeCore(tag, parent, index);
        PlacingTag = null;
    }

    /// <summary>
    /// 「⋯」メニューの「ホームに設定」(VC-TAG-14⑥)。mock setHomeClose と同じく設定のみ
    /// (現ホーム行でも解除しない)。解除トグルは行ホバー家アイコン(ToggleHome)の役割。
    /// </summary>
    [RelayCommand]
    private void SetHomeFromMenu(EditNodeViewModel node)
    {
        foreach (var other in Flatten())
        {
            other.IsHome = false;
        }

        node.IsHome = true;
        SetDirty(true);
    }

    /// <summary>ビュー選択済み(未選択は「ビューを選択してください」、仕様 §2.6 空状態)。</summary>
    [ObservableProperty]
    private bool _hasView;

    [ObservableProperty]
    private string? _statusMessage;

    public string ViewName => _view?.Name ?? string.Empty;

    /// <summary>階層ノード 0 件 → 「まだタグが追加されていません」(仕様 §2.6 空状態)。</summary>
    public bool IsTreeEmpty => HasView && Roots.Count == 0;

    /// <summary>階層の総ノード数(コンテナヘッダの「N タグ」、ECO-009 ③)。</summary>
    public int NodeCount => Flatten().Count();

    /// <summary>「N タグ」表示(i18n)。</summary>
    public string NodeCountText => _localization.T("hierarchy.tagCount", new Dictionary<string, string>
    {
        ["count"] = NodeCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
    });

    public View? View => _view;

    /// <summary>バッチ保存が完了した(modified_at が更新されたためビュー一覧の再読込が必要)。</summary>
    public event EventHandler? Saved;

    /// <summary>編集中ツリー(未保存の配置を含む)に指定タグがあるか(REQ-083/ECO-046 U-a: 削除ガード判定)。</summary>
    public bool ContainsTag(string tagId)
        => Flatten().Any(n => string.Equals(n.TagId, tagId, StringComparison.Ordinal));

    /// <summary>編集対象ビューの読込(view=null は未選択)。ダーティはリセットされる。</summary>
    public async Task LoadAsync(View? view, IReadOnlyDictionary<string, Tag> tagById)
    {
        ArgumentNullException.ThrowIfNull(tagById);
        _view = view;
        _tagById = tagById;
        StatusMessage = null;
        PlacingTag = null; // ECO-099: ビュー切替・再読込で配置モードは持ち越さない
        Roots.Clear();
        SelectedNode = null;

        if (view is not null)
        {
            var nodes = await _views.GetHierarchyAsync(view.Id);
            var vmById = new Dictionary<string, EditNodeViewModel>(StringComparer.Ordinal);
            foreach (var node in nodes)
            {
                _tagById.TryGetValue(node.TagId, out var tag);
                var vm = new EditNodeViewModel(node.Id, node.TagId, tag, _localization)
                {
                    Alias = node.Alias,
                    IsHome = string.Equals(view.HomeTagId, node.Id, StringComparison.Ordinal),
                    NumericMeta = tag?.Type == TagType.Numeric ? NumericMetaResolver?.Invoke(tag.Id) : null,
                };
                vm.SetCondition(node.ConditionType, node.ConditionValue);
                vm.SetExpansion(node.ExpansionMode, node.HideEmptyValues); // REQ-096
                vmById[node.Id] = vm;
            }

            foreach (var node in nodes.OrderBy(n => n.Position).ThenBy(n => n.Id, StringComparer.Ordinal))
            {
                var vm = vmById[node.Id];
                if (node.ParentId is not null && vmById.TryGetValue(node.ParentId, out var parent))
                {
                    vm.Parent = parent;
                    parent.Children.Add(vm);
                }
                else
                {
                    Roots.Add(vm);
                }
            }
        }

        HasView = view is not null;
        SetDirty(false);
        OnPropertyChanged(nameof(ViewName));
        OnPropertyChanged(nameof(IsTreeEmpty));
        OnPropertyChanged(nameof(NodeCount));
        OnPropertyChanged(nameof(NodeCountText));
    }

    /// <summary>ノード追加(タグパレットから D&D)。同一親の末尾に追加。</summary>
    public EditNodeViewModel? AddNode(Tag tag, EditNodeViewModel? parent)
    {
        ArgumentNullException.ThrowIfNull(tag);
        if (_view is null)
        {
            return null;
        }

        return InsertNodeCore(tag, parent, (parent?.Children ?? Roots).Count);
    }

    /// <summary>挿入コア(ECO-099): 指定親の指定位置へ挿入し、親を展開・選択・ダーティ化する。</summary>
    private EditNodeViewModel InsertNodeCore(Tag tag, EditNodeViewModel? parent, int index)
    {
        var node = new EditNodeViewModel(IdGenerator.NewId(), tag.Id, tag, _localization)
        {
            Parent = parent,
            NumericMeta = tag.Type == TagType.Numeric ? NumericMetaResolver?.Invoke(tag.Id) : null,
        };
        var owner = parent?.Children ?? Roots;
        owner.Insert(Math.Clamp(index, 0, owner.Count), node);
        if (parent is not null)
        {
            parent.IsExpanded = true;
        }

        SelectedNode = node;
        SetDirty(true);
        OnPropertyChanged(nameof(IsTreeEmpty));
        OnPropertyChanged(nameof(NodeCount));
        OnPropertyChanged(nameof(NodeCountText));
        return node;
    }

    /// <summary>ノード削除(配下の枝ごと、メモリ内)。</summary>
    [RelayCommand]
    private void DeleteNode(EditNodeViewModel node)
    {
        var owner = node.Parent?.Children ?? Roots;
        if (!owner.Remove(node))
        {
            return;
        }

        if (SelectedNode is not null && IsInSubtree(node, SelectedNode))
        {
            SelectedNode = null;
        }

        SetDirty(true);
        OnPropertyChanged(nameof(IsTreeEmpty));
        OnPropertyChanged(nameof(NodeCount));
        OnPropertyChanged(nameof(NodeCountText));
    }

    /// <summary>別名のインライン編集を開始する。</summary>
    [RelayCommand]
    private void BeginAliasEdit(EditNodeViewModel node)
    {
        node.AliasEditText = node.Alias ?? string.Empty;
        node.IsEditingAlias = true;
    }

    /// <summary>別名の確定(空白のみ → null=タグ名表示、REQ-034)。</summary>
    [RelayCommand]
    private void CommitAlias(EditNodeViewModel node)
    {
        var alias = string.IsNullOrWhiteSpace(node.AliasEditText) ? null : node.AliasEditText;
        node.IsEditingAlias = false;
        if (!string.Equals(node.Alias, alias, StringComparison.Ordinal))
        {
            node.Alias = alias;
            SetDirty(true);
        }
    }

    [RelayCommand]
    private void CancelAliasEdit(EditNodeViewModel node) => node.IsEditingAlias = false;

    /// <summary>ホームタグ設定/解除(単一。設定中ノードは強調表示、REQ-037 / G-6)。</summary>
    [RelayCommand]
    private void ToggleHome(EditNodeViewModel node)
    {
        var wasHome = node.IsHome;
        foreach (var other in Flatten())
        {
            other.IsHome = false;
        }

        node.IsHome = !wasHome;
        SetDirty(true);
    }

    /// <summary>配置タグの設定ダイアログ(展開モード+条件・textual/numeric のみ、仕様 §2.4/§2.6・ECO-086)。</summary>
    [RelayCommand]
    private async Task EditConditionAsync(EditNodeViewModel node)
    {
        if (node.Tag is null || !node.CanHaveCondition)
        {
            return;
        }

        var result = await _windows.ShowNodeSettingsDialogAsync(node.Tag, new NodeSettingsRequest(
            node.ConditionType, node.ConditionValue, node.ExpansionMode, node.HideEmptyValues,
            await DefinedValuesAvailableAsync(node.Tag)));
        if (result is not null)
        {
            SetCondition(node, result.ConditionType, result.ConditionValueJson);
            SetExpansion(node, result.ExpansionMode, result.HideEmptyValues);
        }
    }

    /// <summary>
    /// 定義値を生成できるか(裁定 e の事前警告用)。判定は Core の生成規則そのもの
    /// (TagDefinedValueIndex)へ委譲し、二重定義しない。リポジトリ未注入なら true(警告なし)。
    /// </summary>
    private async Task<bool> DefinedValuesAvailableAsync(Tag tag)
    {
        if (_tags is null)
        {
            return true;
        }

        if (tag.Type == TagType.Textual)
        {
            var settings = await _tags.GetTextualSettingsAsync(tag.Id)
                ?? new TextualTagSettings { TagId = tag.Id };
            return TagDefinedValueIndex.Build(
                new Dictionary<string, TextualTagSettings>(StringComparer.Ordinal) { [tag.Id] = settings },
                new Dictionary<string, NumericTagSettings>(StringComparer.Ordinal))
                .GetDefinedValues(tag.Id) is not null;
        }

        var numeric = await _tags.GetNumericSettingsAsync(tag.Id)
            ?? new NumericTagSettings { TagId = tag.Id };
        return TagDefinedValueIndex.Build(
            new Dictionary<string, TextualTagSettings>(StringComparer.Ordinal),
            new Dictionary<string, NumericTagSettings>(StringComparer.Ordinal) { [tag.Id] = numeric })
            .GetDefinedValues(tag.Id) is not null;
    }

    /// <summary>展開モードの直接設定(メモリ内、unit 検査用エントリポイント・REQ-096)。</summary>
    public void SetExpansion(EditNodeViewModel node, HierarchyExpansionMode mode, bool hideEmptyValues)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.ExpansionMode == mode && node.HideEmptyValues == hideEmptyValues)
        {
            return;
        }

        node.SetExpansion(mode, hideEmptyValues);
        SetDirty(true);
    }

    /// <summary>条件の直接設定(メモリ内、unit 検査用エントリポイント)。</summary>
    public void SetCondition(EditNodeViewModel node, HierarchyConditionType? type, string? valueJson)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.ConditionType == type && string.Equals(node.ConditionValue, valueJson, StringComparison.Ordinal))
        {
            return;
        }

        node.SetCondition(type, valueJson);
        SetDirty(true);
    }

    /// <summary>
    /// バッチ保存: メモリ内ツリーを単一トランザクションで一括コミットし、
    /// modified_at は保存時に 1 回だけ更新される(REQ-032、ViewService.SaveHierarchyAsync)。
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsDirty))]
    private async Task SaveAsync()
    {
        if (_view is null)
        {
            return;
        }

        var nodes = new List<HierarchyNode>();
        Flatten(Roots, null, nodes);
        var home = Flatten().FirstOrDefault(n => n.IsHome)?.Id;

        var result = await _views.SaveHierarchyAsync(_view.Id, nodes, home);
        if (!result.IsSuccess)
        {
            StatusMessage = ErrorMessages.Resolve(_localization, result.Error);
            return;
        }

        StatusMessage = _localization.T("success.saved");
        _view = await _views.GetAsync(_view.Id) ?? _view;
        SetDirty(false);
        Saved?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>キャンセル: 確認後にメモリ内編集を破棄して DB 状態へ戻す(仕様 §2.6)。</summary>
    [RelayCommand(CanExecute = nameof(IsDirty))]
    private async Task CancelAsync()
    {
        if (_view is null)
        {
            return;
        }

        if (!await _windows.ConfirmAsync(
            _localization.T("modals.confirmDiscard.title"), _localization.T("modals.confirmDiscard.message")))
        {
            return;
        }

        await LoadAsync(_view, _tagById);
    }

    /// <summary>ダーティなら破棄確認(ビュー切替・タブ操作時)。true=続行可。</summary>
    public async Task<bool> ConfirmDiscardIfDirtyAsync()
    {
        if (!IsDirty)
        {
            return true;
        }

        return await _windows.ConfirmAsync(
            _localization.T("modals.confirmDiscard.title"), _localization.T("modals.confirmDiscard.message"));
    }

    private void SetDirty(bool value)
    {
        IsDirty = value;
        SaveCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    private static bool IsInSubtree(EditNodeViewModel root, EditNodeViewModel candidate)
    {
        if (ReferenceEquals(root, candidate))
        {
            return true;
        }

        foreach (var child in root.Children)
        {
            if (IsInSubtree(child, candidate))
            {
                return true;
            }
        }

        return false;
    }

    private IEnumerable<EditNodeViewModel> Flatten()
    {
        var stack = new Stack<EditNodeViewModel>(Roots.Reverse());
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;
            foreach (var child in node.Children.Reverse())
            {
                stack.Push(child);
            }
        }
    }

    private void Flatten(IEnumerable<EditNodeViewModel> siblings, string? parentId, List<HierarchyNode> target)
    {
        var position = 0;
        foreach (var node in siblings)
        {
            target.Add(new HierarchyNode
            {
                Id = node.Id,
                ViewId = _view!.Id,
                TagId = node.TagId,
                ParentId = parentId,
                Position = position++,
                Alias = node.Alias,
                ConditionType = node.ConditionType,
                ConditionValue = node.ConditionValue,
                ExpansionMode = node.ExpansionMode,
                HideEmptyValues = node.HideEmptyValues,
            });
            Flatten(node.Children, node.Id, target);
        }
    }
}
