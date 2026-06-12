using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using ViewPrism2.App.ViewModels;
using ViewPrism2.Core.Services;

namespace ViewPrism2.App.Views;

/// <summary>
/// ビューアの View(M-UI-014)。ナビゲーションは ViewerViewModel、
/// ここは ImageMemoryCache(REQ-045)経由の画像ロードと Source 代入のみ。
/// デコードは UI スレッド外(Task.Run)、代入は UI スレッド(K-AVALONIA)。
/// </summary>
public partial class ViewerWindow : Window
{
    private readonly ImageMemoryCache _cache;
    private ViewerViewModel? _viewModel;

    // XAML プレビュー/ランタイムローダ用(未使用経路)
    public ViewerWindow()
        : this(new ImageMemoryCache(new Core.Common.SystemClock()))
    {
    }

    public ViewerWindow(ImageMemoryCache cache)
    {
        _cache = cache;
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as ViewerViewModel);
    }

    private void Attach(ViewerViewModel? viewModel)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.CloseRequested -= OnCloseRequested;
        }

        _viewModel = viewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.CloseRequested += OnCloseRequested;
            _ = LoadCurrentAsync();
        }
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();

    /// <summary>
    /// 画像外余白のクリックで閉じる(REQ-044 v1.3/CR-7)。
    /// 実描画領域(Uniform + DownOnly のフィット計算)は ViewerViewModel.IsBackgroundPoint(純粋関数)。
    /// </summary>
    private void OnBackgroundPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel is null || !e.GetCurrentPoint(ImageHost).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var point = e.GetPosition(ImageView);
        var source = ImageView.Source;
        if (ViewerViewModel.IsBackgroundPoint(
                ImageView.Bounds.Width, ImageView.Bounds.Height,
                source?.Size.Width ?? 0, source?.Size.Height ?? 0,
                point.X, point.Y))
        {
            _viewModel.CloseCommand.Execute(null);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewerViewModel.CurrentImagePath))
        {
            _ = LoadCurrentAsync();
        }
    }

    private async Task LoadCurrentAsync()
    {
        var path = _viewModel?.CurrentImagePath;
        if (path is null)
        {
            ImageView.Source = null;
            return;
        }

        try
        {
            // フルサイズ表示キャッシュ(REQ-045): メモリ LRU 50 枚・TTL 3 分
            var bitmap = await _cache.GetOrAddAsync(path, () => Task.Run(() => new Bitmap(path)));
            if (string.Equals(_viewModel?.CurrentImagePath, path, StringComparison.Ordinal))
            {
                ImageView.Source = bitmap;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ImageView.Source = null; // 壊れた画像・消失でもクラッシュしない
        }
    }
}
