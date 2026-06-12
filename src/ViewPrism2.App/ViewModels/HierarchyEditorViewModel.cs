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
    public EditNodeViewModel(string id, string tagId, Tag? tag)
    {
        Id = id;
        TagId = tagId;
        Tag = tag;
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

    public string DisplayName => Alias ?? Tag?.Name ?? TagId;

    /// <summary>条件設定は textual/numeric のみ(仕様 §2.6)。</summary>
    public bool CanHaveCondition => Tag?.Type is TagType.Textual or TagType.Numeric;

    public bool HasCondition => ConditionType is not null;

    public string ConditionSummary => ConditionType switch
    {
        HierarchyConditionType.Equals => $"equals {ConditionValue}",
        HierarchyConditionType.Range => $"range {ConditionValue}",
        HierarchyConditionType.Pattern => $"pattern {ConditionValue}",
        HierarchyConditionType.Values => $"values {ConditionValue}",
        _ => string.Empty,
    };

    public void SetCondition(HierarchyConditionType? type, string? valueJson)
    {
        ConditionType = type;
        ConditionValue = type is null ? null : valueJson;
        OnPropertyChanged(nameof(HasCondition));
        OnPropertyChanged(nameof(ConditionSummary));
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
    private IReadOnlyDictionary<string, Tag> _tagById = new Dictionary<string, Tag>(StringComparer.Ordinal);
    private View? _view;

    public HierarchyEditorViewModel(ViewService views, LocalizationService localization, IWindowService windows)
    {
        _views = views;
        _localization = localization;
        _windows = windows;
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

    public View? View => _view;

    /// <summary>バッチ保存が完了した(modified_at が更新されたためビュー一覧の再読込が必要)。</summary>
    public event EventHandler? Saved;

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
                var vm = new EditNodeViewModel(node.Id, node.TagId, tag)
                {
                    Alias = node.Alias,
                    IsHome = string.Equals(view.HomeTagId, node.Id, StringComparison.Ordinal),
                };
                vm.SetCondition(node.ConditionType, node.ConditionValue);
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
    }

    /// <summary>ノード追加(タグパレットから D&D またはボタン)。同一親の末尾に追加。</summary>
    public EditNodeViewModel? AddNode(Tag tag, EditNodeViewModel? parent)
    {
        ArgumentNullException.ThrowIfNull(tag);
        if (_view is null)
        {
            return null;
        }

        var node = new EditNodeViewModel(IdGenerator.NewId(), tag.Id, tag) { Parent = parent };
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

    /// <summary>条件設定ダイアログ(textual/numeric のみ、仕様 §2.6)。</summary>
    [RelayCommand]
    private async Task EditConditionAsync(EditNodeViewModel node)
    {
        if (node.Tag is null || !node.CanHaveCondition)
        {
            return;
        }

        var result = await _windows.ShowNodeConditionDialogAsync(node.Tag, node.ConditionType, node.ConditionValue);
        if (result is not null)
        {
            SetCondition(node, result.ConditionType, result.ConditionValueJson);
        }
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
            });
            Flatten(node.Children, node.Id, target);
        }
    }
}
