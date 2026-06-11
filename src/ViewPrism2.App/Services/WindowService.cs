using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ViewPrism2.App.ViewModels;
using ViewPrism2.App.Views;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;
using ViewPrism2.Core.Services;
using ViewPrism2.Infrastructure.Scanning;
using ViewPrism2.Infrastructure.Settings;

namespace ViewPrism2.App.Services;

/// <summary>
/// IWindowService の View 層実装(K-MVVM: ダイアログ表示の実体)。
/// Window.ShowDialog / StorageProvider.OpenFolderPickerAsync(K-AVALONIA)。
/// </summary>
public sealed class WindowService : IWindowService
{
    private readonly ISyncFolderRepository _folders;
    private readonly IImageRepository _images;
    private readonly ITagRepository _tags;
    private readonly TagService _tagService;
    private readonly ViewService _viewService;
    private readonly ScanService _scan;
    private readonly RelinkService _relink;
    private readonly ImageMemoryCache _imageCache;
    private readonly LocalizationService _localization;
    private readonly AppSettings _settings;
    private readonly SettingsStore _settingsStore;

    public WindowService(
        ISyncFolderRepository folders,
        IImageRepository images,
        ITagRepository tags,
        TagService tagService,
        ViewService viewService,
        ScanService scan,
        RelinkService relink,
        ImageMemoryCache imageCache,
        LocalizationService localization,
        AppSettings settings,
        SettingsStore settingsStore)
    {
        _folders = folders;
        _images = images;
        _tags = tags;
        _tagService = tagService;
        _viewService = viewService;
        _scan = scan;
        _relink = relink;
        _imageCache = imageCache;
        _localization = localization;
        _settings = settings;
        _settingsStore = settingsStore;
    }

    /// <summary>モーダルダイアログのオーナー(App 起動時に設定)。</summary>
    public Window? Owner { get; set; }

    public async Task<bool> ConfirmAsync(string title, string message)
    {
        if (Owner is null)
        {
            return false;
        }

        var dialog = new ConfirmDialog(new LocalizationProxy(_localization), title, message);
        return await dialog.ShowDialog<bool?>(Owner) == true;
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        if (Owner is null)
        {
            return null;
        }

        var results = await Owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        return results.Count > 0 ? results[0].TryGetLocalPath() : null;
    }

    public async Task ShowFolderManagementAsync()
    {
        if (Owner is null)
        {
            return;
        }

        var vm = new FolderManagementViewModel(_folders, _scan, _localization, this);
        var window = new FolderManagementWindow { DataContext = vm };
        await vm.LoadAsync();
        await window.ShowDialog(Owner);
    }

    public async Task ShowSettingsAsync()
    {
        if (Owner is null)
        {
            return;
        }

        var vm = new SettingsViewModel(_localization, _settings, _settingsStore);
        var window = new SettingsWindow { DataContext = vm };
        await window.ShowDialog(Owner);
    }

    public async Task<bool> ShowTagEditorAsync(Tag? existing)
    {
        if (Owner is null)
        {
            return false;
        }

        var vm = new TagEditorViewModel(existing, _tagService, _tags, _localization);
        var window = new TagEditorWindow { DataContext = vm };
        vm.Saved += (_, _) => window.Close(true);
        await vm.LoadAsync();
        return await window.ShowDialog<bool?>(Owner) == true;
    }

    public async Task<bool> ShowViewEditDialogAsync(View? existing)
    {
        if (Owner is null)
        {
            return false;
        }

        var vm = new ViewEditDialogViewModel(existing, _viewService, _localization);
        var window = new ViewEditDialog { DataContext = vm };
        vm.Saved += (_, _) => window.Close(true);
        return await window.ShowDialog<bool?>(Owner) == true;
    }

    public async Task<IReadOnlyList<string>?> ShowNumericValueDialogAsync(
        Tag tag, NumericTagSettings? settings, int selectionCount)
    {
        if (Owner is null)
        {
            return null;
        }

        var vm = new NumericValueDialogViewModel(tag, settings, selectionCount, _localization);
        var window = new NumericValueDialog { DataContext = vm };
        return await window.ShowDialog<IReadOnlyList<string>?>(Owner);
    }

    public async Task<NodeConditionResult?> ShowNodeConditionDialogAsync(
        Tag tag, HierarchyConditionType? currentType, string? currentValueJson)
    {
        if (Owner is null)
        {
            return null;
        }

        var vm = new NodeConditionDialogViewModel(tag, currentType, currentValueJson, _localization);
        var window = new NodeConditionDialog { DataContext = vm };
        return await window.ShowDialog<NodeConditionResult?>(Owner);
    }

    public async Task ShowRelinkAsync(string folderId)
    {
        if (Owner is null)
        {
            return;
        }

        var vm = new RelinkViewModel(folderId, _images, _relink, _localization, this);
        var window = new RelinkWindow { DataContext = vm };
        await vm.LoadAsync();
        await window.ShowDialog(Owner);
    }

    public void ShowViewer(IReadOnlyList<ImageEntry> ordered, int startIndex)
    {
        var vm = new ViewerViewModel(ordered, startIndex);
        var window = new ViewerWindow(_imageCache) { DataContext = vm };
        window.Show(Owner!);
    }
}
