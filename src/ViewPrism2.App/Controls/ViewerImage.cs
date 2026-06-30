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
/// 可視(仮想化で実体化中)の間のみ Bitmap を持ち、非可視化/コンテナリサイクル時は Source を外して
/// Bitmap を Dispose(描画から外した後 — 描画中 Dispose のネイティブクラッシュ回避)。進行中ロードは
/// CancellationToken でキャンセルする。これにより画面外アイテムは Bitmap を保持せず、メモリは「画面内+
/// 先読みウィンドウ」に比例する(総枚数に比例しない)。
/// </summary>
public sealed class ViewerImage : ContentControl
{
    public static readonly StyledProperty<string?> SourcePathProperty =
        AvaloniaProperty.Register<ViewerImage, string?>(nameof(SourcePath));

    public static readonly StyledProperty<string?> ErrorTemplateProperty =
        AvaloniaProperty.Register<ViewerImage, string?>(nameof(ErrorTemplate));

    /// <summary>
    /// セッション内寸法キャッシュ(高さ予約・GF-V2/①-lite)。App 起動時に DI から設定する
    /// (<see cref="ThumbnailImage.Service"/> と同じ静的注入)。null なら高さ予約せず従来挙動。
    /// </summary>
    public static ImageDimensionCache? DimensionCache { get; set; }

    private readonly Image _image;
    private readonly TextBlock _error;
    private Bitmap? _bitmap;

    /// <summary>現在 SourcePath の画素寸法(キャッシュ/ヘッダ読み)。デコード前の枠高予約に使う。</summary>
    private (int Width, int Height)? _dims;

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
    /// SourcePath は binding された入力なので触らない。リサイクル中にローカル値で上書きすると、
    /// binding と仮想化の再測定が余計に揺れ、同じコンテナを再利用したときの再ロード契約も壊しうる。
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
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // 寸法予約をロードより先に(意図順: デコード前に高さを確定して extent を安定させる)。
        // _dims は Release でも保持するため、多くの再アタッチでは確定済みで何もしない。
        if (_dims is null && !string.IsNullOrEmpty(SourcePath))
        {
            ApplyDimensions(SourcePath);
        }

        // リサイクルで同じコンテナが同じ SourcePath に戻ると binding 値が変化せず
        // OnPropertyChanged が発火しないことがある。Bitmap が解放済みなら添付時に再ロードする。
        if (_loadCts is null && _bitmap is null && _image.Source is null && !string.IsNullOrEmpty(SourcePath))
        {
            _ = LoadAsync(SourcePath);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourcePathProperty)
        {
            var path = change.GetNewValue<string?>();
            ApplyDimensions(path);   // 高さ予約はデコードより先(デコード前に extent を安定させる)
            _ = LoadAsync(path);
        }
    }

    /// <summary>
    /// SourcePath の画素寸法を反映して枠高を予約する(GF-V2)。キャッシュ命中なら即・InvalidateMeasure。
    /// 未キャッシュは背景でヘッダ読み(フルデコードより軽量)→ 到着後に反映。
    /// </summary>
    private void ApplyDimensions(string? path)
    {
        _dims = null;
        InvalidateMeasure();
        if (string.IsNullOrEmpty(path) || DimensionCache is not { } cache)
        {
            return;
        }

        if (cache.TryGet(path, out var dims))
        {
            _dims = dims;
            InvalidateMeasure();
            return;
        }

        _ = ReadDimensionsAsync(path, cache);
    }

    private async Task ReadDimensionsAsync(string path, ImageDimensionCache cache)
    {
        var read = await cache.GetOrReadAsync(path).ConfigureAwait(true);
        // 読み取り中にリサイクル/差し替えが起きていたら無視(現 SourcePath のものだけ反映)
        if (read is { } d && string.Equals(SourcePath, path, StringComparison.Ordinal))
        {
            _dims = d;
            InvalidateMeasure();
        }
    }

    /// <summary>
    /// 寸法既知なら、利用可能幅(縦スクロール=ビューポート幅・縦は無限)に対する Uniform/DownOnly の
    /// 表示寸法を「デコード前に」返す。これにより Bitmap ロード完了で item 高さがジャンプせず、
    /// 仮想化の extent 推定が安定して暴走しない(GF-V2)。寸法未知なら従来測定。
    /// </summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        if (_dims is { } d && d.Width > 0 && d.Height > 0 &&
            double.IsFinite(availableSize.Width) && availableSize.Width > 0)
        {
            // DownOnly: 拡大しない(scale ≤ 1)。縦が有限(見開きセル)なら両制約に収める。
            var scale = Math.Min(1.0, availableSize.Width / d.Width);
            if (double.IsFinite(availableSize.Height) && availableSize.Height > 0)
            {
                scale = Math.Min(scale, availableSize.Height / d.Height);
            }

            var reserved = new Size(d.Width * scale, d.Height * scale);
            base.MeasureOverride(reserved); // 子(Image/error)を確定枠で測る
            return reserved;
        }

        return base.MeasureOverride(availableSize);
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
