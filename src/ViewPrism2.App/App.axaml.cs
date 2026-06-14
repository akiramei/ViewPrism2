using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using ViewPrism2.App.Services;
using ViewPrism2.App.ViewModels;
using ViewPrism2.App.Views;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;
using ViewPrism2.Core.Services;
using ViewPrism2.Core.Services.Repair;
using ViewPrism2.Core.Services.Similarity;
using ViewPrism2.Infrastructure.Database;
using ViewPrism2.Infrastructure.I18n;
using ViewPrism2.Infrastructure.Imaging;
using ViewPrism2.Infrastructure.Scanning;
using ViewPrism2.Infrastructure.Settings;

namespace ViewPrism2.App;

/// <summary>
/// アプリ合成ルート(M-SLN-000: DI は Microsoft.Extensions.DependencyInjection で App 起動時に合成)。
/// データ配置は %APPDATA%/ViewPrism2/(settings.json・viewprism2.db・thumbnails/・logs/)。
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _provider;
    private MainWindowViewModel? _mainViewModel;
    private SettingsStore? _settingsStore;
    private AppSettings? _settings;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // データ配置の既定は %APPDATA%/ViewPrism2。CP-L1-SMOKE の隔離実行用に
            // 環境変数 VIEWPRISM2_DATA_DIR で差し替え可能(未設定時は従来どおり)
            var appDataDir = Environment.GetEnvironmentVariable("VIEWPRISM2_DATA_DIR")
                is { Length: > 0 } overrideDir
                ? overrideDir
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ViewPrism2");
            _provider = ConfigureServices(appDataDir);

            ThumbnailImageServiceInit(_provider);
            _settingsStore = _provider.GetRequiredService<SettingsStore>();
            _settings = _provider.GetRequiredService<AppSettings>();

            // DF-1(NFR-002): グローバル例外ハンドラ — UI 例外でプロセスを終了させない。
            // ログ(%APPDATA%/ViewPrism2/logs/)+非モーダル通知(ステータスバー)
            var localization = _provider.GetRequiredService<LocalizationService>();
            var exceptions = _provider.GetRequiredService<GlobalExceptionHandler>();
            exceptions.FormatNotification = _ => localization.T("error.unhandled");
            exceptions.NotificationRequested += (_, message) =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (_mainViewModel is not null)
                    {
                        _mainViewModel.StatusMessage = message;
                    }
                });
            exceptions.Register();

            _mainViewModel = _provider.GetRequiredService<MainWindowViewModel>();
            var window = new MainWindow { DataContext = _mainViewModel };
            _provider.GetRequiredService<WindowService>().Owner = window;

            RestoreWindowState(window, _settings);
            window.Closing += (_, _) => SaveWindowState(window);
            window.Opened += async (_, _) => await _mainViewModel.InitializeAsync();

            desktop.MainWindow = window;
            desktop.Exit += (_, _) => _provider.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ThumbnailImageServiceInit(ServiceProvider provider)
        => Controls.ThumbnailImage.Service = provider.GetRequiredService<ThumbnailService>();

    private static ServiceProvider ConfigureServices(string appDataDir)
    {
        var services = new ServiceCollection();

        // ログ(M-BOM silence_sweep): %APPDATA%/ViewPrism2/logs/app-.log 日次ローリング 7 世代・Information 以上
        var serilog = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(appDataDir, "logs", "app-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();
        services.AddSingleton<ILoggerFactory>(new SerilogLoggerFactory(serilog, dispose: true));
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));

        // 堅牢性(NFR-002 / DF-1): グローバル例外ハンドラ
        services.AddSingleton(sp => new GlobalExceptionHandler(
            sp.GetRequiredService<ILogger<GlobalExceptionHandler>>()));

        // Core 共通
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton(sp => new SettingsStore(appDataDir));
        services.AddSingleton(sp => sp.GetRequiredService<SettingsStore>().Load());

        // i18n(M-I18N-011): Assets/i18n の翻訳資産+settings.locale
        services.AddSingleton(sp =>
        {
            var resources = I18nResourceLoader.Load(
                Path.Combine(AppContext.BaseDirectory, "Assets", "i18n"),
                sp.GetRequiredService<ILogger<LocalizationService>>());
            return new LocalizationService(resources, sp.GetRequiredService<AppSettings>().Locale);
        });

        // 永続化(M-DB-007): 単一共有接続+SemaphoreSlim
        services.AddSingleton(sp => DatabaseManager.Open(
            Path.Combine(appDataDir, "viewprism2.db"), sp.GetRequiredService<IClock>()));
        services.AddSingleton<ISyncFolderRepository>(sp => new SyncFolderRepository(sp.GetRequiredService<DatabaseManager>()));
        services.AddSingleton<IImageRepository>(sp => new ImageRepository(sp.GetRequiredService<DatabaseManager>()));
        services.AddSingleton<ITagRepository>(sp => new TagRepository(sp.GetRequiredService<DatabaseManager>()));
        services.AddSingleton<IViewRepository>(sp => new ViewRepository(sp.GetRequiredService<DatabaseManager>()));

        // v3.0 類似検索/マージの永続化(M-SIMSEARCH-021 / M-MERGE-022)
        services.AddSingleton<IImageFeatureRepository>(sp => new ImageFeatureRepository(sp.GetRequiredService<DatabaseManager>()));
        services.AddSingleton<IImageSimilarityRepository>(sp => new ImageSimilarityRepository(sp.GetRequiredService<DatabaseManager>()));
        services.AddSingleton<IMergeRepository>(sp => new MergeRepository(sp.GetRequiredService<DatabaseManager>()));

        // Core サービス(K-MVVM: シングルトン)
        services.AddSingleton<ConditionEvaluator>();
        services.AddSingleton<NodeGraphBuilder>();
        services.AddSingleton<PathConditionConverter>();
        services.AddSingleton<ImageSorter>();
        services.AddSingleton(sp => new ImageMemoryCache(sp.GetRequiredService<IClock>()));
        services.AddSingleton(sp => new TagService(sp.GetRequiredService<ITagRepository>()));
        services.AddSingleton(sp => new ViewService(sp.GetRequiredService<IViewRepository>(), sp.GetRequiredService<IClock>()));

        // v3.0 類似検索・マージ(M-PHASH-020 / M-SIMSEARCH-021 / M-MERGE-022)。
        // P-09: production pHash adapter は scaled-decode(早期縮小)= AdapterId "skia-scaled-decode-v1"。
        // full-decode(PHashImageReader)から世代交代(P-08 で 6.29× 高速・順位等価 EQ-RANK 緑)。
        // 旧 full-decode で永続化された pHash は hash_adapter 不一致で自動再計算される(混在なし)。
        services.AddSingleton<IPHashImageReader>(sp => new PHashImageReaderScaledDecode(
            sp.GetRequiredService<ILogger<PHashImageReaderScaledDecode>>()));
        services.AddSingleton(sp => new SimilaritySearchService(
            sp.GetRequiredService<ISyncFolderRepository>(),
            sp.GetRequiredService<IImageRepository>(),
            sp.GetRequiredService<IImageFeatureRepository>(),
            sp.GetRequiredService<IImageSimilarityRepository>(),
            sp.GetRequiredService<IPHashImageReader>(),
            sp.GetRequiredService<IClock>()));
        services.AddSingleton(sp => new MergeService(
            sp.GetRequiredService<IImageRepository>(),
            sp.GetRequiredService<ITagRepository>(),
            sp.GetRequiredService<IMergeRepository>()));

        // v4.0 修復ライフサイクル(M-CRITERIA-024 / M-TRASH-026)。
        // FilePresenceProbe(Infrastructure)=File.Exists のみ。TrashService(Core)は bool を受けて遷移判断(INV-009)
        services.AddSingleton<IFilePresenceProbe>(sp => new FilePresenceProbe());
        services.AddSingleton(sp => new CriteriaSearchService(sp.GetRequiredService<IImageRepository>()));
        services.AddSingleton(sp => new TrashService(
            sp.GetRequiredService<IImageRepository>(),
            sp.GetRequiredService<ISyncFolderRepository>(),
            sp.GetRequiredService<IFilePresenceProbe>()));

        // Infrastructure サービス
        services.AddSingleton(sp => new ScanService(
            sp.GetRequiredService<ISyncFolderRepository>(),
            sp.GetRequiredService<IImageRepository>(),
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<ILogger<ScanService>>()));
        services.AddSingleton(sp => new RelinkService(
            sp.GetRequiredService<IImageRepository>(),
            sp.GetRequiredService<ITagRepository>()));
        services.AddSingleton(sp => new ThumbnailService(
            Path.Combine(appDataDir, "thumbnails"),
            sp.GetRequiredService<ILogger<ThumbnailService>>()));

        // View 層サービスと ViewModel(K-MVVM: ViewModel はトランジェント)
        services.AddSingleton<WindowService>();
        services.AddSingleton<IWindowService>(sp => sp.GetRequiredService<WindowService>());
        services.AddTransient(sp => new FolderManagementViewModel(
            sp.GetRequiredService<ISyncFolderRepository>(),
            sp.GetRequiredService<ScanService>(),
            sp.GetRequiredService<LocalizationService>(),
            sp.GetRequiredService<IWindowService>()));
        services.AddTransient(sp => new TagsTabViewModel(
            sp.GetRequiredService<ViewService>(),
            sp.GetRequiredService<TagService>(),
            sp.GetRequiredService<ITagRepository>(),
            sp.GetRequiredService<LocalizationService>(),
            sp.GetRequiredService<IWindowService>()));
        services.AddTransient(sp => new TaggingPanelViewModel(
            sp.GetRequiredService<TagService>(),
            sp.GetRequiredService<ITagRepository>(),
            sp.GetRequiredService<LocalizationService>(),
            sp.GetRequiredService<IWindowService>()));
        services.AddTransient(sp => new MainWindowViewModel(
            sp.GetRequiredService<ISyncFolderRepository>(),
            sp.GetRequiredService<IImageRepository>(),
            sp.GetRequiredService<ITagRepository>(),
            sp.GetRequiredService<ViewService>(),
            sp.GetRequiredService<NodeGraphBuilder>(),
            sp.GetRequiredService<PathConditionConverter>(),
            sp.GetRequiredService<ConditionEvaluator>(),
            sp.GetRequiredService<ImageSorter>(),
            sp.GetRequiredService<ThumbnailService>(),
            sp.GetRequiredService<LocalizationService>(),
            sp.GetRequiredService<AppSettings>(),
            sp.GetRequiredService<IWindowService>(),
            sp.GetRequiredService<FolderManagementViewModel>(),
            sp.GetRequiredService<TagsTabViewModel>(),
            sp.GetRequiredService<TaggingPanelViewModel>(),
            sp.GetRequiredService<ILogger<MainWindowViewModel>>()));

        return services.BuildServiceProvider();
    }

    /// <summary>ウィンドウ位置・サイズ・最大化状態の復元(REQ-052)。</summary>
    private static void RestoreWindowState(Window window, AppSettings settings)
    {
        window.Width = settings.WindowWidth;
        window.Height = settings.WindowHeight;
        if (settings.WindowX is { } x && settings.WindowY is { } y)
        {
            window.Position = new PixelPoint(x, y);
        }

        if (settings.IsMaximized)
        {
            window.WindowState = WindowState.Maximized;
        }
    }

    /// <summary>終了時の永続化(REQ-052 / K-AVALONIA: Closing で読み settings.json へ)。</summary>
    private void SaveWindowState(Window window)
    {
        if (_settings is null || _settingsStore is null)
        {
            return;
        }

        if (window.WindowState == WindowState.Normal)
        {
            _settings.WindowX = window.Position.X;
            _settings.WindowY = window.Position.Y;
            _settings.WindowWidth = (int)window.ClientSize.Width;
            _settings.WindowHeight = (int)window.ClientSize.Height;
        }

        _settings.IsMaximized = window.WindowState == WindowState.Maximized;
        _mainViewModel?.CaptureSettings();
        _settingsStore.Save(_settings);
    }
}
