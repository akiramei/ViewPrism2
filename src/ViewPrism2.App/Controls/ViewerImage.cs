using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace ViewPrism2.App.Controls;

/// <summary>
/// ビューア(scroll/spread)用の単一画像表示部品(M-UI-018)。
/// パスからフルサイズ Bitmap を UI スレッド外(Task.Run)で生成し、Source 代入は UI スレッド(K-AVALONIA)。
/// 個別画像のロード失敗は当該位置にエラー表示(ファイル名+失敗の旨)を出し、前後の閲覧を継続できる
/// (ビューア全体を壊さない — 仕様 §2.9)。
///
/// 仮想化ライフサイクル(K-AVALONIA v2.0/Run2):
/// 可視(仮想化で実体化中)の間のみ SourcePath を持ち、非可視化/コンテナリサイクル時は SourcePath=null に
/// して Bitmap を Dispose(描画から外した後 — 描画中 Dispose のネイティブクラッシュ回避)。進行中ロードは
/// CancellationToken でキャンセルする。これにより画面外アイテムは Bitmap を保持せず、メモリは「画面内+
/// 先読みウィンドウ」に比例する(総枚数に比例しない)。
/// </summary>
public sealed class ViewerImage : ContentControl
{
    public static readonly StyledProperty<string?> SourcePathProperty =
        AvaloniaProperty.Register<ViewerImage, string?>(nameof(SourcePath));

    public static readonly StyledProperty<string?> ErrorTemplateProperty =
        AvaloniaProperty.Register<ViewerImage, string?>(nameof(ErrorTemplate));

    private readonly Image _image;
    private readonly TextBlock _error;
    private Bitmap? _bitmap;

    /// <summary>進行中ロードのキャンセル元。SourcePath 変更/Release のたびに張り替える。</summary>
    private CancellationTokenSource? _loadCts;

    public ViewerImage()
    {
        _image = new Image
        {
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _error = new TextBlock
        {
            IsVisible = false,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8),
            Foreground = Brushes.Gray,
        };
        Content = new Panel { Children = { _image, _error } };
    }

    /// <summary>表示する画像の絶対パス。null で空表示。</summary>
    public string? SourcePath
    {
        get => GetValue(SourcePathProperty);
        set => SetValue(SourcePathProperty, value);
    }

    /// <summary>ロード失敗時の文言テンプレート(例: "{fileName} を読み込めませんでした")。{fileName} を置換。</summary>
    public string? ErrorTemplate
    {
        get => GetValue(ErrorTemplateProperty);
        set => SetValue(ErrorTemplateProperty, value);
    }

    /// <summary>
    /// 表示を解除して Bitmap を破棄し、進行中ロードをキャンセルする(コンテナリサイクル/非可視化時)。
    /// 描画から外した後に Dispose する(描画中 Dispose のネイティブクラッシュ回避 — K-AVALONIA v2.0)。
    /// SourcePath も null にして「可視の間のみパスを持つ」契約を保つ。
    /// </summary>
    public void Release()
    {
        // 進行中ロードを止める(画面外へ出たら不要 — 帯域・メモリの節約)
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;

        _image.Source = null; // 描画から外す
        var old = _bitmap;
        _bitmap = null;
        old?.Dispose(); // 外した後に破棄
        _error.IsVisible = false;

        // 可視の間のみ SourcePath を持つ契約: 解除時は null へ(OnPropertyChanged 再入で再ロードしない)
        SourcePath = null;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourcePathProperty)
        {
            _ = LoadAsync(change.GetNewValue<string?>());
        }
    }

    private async Task LoadAsync(string? path)
    {
        // 旧ロードをキャンセルし、旧 Bitmap は Source から外してから破棄(描画中 Dispose 回避 — K-AVALONIA v2.0)
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;

        _image.Source = null;
        var old = _bitmap;
        _bitmap = null;
        old?.Dispose();

        _error.IsVisible = false;
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _loadCts = cts;
        var token = cts.Token;

        try
        {
            // UI スレッド外でデコード(K-AVALONIA)。完了後に対象が変わっていれば破棄する。
            var bitmap = await Task.Run(() => new Bitmap(path), token).ConfigureAwait(true);

            // この間にリサイクル/別パスへ差し替わっていたら、描画に載せず破棄(仮想化/リサイクル)
            if (token.IsCancellationRequested || !string.Equals(SourcePath, path, StringComparison.Ordinal))
            {
                bitmap.Dispose();
                return;
            }

            _bitmap = bitmap;
            _image.Source = bitmap;
        }
        catch (OperationCanceledException)
        {
            // 画面外へ出た/差し替わった: 何も表示しない(正常系)
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // 失敗= ファイル名+失敗の旨(ビューア全体は継続。仕様 §2.9)。対象が変わっていれば表示しない。
            if (token.IsCancellationRequested || !string.Equals(SourcePath, path, StringComparison.Ordinal))
            {
                return;
            }

            var fileName = System.IO.Path.GetFileName(path);
            var template = ErrorTemplate;
            _error.Text = string.IsNullOrEmpty(template)
                ? fileName
                : template.Replace("{fileName}", fileName, StringComparison.Ordinal);
            _error.IsVisible = true;
        }
    }
}
