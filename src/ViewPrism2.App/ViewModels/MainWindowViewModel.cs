using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ViewPrism2.App.Services;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;
using ViewPrism2.Core.Services;
using ViewPrism2.Core.Services.Repair;
using ViewPrism2.Core.Services.Similarity;

namespace ViewPrism2.App.ViewModels;

/// <summary>
/// シェルのルート ViewModel(M-UI-013、E-UI-SHELL-021)。
/// 上部タブナビゲーション「タグ(0)」「画像(1)」「作業(2)」+右端「設定」。
/// 各タブ surface(TagsTab / ImageTab / WorkTab)を保持し、タブ切替・ローカライズ・設定確定のみを担う。
/// ECO-024: 原典(legacy)画像タブ Grid・harness・legacy VM(Browser/Detail/Tagging/FolderPane 等)を撤去し、
/// 画像タブを ImageTabView 一本化。コレクション選択スコープ(REQ-053)・表示モード永続化(CR-5/CR-6)・
/// 表示軸ナビ・タグ編集・類似/マージ/トラッシュ/修復/削除/作業は ImageTabViewModel が自己完結で担う。
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly LocalizationService _localization;
    private readonly AppSettings _settings;
    private readonly IWindowService _windows;

    private bool _imagesTabStale;

    public MainWindowViewModel(
        ISyncFolderRepository folders,
        IImageRepository images,
        ITagRepository tags,
        ViewService views,
        NodeGraphBuilder graphBuilder,
        PathConditionConverter pathConverter,
        ConditionEvaluator evaluator,
        SimilaritySearchService similar,
        MergeService merge,
        TrashService trash,
        ImageSorter sorter,
        LocalizationService localization,
        AppSettings settings,
        IWindowService windows,
        TagsTabViewModel tagsTab,
        WorkspaceService workspaces,
        ILogger<MainWindowViewModel>? logger = null)
    {
        _localization = localization;
        _settings = settings;
        _windows = windows;

        Loc = new LocalizationProxy(localization);
        TagsTab = tagsTab;

        // 画像タブ実 VM(モック準拠 surface)。注入済みリポジトリ/サービスを共有する。
        ImageTab = new ImageTabViewModel(folders, images, tags, sorter, views, graphBuilder, pathConverter, evaluator, similar, merge, trash, windows, settings, workspaces, localization);

        // 作業タブ surface(第3タブ・ECO-020)。
        WorkTab = new WorkTabViewModel(workspaces, folders, tags, similar, merge, trash, windows, sorter, settings);

        // タグ タブでの永続変更(タグ・ビュー・階層)は次回画像タブ表示時に反映する(stale フラグ)。
        TagsTab.DataChanged += (_, _) => _imagesTabStale = true;

        localization.CultureChanged += (_, _) =>
        {
            // DF-3(K-AVALONIA の罠): コンパイル済みバインディングはインデクサ('Item[]')の
            // PropertyChanged では再評価されない。Loc 自体(名前付きプロパティ)を差し替えて
            // 全文言バインディングを確実に再評価させる
            Loc = new LocalizationProxy(localization);
            OnPropertyChanged(nameof(Loc));
        };
    }

    public LocalizationProxy Loc { get; private set; }

    /// <summary>タグタブ(3 ペイン)。</summary>
    public TagsTabViewModel TagsTab { get; }

    /// <summary>画像タブ surface(ImageTabView)。</summary>
    public ImageTabViewModel ImageTab { get; }

    /// <summary>作業タブ surface(ECO-020・第3タブ)。</summary>
    public WorkTabViewModel WorkTab { get; }

    /// <summary>選択中タブ(0=タグ / 1=画像 / 2=作業)。初期は画像タブ。</summary>
    [ObservableProperty]
    private int _selectedTabIndex = 1;

    /// <summary>非モーダル通知(ステータスバー)。</summary>
    [ObservableProperty]
    private string? _statusMessage;

    public bool IsTagsTabSelected => SelectedTabIndex == 0;

    public bool IsImagesTabSelected => SelectedTabIndex == 1;

    /// <summary>作業タブ(第3タブ・ECO-020)を選択中か。</summary>
    public bool IsWorkTabSelected => SelectedTabIndex == 2;

    /// <summary>起動時初期化: 画像タブ実 VM の初期ロード(選択コレクション/表示モードは settings から復元)。</summary>
    public async Task InitializeAsync()
    {
        await ImageTab.InitializeAsync();
    }

    /// <summary>
    /// 終了時の永続化(REQ-052 v1.3): ロケール + 選択コレクション(CR-5)・表示モード(CR-6)を設定へ書き戻す。
    /// コレクション/表示モードは画像タブ surface(ImageTab)が所有する。
    /// </summary>
    public void CaptureSettings()
    {
        _settings.Locale = _localization.CurrentLocale;
        // 選択コレクション(CR-5)・表示モード(CR-6)は画像タブ surface(ImageTab)が所有する。
        ImageTab.CaptureSettings();
    }

    [RelayCommand]
    private void ShowTagsTab() => SelectedTabIndex = 0;

    [RelayCommand]
    private void ShowImagesTab() => SelectedTabIndex = 1;

    [RelayCommand]
    private void ShowWorkTab() => SelectedTabIndex = 2;

    [RelayCommand]
    private async Task OpenSettings()
    {
        await _windows.ShowSettingsAsync();
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsTagsTabSelected));
        OnPropertyChanged(nameof(IsImagesTabSelected));
        OnPropertyChanged(nameof(IsWorkTabSelected));
        if (value == 0)
        {
            _ = TagsTab.EnsureLoadedAsync();
        }
        else if (value == 2)
        {
            // 作業タブ(ECO-020): 画像タブでの受け渡し(追加)を反映するため毎回再読込(現スペース維持)
            _ = WorkTab.RefreshAsync();
        }
        else if (_imagesTabStale)
        {
            // タグタブでの永続変更(タグ・ビュー・階層)を画像タブへ反映(台帳再読込・状態保持)
            _imagesTabStale = false;
            _ = ImageTab.ReloadTagCatalogAsync();
        }
    }
}
