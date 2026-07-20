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
    private Dictionary<string, string> _numericMetaByTagId = new(StringComparer.Ordinal);
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
        // ECO-099: 数値タグの定義域メタ("1–5 ★")を行へ供給(ReloadTagDictionaryAsync で構築)
        Editor.NumericMetaResolver = tagId =>
            _numericMetaByTagId.TryGetValue(tagId, out var meta) ? meta : null;
        // ECO-099: 配置モード(エディタ所有)をパレットの配置中カード強調へ同期(VC-TAG-12①)
        Editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(HierarchyEditorViewModel.PlacingTag))
            {
                Palette.PlacingTagId = Editor.PlacingTag?.Id;
            }
        };
        localization.CultureChanged += (_, _) =>
        {
            // DF-3: Loc 差し替えで全文言バインディングを再評価させる(K-AVALONIA の罠対策)
            Loc = new LocalizationProxy(localization);
            OnPropertyChanged(nameof(Loc));
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

        // ECO-103(VC-TAG-16⑥): dirty 中の別ビュー選択はブロック+attention(旧・破棄確認ダイアログを撤去)
        if (!Editor.GuardNavigation())
        {
            return;
        }

        SetSelectedRow(row);
        await Editor.LoadAsync(row.View, _tagById);
    }

    [RelayCommand]
    private async Task NewViewAsync()
    {
        // ECO-103/TAG-016(i): dirty 中のビュー操作は同ガード様式でブロック
        if (!Editor.GuardNavigation())
        {
            return;
        }

        if (await _windows.ShowViewEditDialogAsync(null))
        {
            await ReloadViewsAsync();
            DataChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    private async Task EditViewAsync(ViewRowViewModel row)
    {
        // ECO-103/TAG-016(i): dirty 中のビュー操作(リネーム等)はブロック
        if (!Editor.GuardNavigation())
        {
            return;
        }

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
        // ECO-103/TAG-016(i): dirty 中の削除は未保存編集の消失経路 — 確認より前にブロック
        if (!Editor.GuardNavigation())
        {
            return;
        }

        var message = _localization.T("common.confirmDelete", new Dictionary<string, string>
        {
            ["name"] = row.View.Name,
        });
        if (!await _windows.ConfirmAsync(_localization.T("view.deleteConfirmTitle"), message,
                _localization.T("common.ctaDelete"), destructive: true))
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

    /// <summary>
    /// パレットカードのクリック=配置モードのトグル(ECO-099・配置モデル統一)。
    /// 旧・選択ベース配置ボタン(GF-04/ECO-007 E3)はこの経路に置換され撤去(CAD mock v3 で superseded)。
    /// </summary>
    public void TogglePlacing(TagPaletteRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        Editor.TogglePlacing(row.Tag);
    }

    // ECO-100: 旧 D&D 経路 AddTagById(行上=子/空白=ルートの暗黙ドロップ)は撤去 —
    // ドラッグ配置は挿入表示(Editor.BeginDragPlacing→Insert* コマンド)へ一本化(mock 契約)。

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

        // ECO-099: 配置中タグが削除されたら配置モードを解除する(参照切れ配置の防止)
        if (Editor.PlacingTag is { } placing && !_tagById.ContainsKey(placing.Id))
        {
            Editor.CancelPlacingCommand.Execute(null);
        }

        // タグ削除は DB 側で階層ノードを CASCADE 削除する(REQ-028)ため、エディタを最新状態へ
        if (!Editor.IsDirty && SelectedViewRow is { } row)
        {
            var view = await _views.GetAsync(row.View.Id);
            await Editor.LoadAsync(view, _tagById);
        }
        else
        {
            // ECO-102(案A): dirty 中は構造(未保存ツリー)を守りつつ、表示(Tag 参照・数値メタ・
            // 配置中タグ)だけを最新定義へ再束縛する — 構造の保護と表示の鮮度の分離
            Editor.RebindTags(_tagById);
        }

        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task ReloadTagDictionaryAsync()
    {
        var all = await _tags.GetAllAsync();
        _tagById = all.ToDictionary(t => t.Id, StringComparer.Ordinal);

        // ECO-099: 数値タグの定義域メタ(mock node.meta="1–5 ★")。行生成時に NumericMetaResolver が引く
        var meta = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var tag in all.Where(t => t.Type == TagType.Numeric))
        {
            var settings = await _tags.GetNumericSettingsAsync(tag.Id);
            if (TagPaletteRowViewModel.BuildRangeText(settings) is { } text)
            {
                meta[tag.Id] = text;
            }
        }

        _numericMetaByTagId = meta;
    }
}
