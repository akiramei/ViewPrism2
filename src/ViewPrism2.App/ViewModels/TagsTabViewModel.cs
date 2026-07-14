using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ViewPrism2.App.Services;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;
using ViewPrism2.Core.Services;

namespace ViewPrism2.App.ViewModels;

/// <summary>
/// タグタブ左「ビュー管理」の 1 行(名前+タグ数バッジ+説明 tooltip+編集/削除アイコン、
/// 仕様 §2.6・ECO-007/E1)。
/// </summary>
public sealed partial class ViewRowViewModel : ObservableObject
{
    public ViewRowViewModel(View view, int tagCount = 0)
    {
        View = view;
        TagCount = tagCount;
    }

    public View View { get; }

    public string Name => View.Name;

    /// <summary>
    /// お気に入りデータ(View.IsFavorite)。ECO-007/E1: タグタブ ビュー行には★を出さない
    /// (DC-VIEWLIST-001/DE-2 撤回)。データは保持する(作成/編集ダイアログ・他画面で利用)。
    /// </summary>
    public bool IsFavorite => View.IsFavorite;

    /// <summary>
    /// 配置タグ数=このビューの階層ノード数(ECO-007/E1・DC-VIEWLIST-001/DE-4)。行末バッジで表示する。
    /// </summary>
    public int TagCount { get; }

    /// <summary>
    /// 説明(ECO-007/E1・DC-VIEWLIST-001/DE-3)。行内 truncate ではなく tooltip で表示する。
    /// null/空白のみなら tooltip を出さない(null を返す)。
    /// </summary>
    public string? Description => HasDescription ? View.Description : null;

    /// <summary>説明が非空か(tooltip の表示制御。空文字・空白のみは非表示)。</summary>
    public bool HasDescription => !string.IsNullOrWhiteSpace(View.Description);

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// タグタブ(3 ペイン)の統括 VM(M-UI-013 v1.2、G-6)。
/// 左=ビュー管理(新規・一覧・編集/削除・選択)/ 中央=階層構造エディタ(バッチ保存)/ 右=タグパレット。
/// 選択中のビューが中央の階層編集対象になる。ダーティ中のビュー切替は破棄確認を挟む。
/// </summary>
public sealed partial class TagsTabViewModel : ObservableObject
{
    private readonly ViewService _views;
    private readonly ITagRepository _tags;
    private readonly LocalizationService _localization;
    private readonly IWindowService _windows;
    private Dictionary<string, Tag> _tagById = new(StringComparer.Ordinal);
    private bool _loaded;

    public TagsTabViewModel(
        ViewService views,
        TagService tagService,
        ITagRepository tags,
        LocalizationService localization,
        IWindowService windows)
    {
        _views = views;
        _tags = tags;
        _localization = localization;
        _windows = windows;
        Loc = new LocalizationProxy(localization);
        Editor = new HierarchyEditorViewModel(views, localization, windows, tags);
        Palette = new TagPaletteViewModel(tagService, localization, windows);

        Editor.Saved += (_, _) => DataChanged?.Invoke(this, EventArgs.Empty);
        // ECO-046(U-a): DB ガード(ECO-045)が関知できない未保存編集中の配置を削除から保護
        Palette.IsTagInUnsavedEdit = tagId => Editor.IsDirty && Editor.ContainsTag(tagId);
        Palette.TagsChanged += async (_, _) => await OnTagsChangedAsync();
        Palette.PropertyChanged += (_, e) =>
        {
            // GF-04: パレット選択の変化で追加ボタンの文言・活性を更新する
            if (e.PropertyName == nameof(TagPaletteViewModel.SelectedTag))
            {
                RaiseAddButtonChanged();
            }
        };
        localization.CultureChanged += (_, _) =>
        {
            // DF-3: Loc 差し替えで全文言バインディングを再評価させる(K-AVALONIA の罠対策)
            Loc = new LocalizationProxy(localization);
            OnPropertyChanged(nameof(Loc));
            RaiseAddButtonChanged();
        };
    }

    public LocalizationProxy Loc { get; private set; }

    public HierarchyEditorViewModel Editor { get; }

    public TagPaletteViewModel Palette { get; }

    public ObservableCollection<ViewRowViewModel> Views { get; } = [];

    [ObservableProperty]
    private ViewRowViewModel? _selectedViewRow;

    public bool IsViewsEmpty => Views.Count == 0;

    /// <summary>ビュー・タグ・階層の永続変更があった(画像タブ側の再読込用)。</summary>
    public event EventHandler? DataChanged;

    /// <summary>タブ初回表示時の遅延読込。</summary>
    public async Task EnsureLoadedAsync()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await Palette.LoadAsync();
        await ReloadTagDictionaryAsync();
        await ReloadViewsAsync();
    }

    public async Task ReloadViewsAsync()
    {
        var selectedId = SelectedViewRow?.View.Id;
        var all = await _views.GetAllAsync();
        Views.Clear();
        foreach (var view in all
            .OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(v => v.Id, StringComparer.Ordinal))
        {
            // ECO-007/E1: 配置タグ数=階層ノード数をバッジ表示するために件数を取得する
            var tagCount = await _views.GetHierarchyCountAsync(view.Id);
            Views.Add(new ViewRowViewModel(view, tagCount));
        }

        OnPropertyChanged(nameof(IsViewsEmpty));

        if (selectedId is not null)
        {
            var row = Views.FirstOrDefault(r => string.Equals(r.View.Id, selectedId, StringComparison.Ordinal));
            if (row is not null)
            {
                SetSelectedRow(row);
                return;
            }

            // 選択中ビューが消えた → エディタを未選択へ
            SetSelectedRow(null);
            await Editor.LoadAsync(null, _tagById);
        }
    }

