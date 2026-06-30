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

    // ============ v2.0 追加(REQ-059 ビューア設定。M-SET-010 拡張) ============

    [Fact]
    public void V2_ビューア設定7項目のラウンドトリップ()
    {
        var store = new SettingsStore(_directory);
        var settings = new AppSettings
        {
            ViewerMode = "spread-right",
            ViewerResizeMode = "matchLargerHeight",
            ViewerAlignMode = "top",
            ViewerGapMode = "loose",
            ViewerCustomGapPx = 16,
            ViewerPageTurnMode = "singlePage",
            ViewerStartWithEmptyPage = true,
        };

        store.Save(settings);
        var loaded = new SettingsStore(_directory).Load();

        Assert.Equal("spread-right", loaded.ViewerMode);
        Assert.Equal("matchLargerHeight", loaded.ViewerResizeMode);
        Assert.Equal("top", loaded.ViewerAlignMode);
        Assert.Equal("loose", loaded.ViewerGapMode);
        Assert.Equal(16, loaded.ViewerCustomGapPx);
        Assert.Equal("singlePage", loaded.ViewerPageTurnMode);
        Assert.True(loaded.ViewerStartWithEmptyPage);
        Assert.Equal(settings, loaded);
    }

    [Fact]
    public void V2_customGapPx範囲外と列挙不正は項目単位で既定_FMEA018()
    {
        Directory.CreateDirectory(_directory);
        var store = new SettingsStore(_directory);
        // customGapPx=9999(範囲外)・viewerMode='xyz'(列挙外)・viewerCustomGapPx 型不正は別ケースで検証
        File.WriteAllText(store.SettingsFilePath, """
            {
              "locale": "ja",
              "viewerMode": "xyz",
              "viewerCustomGapPx": 9999,
              "viewerGapMode": "loose"
            }
            """);

        var loaded = store.Load(); // 例外なし・項目単位で既定へ

        Assert.Equal("normal", loaded.ViewerMode);       // 列挙外文字列 → 既定
        Assert.Equal(0, loaded.ViewerCustomGapPx);        // 範囲外 9999 → 既定 0
        Assert.Equal("loose", loaded.ViewerGapMode);      // 正常値は維持
    }

    [Fact]
    public void V2_型不正の保存値は項目単位で既定_ファイル全体は破損扱いにしない()
    {
        Directory.CreateDirectory(_directory);
        var store = new SettingsStore(_directory);
        // viewerCustomGapPx に文字列(型不正)・viewerMode に数値(型不正)・負値
        File.WriteAllText(store.SettingsFilePath, """
            {
              "locale": "en",
              "viewerCustomGapPx": "abc",
              "viewerMode": 123,
              "viewerStartWithEmptyPage": "nope"
            }
            """);

        var loaded = store.Load(); // 例外なし

        Assert.Equal("en", loaded.Locale);               // 他の正常項目は読める(全体破損扱いではない)
        Assert.Equal(0, loaded.ViewerCustomGapPx);        // 型不正 → 既定 0
        Assert.Equal("normal", loaded.ViewerMode);        // 型不正 → 既定
        Assert.False(loaded.ViewerStartWithEmptyPage);    // 型不正 → 既定 false
        Assert.False(File.Exists(store.BackupFilePath));  // .bak 退避は発生しない(項目単位の既定化)
    }

    [Fact]
    public void V2_負値のcustomGapPxは既定0()
    {
        Directory.CreateDirectory(_directory);
        var store = new SettingsStore(_directory);
        File.WriteAllText(store.SettingsFilePath, """
            { "viewerCustomGapPx": -5 }
            """);

        Assert.Equal(0, store.Load().ViewerCustomGapPx);
    }

    [Fact]
    public void V2_旧v13形式_Viewerキーなしは全て既定値で読める_前方互換()
    {
        Directory.CreateDirectory(_directory);
        var store = new SettingsStore(_directory);
        // v1.3 形式(Viewer* キーが一切ない)
        File.WriteAllText(store.SettingsFilePath, """
            {
              "locale": "en",
              "displayMode": "list",
              "lastViewId": "v1",
              "lastCollectionId": "c1"
            }
            """);

        var loaded = store.Load();

        Assert.Equal("en", loaded.Locale);
        Assert.Equal("list", loaded.DisplayMode);
        // ビューア設定は全て既定値
        Assert.Equal("normal", loaded.ViewerMode);
        Assert.Equal("noResize", loaded.ViewerResizeMode);
        Assert.Equal("middle", loaded.ViewerAlignMode);
        Assert.Equal("tight", loaded.ViewerGapMode);
        Assert.Equal(0, loaded.ViewerCustomGapPx);
        Assert.Equal("doublePage", loaded.ViewerPageTurnMode);
        Assert.False(loaded.ViewerStartWithEmptyPage);
    }

    [Fact]
    public void V2_ビューア設定既定値スキーマ()
    {
        var defaults = new AppSettings();
        Assert.Equal("normal", defaults.ViewerMode);
        Assert.Equal("noResize", defaults.ViewerResizeMode);
        Assert.Equal("middle", defaults.ViewerAlignMode);
        Assert.Equal("tight", defaults.ViewerGapMode);
        Assert.Equal(0, defaults.ViewerCustomGapPx);
        Assert.Equal("doublePage", defaults.ViewerPageTurnMode);
        Assert.False(defaults.ViewerStartWithEmptyPage);
    }

    // ============ モック改善追加(フィット・背景・スクロール横揃え)============

    [Fact]
    public void V3_ビューア改善3項目のラウンドトリップ()
    {
        var store = new SettingsStore(_directory);
        var settings = new AppSettings
        {
            ViewerFitMode = "width",
            ViewerBackground = "checker",
            ViewerScrollHAlign = "right",
        };

        store.Save(settings);
        var loaded = new SettingsStore(_directory).Load();

        Assert.Equal("width", loaded.ViewerFitMode);
        Assert.Equal("checker", loaded.ViewerBackground);
        Assert.Equal("right", loaded.ViewerScrollHAlign);
        Assert.Equal(settings, loaded);
    }

    [Fact]
    public void V3_改善3項目の既定値スキーマ()
    {
        var defaults = new AppSettings();
        Assert.Equal("fit", defaults.ViewerFitMode);
        Assert.Equal("dark", defaults.ViewerBackground);
        Assert.Equal("center", defaults.ViewerScrollHAlign);
    }

    [Fact]
    public void V3_改善3項目の列挙外と型不正は項目単位で既定()
    {
        Directory.CreateDirectory(_directory);
        var store = new SettingsStore(_directory);
        // fit=列挙外文字列 / background=数値(型不正) / scrollHAlign=正常値
        File.WriteAllText(store.SettingsFilePath, """
            {
              "locale": "ja",
              "viewerFitMode": "zoom",
              "viewerBackground": 7,
              "viewerScrollHAlign": "left"
            }
            """);

        var loaded = store.Load();

        Assert.Equal("fit", loaded.ViewerFitMode);          // 列挙外 → 既定 fit
        Assert.Equal("dark", loaded.ViewerBackground);      // 型不正 → 既定 dark
        Assert.Equal("left", loaded.ViewerScrollHAlign);    // 正常値は維持
        Assert.False(File.Exists(store.BackupFilePath));    // 項目単位の既定化(全体破損扱いにしない)
    }

    [Fact]
    public void V3_旧形式_改善キーなしは既定値で読める_前方互換()
    {
        Directory.CreateDirectory(_directory);
        var store = new SettingsStore(_directory);
        File.WriteAllText(store.SettingsFilePath, """
            { "locale": "en", "viewerMode": "scroll" }
            """);

        var loaded = store.Load();

        Assert.Equal("scroll", loaded.ViewerMode);
        Assert.Equal("fit", loaded.ViewerFitMode);
        Assert.Equal("dark", loaded.ViewerBackground);
        Assert.Equal("center", loaded.ViewerScrollHAlign);
    }
}
