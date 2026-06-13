using SkiaSharp;
using ViewPrism2.Core.Common;
using ViewPrism2.Infrastructure.Database;

namespace ViewPrism2.Oracle;

/// <summary>
/// EQ-001 の同値規則(41-fixed-oracle.yaml): ID は pattern のみ・日時は形式のみ検査。
/// </summary>
internal static class OraclePatterns
{
    /// <summary>UUIDv4 小文字 36 文字(REQ-001)。</summary>
    public const string UuidV4 = "^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$";

    /// <summary>ISO 8601 UTC、ミリ秒 3 桁、literal Z(REQ-002 / INV-002)。</summary>
    public const string IsoUtc = @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$";
}

/// <summary>一時ファイル SQLite DB(tests/ViewPrism2.Tests/TempDb.cs と同型の設計者側治具)。</summary>
internal sealed class OracleDb : IDisposable
{
    public OracleDb(IClock? clock = null)
    {
        Clock = clock ?? new SystemClock();
        Directory = Path.Combine(Path.GetTempPath(), "ViewPrism2.Oracle", Guid.NewGuid().ToString("D"));
        DbPath = Path.Combine(Directory, "viewprism2.db");
        Manager = DatabaseManager.Open(DbPath, Clock);
        Folders = new SyncFolderRepository(Manager);
        Images = new ImageRepository(Manager);
        Tags = new TagRepository(Manager);
        Views = new ViewRepository(Manager);
    }

    public IClock Clock { get; }

    public string Directory { get; }

    public string DbPath { get; }

    public DatabaseManager Manager { get; }

    public SyncFolderRepository Folders { get; }

    public ImageRepository Images { get; }

    public TagRepository Tags { get; }

    public ViewRepository Views { get; }

    public void Dispose()
    {
        Manager.Dispose();
        try
        {
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // 一時ディレクトリの後始末失敗はオラクル判定に影響させない
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>実画像フィクスチャ(S-01/S-10)。SkiaSharp でテスト内生成する。</summary>
internal static class OracleImages
{
    /// <summary>単色画像を生成して path へ保存する(png/jpeg/webp)。色でバイト列=ハッシュを変える。</summary>
    public static void WriteEncoded(string path, int width, int height, SKEncodedImageFormat format, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(color);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90)
            ?? throw new InvalidOperationException($"encode 失敗: {format}");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }

    /// <summary>
    /// patternIndex で構造が変わる低周波グレースケール画像を path へ保存(S-25/能力プローブ用)。
    /// 同一 patternIndex+同一 shift は同一構造(format/quality 違いは知覚的に近傍=同じ pHash 近傍)。
    /// </summary>
    public static void WriteStructured(
        string path, int size, SKEncodedImageFormat format, int quality, int patternIndex, int brightnessShift = 0)
    {
        // 多周波の豊かなパターン(低周波成分が多く pHash ビットが安定 → JPEG 再エンコードに頑健)。
        // patternIndex で周波数/位相が変わり構造が distinct になる。単一周波数だと中央値近傍の
        // 係数が多くビットが不安定になり JPEG 微小ノイズで距離が暴れる(S-25 の near-dup が壊れる)。
        var fx1 = (patternIndex % 5) + 1;
        var fy1 = (patternIndex % 3) + 1;
        var fx2 = (patternIndex % 4) + 2;
        var fy2 = (patternIndex % 6) + 1;
        var fx3 = (patternIndex % 7) + 1;
        var fy3 = (patternIndex % 5) + 2;
        var ph = patternIndex * 0.7;
        using var bitmap = new SKBitmap(size, size);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                // 振幅合計 90(基準 128 に対し 38〜218 でクリップしない)→ 加算輝度シフトが純加算を保ち、
                // JPEG 再エンコードもハードエッジを生まず低周波 pHash が安定。
                var u = 2 * Math.PI / size;
                var v = (byte)Math.Clamp(
                    128
                    + (40 * Math.Cos((u * ((fx1 * x) + (fy1 * y))) + ph))
                    + (30 * Math.Cos((u * ((fx2 * x) + (fy2 * y))) + (ph * 1.3)))
                    + (20 * Math.Cos((u * ((fx3 * x) + (fy3 * y))) + (ph * 0.5)))
                    + brightnessShift,
                    0, 255);
                bitmap.SetPixel(x, y, new SKColor(v, v, v));
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, quality)
            ?? throw new InvalidOperationException($"encode 失敗: {format}");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }
}