    /// <summary>ビュー選択(クリック)。ダーティなら破棄確認(仕様 §2.6 のバッチ編集規律)。</summary>
    [RelayCommand]
    private async Task SelectViewAsync(ViewRowViewModel row)
    {
        if (ReferenceEquals(row, SelectedViewRow))
        {
            return;
        }

        if (!await Editor.ConfirmDiscardIfDirtyAsync())
        {
            return;
        }

        SetSelectedRow(row);
        await Editor.LoadAsync(row.View, _tagById);
    }

    [RelayCommand]
    private async Task NewViewAsync()
    {
        if (await _windows.ShowViewEditDialogAsync(null))
        {
            await ReloadViewsAsync();
            DataChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    private async Task EditViewAsync(ViewRowViewModel row)
    {
        if (!await _windows.ShowViewEditDialogAsync(row.View))
        {
            return;
        }

        await ReloadViewsAsync();
        DataChanged?.Invoke(this, EventArgs.Empty);

        // 名前変更を中央ペインに反映(ダーティ編集中は保護のため触らない)
        if (!Editor.IsDirty &&
            string.Equals(SelectedViewRow?.View.Id, row.View.Id, StringComparison.Ordinal) &&
            SelectedViewRow is { } current)
        {
            await Editor.LoadAsync(current.View, _tagById);
        }
    }

    [RelayCommand]
    private async Task DeleteViewAsync(ViewRowViewModel row)
    {
        var message = _localization.T("common.confirmDelete", new Dictionary<string, string>
        {
            ["name"] = row.View.Name,
        });
        if (!await _windows.ConfirmAsync(_localization.T("view.deleteConfirmTitle"), message))
        {
            return;
        }

        await _views.DeleteAsync(row.View.Id);
        if (string.Equals(SelectedViewRow?.View.Id, row.View.Id, StringComparison.Ordinal))
        {
            SetSelectedRow(null);
            await Editor.LoadAsync(null, _tagById);
        }

        await ReloadViewsAsync();
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>パレットでタグが選択されているか(GF-04: 追加ボタンの活性条件)。</summary>
    public bool CanAddNode => Palette.SelectedTag is not null;

    /// <summary>
    /// ルート追加ボタンの文言(ECO-007/E3・GF-04 撤回)。汎用ラベル『タグを配置』(ルート)を表示し、
    /// 対象タグ名は出さない。未選択時は非活性(CanAddNode=false)+選択を促す文言。i18n キー経由。
    /// </summary>
    public string AddRootButtonText => Palette.SelectedTag is not null
        ? _localization.T("hierarchy.placeTagRoot")
        : _localization.T("hierarchy.selectTagToAdd");

    /// <summary>
    /// 子追加ボタンの文言(ECO-007/E3・GF-04 撤回)。汎用ラベル『タグを配置』(子)を表示し、
    /// 対象タグ名は出さない。未選択時は選択を促す文言。
    /// </summary>
    public string AddChildButtonText => Palette.SelectedTag is not null
        ? _localization.T("hierarchy.placeTagChild")
        : _localization.T("hierarchy.selectTagToAdd");

    /// <summary>パレット選択タグをルートへ追加(ボタン経路、仕様 §2.6)。未選択時は非活性(GF-04)。</summary>
    [RelayCommand(CanExecute = nameof(CanAddNode))]
    private void AddRootNode()
    {
        if (Palette.SelectedTag is { } row)
        {
            Editor.AddNode(row.Tag, null);
        }
    }

    /// <summary>パレット選択タグを選択ノードの子として追加(ボタン経路)。未選択時は非活性(GF-04)。</summary>
    [RelayCommand(CanExecute = nameof(CanAddNode))]
    private void AddChildNode()
    {
        if (Palette.SelectedTag is { } row)
        {
            Editor.AddNode(row.Tag, Editor.SelectedNode);
        }
    }

    /// <summary>GF-04: 追加ボタンの文言・活性を再評価する。</summary>
    private void RaiseAddButtonChanged()
    {
        OnPropertyChanged(nameof(CanAddNode));
        OnPropertyChanged(nameof(AddRootButtonText));
        OnPropertyChanged(nameof(AddChildButtonText));
        AddRootNodeCommand.NotifyCanExecuteChanged();
        AddChildNodeCommand.NotifyCanExecuteChanged();
    }

    /// <summary>D&D 経路: タグ id からノード追加(View 層の Drop ハンドラから呼ぶ)。</summary>
    public void AddTagById(string tagId, EditNodeViewModel? target)
    {
        if (_tagById.TryGetValue(tagId, out var tag))
        {
            Editor.AddNode(tag, target);
        }
    }

    private void SetSelectedRow(ViewRowViewModel? row)
    {
        foreach (var other in Views)
        {
            other.IsSelected = ReferenceEquals(other, row);
        }

        SelectedViewRow = row;
    }

    private async Task OnTagsChangedAsync()
    {
        await ReloadTagDictionaryAsync();

        // タグ削除は DB 側で階層ノードを CASCADE 削除する(REQ-028)ため、エディタを最新状態へ
        if (!Editor.IsDirty && SelectedViewRow is { } row)
        {
            var view = await _views.GetAsync(row.View.Id);
            await Editor.LoadAsync(view, _tagById);
        }

        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task ReloadTagDictionaryAsync()
    {
        var all = await _tags.GetAllAsync();
        _tagById = all.ToDictionary(t => t.Id, StringComparer.Ordinal);
    }
}
