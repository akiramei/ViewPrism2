using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;
using ViewPrism2.Core.Services;

namespace ViewPrism2.App.ViewModels;

/// <summary>ビュー階層エディタのノード表示(alias が NULL なら tag.name、REQ-034)。</summary>
public sealed class HierarchyNodeViewModel
{
    public HierarchyNodeViewModel(HierarchyNode node, string displayName)
    {
        Node = node;
        DisplayName = displayName;
    }

    public HierarchyNode Node { get; }

    public string DisplayName { get; }

    public ObservableCollection<HierarchyNodeViewModel> Children { get; } = [];
}

/// <summary>条件一覧の表示行(REQ-031 の入力管理)。</summary>
public sealed record ConditionRowViewModel(ViewCondition Condition, string DisplayText);

/// <summary>演算子の選択肢(表記は仕様 §2.3 の小文字トークン)。</summary>
public sealed record OperatorOption(ConditionOperator Value, string Label);

/// <summary>
/// ビュー編集ダイアログ(E-UI-NODEGRAPH-025 の階層・条件編集)。
/// 基本情報(name 必須・お気に入り)→ 保存後に階層ノード・条件を編集する。
/// modified_at 規則・循環拒否は ViewService(core)に委譲する(REQ-032/034)。
/// </summary>
public sealed partial class ViewEditorViewModel : ObservableObject
{
    private readonly ViewService _views;
    private readonly ITagRepository _tags;
    private readonly LocalizationService _localization;
    private View? _view;
    private Dictionary<string, Tag> _tagById = new(StringComparer.Ordinal);

    public ViewEditorViewModel(View? existing, ViewService views, ITagRepository tags, LocalizationService localization)
    {
        _views = views;
        _tags = tags;
        _localization = localization;
        _view = existing;
        Loc = new LocalizationProxy(localization);

        OperatorOptions =
        [
            new(ConditionOperator.Exists, "exists"),
            new(ConditionOperator.Equals, "equals"),
            new(ConditionOperator.Between, "between"),
            new(ConditionOperator.Regexp, "regexp"),
            new(ConditionOperator.In, "in"),
        ];
        _selectedOperator = OperatorOptions[0];

        if (existing is not null)
        {
            _name = existing.Name;
            _isFavorite = existing.IsFavorite;
        }
    }

    public LocalizationProxy Loc { get; }

    public IReadOnlyList<OperatorOption> OperatorOptions { get; }

    public ObservableCollection<Tag> AllTags { get; } = [];

    public ObservableCollection<HierarchyNodeViewModel> Roots { get; } = [];

    public ObservableCollection<ConditionRowViewModel> Conditions { get; } = [];

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    private HierarchyNodeViewModel? _selectedNode;

    [ObservableProperty]
    private Tag? _selectedTag;

    [ObservableProperty]
    private string _aliasText = string.Empty;

    [ObservableProperty]
    private Tag? _selectedConditionTag;

    [ObservableProperty]
    private OperatorOption _selectedOperator;

    [ObservableProperty]
    private string _conditionValue = string.Empty;

    [ObservableProperty]
    private string _conditionValue2 = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>保存済み(階層・条件の編集が可能)。</summary>
    public bool IsPersisted => _view is not null;

    /// <summary>何らかの変更を保存した(呼び出し元の一覧再読込判定)。</summary>
    public bool Changed { get; private set; }

