using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using ViewPrism2.Infrastructure.Imaging;

namespace ViewPrism2.App.Controls;

/// <summary>
/// サムネイル遅延ロード付き Image(View 層の表示部品)。
/// セルが実体化されたときに ThumbnailService(M-THUMB-008)で生成・取得し、
/// デコードは UI スレッド外(Task.Run)、Source 代入は UI スレッド(K-AVALONIA)。
/// 生成失敗(null)は Source を設定せず、背後のプレースホルダ('?' グリフ)が見えたままになる。
///
/// 仮想化ライフサイクル(ECO-026/#6・K-AVALONIA v2.0/Run2 規律をサムネへ横展開):
/// SourcePath 変更/非可視化(detach)時に進行中ロードを CancellationToken でキャンセルし、Source を外して
/// から Bitmap を Dispose する(描画中 Dispose のネイティブクラッシュ回避)。これにより仮想化グリッド
/// (ECO-026/#1)で画面外セルがリサイクルされても Bitmap を保持し続けない。リサイクルで同じ SourcePath へ
/// 戻り binding が発火しない場合は再アタッチ時に再ロードする(<see cref="ViewerImage"/> と同じ契約)。
/// </summary>
public sealed class ThumbnailImage : Image
{
    public static readonly StyledProperty<string?> SourcePathProperty =
        AvaloniaProperty.Register<ThumbnailImage, string?>(nameof(SourcePath));

    /// <summary>コンポジションルート(App)が起動時に設定する。</summary>
    public static ThumbnailService? Service { get; set; }

    private Bitmap? _bitmap;
    private CancellationTokenSource? _loadCts;

    public string? SourcePath
    {
        get => GetValue(SourcePathProperty);
        set => SetValue(SourcePathProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourcePathProperty)
        {
            _ = LoadAsync(change.GetNewValue<string?>());
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        // 非可視化/コンテナリサイクルで Bitmap を保持し続けない(進行中ロードもキャンセル)。
        Release();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // リサイクルで同じコンテナが同じ SourcePath に戻ると binding 値が変化せず OnPropertyChanged が
        // 発火しないことがある。解放済みなら添付時に再ロードする。
        if (_loadCts is null && _bitmap is null && Source is null && !string.IsNullOrEmpty(SourcePath))
        {
            _ = LoadAsync(SourcePath);
        }
    }

    /// <summary>進行中ロードをキャンセルし、Source を外してから Bitmap を破棄する(描画中 Dispose 回避)。</summary>
    private void Release()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;

        Source = null; // 描画から外す
        var old = _bitmap;
        _bitmap = null;
        old?.Dispose(); // 外した後に破棄
    }

    private async Task LoadAsync(string? path)
    {
        // 旧ロードをキャンセルし、旧 Bitmap は Source から外してから破棄(描画中 Dispose 回避 — K-AVALONIA v2.0)
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;

        Source = null;
        var old = _bitmap;
        _bitmap = null;
        old?.Dispose();

        if (path is null || Service is null)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _loadCts = cts;
        var token = cts.Token;

        try
        {
            var thumbnailPath = await Service.GetOrCreateAsync(path);
            if (thumbnailPath is null || token.IsCancellationRequested)
            {
                return; // プレースホルダ表示のまま(REQ-040: 次回表示時に再試行)
            }

            var bitmap = await Task.Run(() => new Bitmap(thumbnailPath));

            // ロード中に対象が変わった/リサイクルされたら破棄(仮想化対応)
            if (token.IsCancellationRequested || !string.Equals(SourcePath, path, StringComparison.Ordinal))
            {
                bitmap.Dispose();
                return;
            }

            _bitmap = bitmap;
            Source = bitmap;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // 一覧表示を停止させない(REQ-040)
        }
    }
}
