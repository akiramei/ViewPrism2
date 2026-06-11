using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace ViewPrism2.Infrastructure.Imaging;

/// <summary>
/// サムネイル生成サービス(M-THUMB-008、REQ-040、K-SKIA)。
/// 長辺 256px へ縮小(アスペクト比保持・拡大しない)。PNG 入力→PNG、その他→JPEG 品質 80。
/// キャッシュキー = MD5(画像絶対パスの小文字) hex。存在すれば再生成しない。
/// 生成失敗(壊れた画像)は null を返しキャッシュへ記録しない(次回表示時に再試行、FMEA-012)。
/// 読み取り不能なキャッシュファイルは削除して再生成する。元画像へは一切書き込まない(INV-009)。
/// </summary>
public sealed class ThumbnailService
{
    /// <summary>サムネイルの長辺上限(REQ-040)。</summary>
    public const int MaxDimension = 256;

    /// <summary>JPEG 出力品質(REQ-040)。</summary>
    public const int JpegQuality = 80;

    private readonly string _cacheDirectory;
    private readonly ILogger<ThumbnailService>? _logger;

    /// <param name="cacheDirectory">保存先。null なら %APPDATA%/ViewPrism2/thumbnails(受入は一時ディレクトリを注入)。</param>
    /// <param name="logger">警告ログ出力先。</param>
    public ThumbnailService(string? cacheDirectory = null, ILogger<ThumbnailService>? logger = null)
    {
        _cacheDirectory = cacheDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ViewPrism2", "thumbnails");
        _logger = logger;
    }

    public string CacheDirectory => _cacheDirectory;

    /// <summary>
    /// サムネイルを取得(なければ生成)する。戻り値はサムネイルファイルパス。
    /// 失敗時は null(キャッシュ記録なし。スキャン・一覧表示は停止させない)。
    /// </summary>
    public Task<string?> GetOrCreateAsync(string absoluteImagePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(absoluteImagePath);
        return Task.Run(() => GetOrCreate(absoluteImagePath));
    }

    /// <summary>解像度取得(REQ-043)。SKCodec によるヘッダ読みのみ(フルデコードなし)。失敗時 null。</summary>
    public Task<(int Width, int Height)?> GetDimensionsAsync(string absoluteImagePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(absoluteImagePath);
        return Task.Run<(int, int)?>(() =>
        {
            try
            {
                using var stream = OpenRead(absoluteImagePath);
                using var codec = SKCodec.Create(stream);
                return codec is null ? null : (codec.Info.Width, codec.Info.Height);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger?.LogWarning(ex, "解像度取得に失敗しました: {Path}", absoluteImagePath);
                return null;
            }
        });
    }

    /// <summary>キャッシュファイルパス(MD5(小文字絶対パス).{png|jpg})。</summary>
    public string GetCachePath(string absoluteImagePath)
    {
        var key = Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(absoluteImagePath.ToLowerInvariant())));
        var extension = IsPng(absoluteImagePath) ? ".png" : ".jpg";
        return Path.Combine(_cacheDirectory, key + extension);
    }

    private string? GetOrCreate(string absoluteImagePath)
    {
        var cachePath = GetCachePath(absoluteImagePath);
        try
        {
            if (File.Exists(cachePath))
            {
                if (IsReadableImage(cachePath))
                {
                    return cachePath; // 存在すれば再生成しない(REQ-040)
                }

                // 読み取り不能なキャッシュファイルは削除して再生成(REQ-040)
                _logger?.LogWarning("破損したサムネイルキャッシュを削除して再生成します: {Cache}", cachePath);
                File.Delete(cachePath);
            }

            return Generate(absoluteImagePath, cachePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "サムネイル生成に失敗しました: {Path}", absoluteImagePath);
            DeletePartial(cachePath);
            return null;
        }
    }

    private string? Generate(string sourcePath, string cachePath)
    {
        // K-SKIA: SKBitmap.Decode が null を返したら「壊れた画像」(例外を投げない)
        using var stream = OpenRead(sourcePath);
        using var bitmap = SKBitmap.Decode(stream);
        if (bitmap is null)
        {
            _logger?.LogWarning("壊れた画像のためサムネイルを生成できません: {Path}", sourcePath);
            return null;
        }

        // inside-fit 縮小: scale = Min(1.0, 256/width, 256/height)。scale==1.0 なら原寸のまま再エンコード(拡大禁止)
        var scale = Math.Min(1.0, Math.Min(
            (double)MaxDimension / bitmap.Width, (double)MaxDimension / bitmap.Height));

        SKBitmap? resized = null;
        try
        {
            var target = bitmap;
            if (scale < 1.0)
            {
                // 丸めは Round half away from zero・最小 1px(K-SKIA / M-BOM silence_sweep)
                var newWidth = Math.Max(1, (int)Math.Round(bitmap.Width * scale, MidpointRounding.AwayFromZero));
                var newHeight = Math.Max(1, (int)Math.Round(bitmap.Height * scale, MidpointRounding.AwayFromZero));
                resized = bitmap.Resize(
                    new SKImageInfo(newWidth, newHeight, bitmap.ColorType, bitmap.AlphaType),
                    new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
                if (resized is null)
                {
                    _logger?.LogWarning("リサイズに失敗しました: {Path}", sourcePath);
                    return null;
                }

                target = resized;
            }

            using var image = SKImage.FromBitmap(target);
            using var data = IsPng(sourcePath)
                ? image.Encode(SKEncodedImageFormat.Png, 100)
                : image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
            if (data is null)
            {
                _logger?.LogWarning("エンコードに失敗しました: {Path}", sourcePath);
                return null;
            }

            Directory.CreateDirectory(_cacheDirectory);
            using var output = File.Create(cachePath);
            data.SaveTo(output);
            return cachePath;
        }
        finally
        {
            resized?.Dispose();
        }
    }

    /// <summary>キャッシュファイルが画像としてデコード可能か(ヘッダ読みのみ)。</summary>
    private static bool IsReadableImage(string path)
    {
        try
        {
            using var stream = OpenRead(path);
            using var codec = SKCodec.Create(stream);
            return codec is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsPng(string path)
        => string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase);

    /// <summary>K-WINFS: 他プロセスのロックと共存する読み取り専用オープン(INV-009)。</summary>
    private static FileStream OpenRead(string path)
        => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    private void DeletePartial(string cachePath)
    {
        try
        {
            if (File.Exists(cachePath))
            {
                File.Delete(cachePath); // 失敗時はキャッシュ記録を残さない(REQ-040)
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "部分書き込みされたキャッシュの削除に失敗しました: {Cache}", cachePath);
        }
    }
}
