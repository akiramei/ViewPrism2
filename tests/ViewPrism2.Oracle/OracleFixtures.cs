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
}
