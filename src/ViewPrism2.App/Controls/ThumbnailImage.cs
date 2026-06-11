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
/// </summary>
public sealed class ThumbnailImage : Image
{
    public static readonly StyledProperty<string?> SourcePathProperty =
        AvaloniaProperty.Register<ThumbnailImage, string?>(nameof(SourcePath));

    /// <summary>コンポジションルート(App)が起動時に設定する。</summary>
    public static ThumbnailService? Service { get; set; }

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

    private async Task LoadAsync(string? path)
    {
        Source = null;
        if (path is null || Service is null)
        {
            return;
        }

        try
        {
            var thumbnailPath = await Service.GetOrCreateAsync(path);
            if (thumbnailPath is null)
            {
                return; // プレースホルダ表示のまま(REQ-040: 次回表示時に再試行)
            }

            var bitmap = await Task.Run(() => new Bitmap(thumbnailPath));

            // コンテナリサイクルで対象が変わっていたら破棄(仮想化対応)
            if (string.Equals(SourcePath, path, StringComparison.Ordinal))
            {
                Source = bitmap;
            }
            else
            {
                bitmap.Dispose();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // 一覧表示を停止させない(REQ-040)
        }
    }
}
