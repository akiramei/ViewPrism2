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

    /// <summary>条件チップ配色: 数値(等値/範囲)=amber。</summary>
    public bool IsConditionAmber => ConditionType is HierarchyConditionType.Equals or HierarchyConditionType.Range;

    /// <summary>条件チップ配色: パターン(regex)=mono 青。Values=緑(既定)。</summary>
    public bool IsConditionMono => ConditionType is HierarchyConditionType.Pattern;

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
        OnPropertyChanged(nameof(IsConditionAmber));
        OnPropertyChanged(nameof(IsConditionMono));
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
        };
    }

    public LocalizationProxy Loc { get; private set; }

    public ObservableCollection<EditNodeViewModel> Roots { get; } = [];

    [ObservableProperty]
    private EditNodeViewModel? _selectedNode;

    /// <summary>未保存変更あり(保存/キャンセルの活性条件、G-6)。</summary>
    [ObservableProperty]
    private bool _isDirty;

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

    /// <summary>ノード追加(タグパレットから D&D またはボタン)。同一親の末尾に追加。</summary>
    public EditNodeViewModel? AddNode(Tag tag, EditNodeViewModel? parent)
    {
        ArgumentNullException.ThrowIfNull(tag);
        if (_view is null)
        {
            return null;
        }

        var node = new EditNodeViewModel(IdGenerator.NewId(), tag.Id, tag, _localization) { Parent = parent };
        if (parent is null)
        {
            Roots.Add(node);
        }
        else
        {
            parent.Children.Add(node);
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
