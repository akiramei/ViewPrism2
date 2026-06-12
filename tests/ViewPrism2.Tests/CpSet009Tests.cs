using ViewPrism2.Core.Models;
using ViewPrism2.Infrastructure.Settings;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>CP-SET-009: 設定ストアのラウンドトリップ・破損耐性が REQ-052 と一致する。</summary>
[Trait("cp", "CP-SET-009")]
public sealed class CpSet009Tests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "ViewPrism2.Tests", Guid.NewGuid().ToString("D"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // 一時ディレクトリの後始末失敗はテスト結果に影響させない
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void 全項目ラウンドトリップ()
    {
        // REQ-052 v1.3: 表示モード(CR-6)・最後に選択したコレクション(CR-5)を含む全キー
        var store = new SettingsStore(_directory);
        var settings = new AppSettings
        {
            Locale = "en",
            WindowX = 10,
            WindowY = 20,
            WindowWidth = 1600,
            WindowHeight = 900,
            IsMaximized = true,
            DisplayMode = "list",
            LastViewId = "view-001",
            LastCollectionId = "col-001",
        };

        store.Save(settings);
        var loaded = new SettingsStore(_directory).Load();

        Assert.Equal(settings, loaded);
        Assert.Equal("list", loaded.DisplayMode);
        Assert.Equal("col-001", loaded.LastCollectionId);
    }

    [Fact]
    public void 旧grid_columnsキーは残存しても無視され書き出されない()
    {
        // REQ-052 v1.3/CR-1: グリッド列数キーは廃止(残存しても無視)
        Directory.CreateDirectory(_directory);
        var store = new SettingsStore(_directory);
        File.WriteAllText(store.SettingsFilePath, """
            {
              "locale": "en",
              "gridColumns": 6,
              "lastViewId": "view-001"
            }
            """);

        var loaded = store.Load(); // 例外なく読める(旧キーは無視)

        Assert.Equal("en", loaded.Locale);
        Assert.Equal("view-001", loaded.LastViewId);
        Assert.Equal(4, loaded.GridColumns); // 旧キーの値 6 は読み込まれない(既定値のまま)

        store.Save(loaded); // 再保存で旧キーは消える(書き出さない)
        var json = File.ReadAllText(store.SettingsFilePath);
        Assert.DoesNotContain("gridColumns", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("displayMode", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void 壊れたJSONは既定値_bak退避_再生成_例外なし_FMEA009()
    {
        Directory.CreateDirectory(_directory);
        var store = new SettingsStore(_directory);
        File.WriteAllText(store.SettingsFilePath, "{{{");

        var loaded = store.Load(); // 例外を漏らさない

        Assert.Equal(new AppSettings(), loaded);                       // 既定値
        Assert.True(File.Exists(store.BackupFilePath));                // .bak へ退避
        Assert.Equal("{{{", File.ReadAllText(store.BackupFilePath));
        Assert.True(File.Exists(store.SettingsFilePath));              // 再生成済み
        Assert.Equal(new AppSettings(), store.Load());                 // 再生成ファイルは正常に読める
    }

    [Fact]
    public void ファイル欠落は既定値()
    {
        var loaded = new SettingsStore(_directory).Load();
        Assert.Equal(new AppSettings(), loaded);
    }

    [Fact]
    public void 既定値スキーマ_M_SET_010_v13()
    {
        var defaults = new AppSettings();
        Assert.Equal("ja", defaults.Locale);
        Assert.Null(defaults.WindowX);
        Assert.Null(defaults.WindowY);
        Assert.Equal(1200, defaults.WindowWidth);
        Assert.Equal(800, defaults.WindowHeight);
        Assert.False(defaults.IsMaximized);
        Assert.Equal("grid", defaults.DisplayMode); // v1.3/CR-6
        Assert.Null(defaults.LastViewId);
        Assert.Null(defaults.LastCollectionId);     // v1.3/CR-5
    }
}