    public async Task LoadAsync()
    {
        AllTags.Clear();
        var tags = await _tags.GetAllAsync();
        foreach (var tag in tags.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
        {
            AllTags.Add(tag);
        }

        _tagById = tags.ToDictionary(t => t.Id, StringComparer.Ordinal);
        await ReloadHierarchyAsync();
        await ReloadConditionsAsync();
    }

    [RelayCommand]
    private async Task SaveBasicAsync()
    {
        ErrorMessage = null;
        if (_view is null)
        {
            var created = await _views.CreateAsync(Name, IsFavorite);
            if (!created.IsSuccess)
            {
                ErrorMessage = ErrorMessages.Resolve(_localization, created.Error);
                return;
            }

            _view = created.Value;
            OnPropertyChanged(nameof(IsPersisted));
        }
        else
        {
            var updated = await _views.UpdateAsync(_view with { Name = Name, IsFavorite = IsFavorite });
            if (!updated.IsSuccess)
            {
                ErrorMessage = ErrorMessages.Resolve(_localization, updated.Error);
                return;
            }

            _view = updated.Value;
        }

        Changed = true;
    }

    [RelayCommand]
    private Task AddRootNodeAsync() => AddNodeAsync(parent: null);

    [RelayCommand]
    private Task AddChildNodeAsync() => AddNodeAsync(parent: SelectedNode);

    [RelayCommand]
    private async Task DeleteNodeAsync()
    {
        if (_view is null || SelectedNode is not { } node)
        {
            return;
        }

        var result = await _views.DeleteNodeAsync(node.Node.Id);
        if (!result.IsSuccess)
        {
            ErrorMessage = ErrorMessages.Resolve(_localization, result.Error);
            return;
        }

        Changed = true;
        await ReloadHierarchyAsync();
    }

    [RelayCommand]
    private async Task AddConditionAsync()
    {
        if (_view is null || SelectedConditionTag is not { } tag)
        {
            return;
        }

        ErrorMessage = null;
        var op = SelectedOperator.Value;
        string? value = op == ConditionOperator.Exists ? null : NullIfEmpty(ConditionValue);
        string? value2 = op == ConditionOperator.Between ? NullIfEmpty(ConditionValue2) : null;

        var result = await _views.AddConditionAsync(_view.Id, tag.Id, op, value, value2);
        if (!result.IsSuccess)
        {
            ErrorMessage = ErrorMessages.Resolve(_localization, result.Error);
            return;
        }

        Changed = true;
        await ReloadConditionsAsync();
    }

    [RelayCommand]
    private async Task DeleteConditionAsync(ConditionRowViewModel row)
    {
        if (_view is null)
        {
            return;
        }

        var result = await _views.DeleteConditionAsync(row.Condition.Id);
        if (!result.IsSuccess)
        {
            ErrorMessage = ErrorMessages.Resolve(_localization, result.Error);
            return;
        }

        Changed = true;
        await ReloadConditionsAsync();
    }

    private async Task AddNodeAsync(HierarchyNodeViewModel? parent)
    {
        if (_view is null || SelectedTag is not { } tag)
        {
            return;
        }

        ErrorMessage = null;
        var siblings = parent?.Children.Count ?? Roots.Count;
        var alias = NullIfEmpty(AliasText);
        var result = await _views.AddNodeAsync(_view.Id, tag.Id, parent?.Node.Id, siblings, alias);
        if (!result.IsSuccess)
        {
            ErrorMessage = ErrorMessages.Resolve(_localization, result.Error);
            return;
        }

        Changed = true;
        AliasText = string.Empty;
        await ReloadHierarchyAsync();
    }

    private async Task ReloadHierarchyAsync()
    {
        Roots.Clear();
        if (_view is null)
        {
            return;
        }

        var nodes = await _views.GetHierarchyAsync(_view.Id);
        var vmById = new Dictionary<string, HierarchyNodeViewModel>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            var displayName = node.Alias ?? (_tagById.TryGetValue(node.TagId, out var tag) ? tag.Name : node.TagId);
            vmById[node.Id] = new HierarchyNodeViewModel(node, displayName);
        }

        foreach (var node in nodes.OrderBy(n => n.Position).ThenBy(n => n.Id, StringComparer.Ordinal))
        {
            var vm = vmById[node.Id];
            if (node.ParentId is not null && vmById.TryGetValue(node.ParentId, out var parent))
            {
                parent.Children.Add(vm);
            }
            else
            {
                Roots.Add(vm);
            }
        }
    }

    private async Task ReloadConditionsAsync()
    {
        Conditions.Clear();
        if (_view is null)
        {
            return;
        }

        foreach (var condition in await _views.GetConditionsAsync(_view.Id))
        {
            var tagName = condition.TagId is not null && _tagById.TryGetValue(condition.TagId, out var tag)
                ? tag.Name
                : "—";
            var op = OperatorOptions.First(o => o.Value == condition.Operator).Label;
            var text = condition.Operator switch
            {
                ConditionOperator.Exists => $"{tagName} : {op}",
                ConditionOperator.Between => $"{tagName} : {op} {condition.Value} 〜 {condition.Value2}",
                _ => $"{tagName} : {op} {condition.Value}",
            };
            Conditions.Add(new ConditionRowViewModel(condition, text));
        }
    }

    private static string? NullIfEmpty(string text) => string.IsNullOrWhiteSpace(text) ? null : text;
}
