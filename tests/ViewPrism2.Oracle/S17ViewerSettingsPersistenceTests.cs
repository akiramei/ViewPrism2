using ViewPrism2.Core.Models;
using ViewPrism2.Infrastructure.Settings;
using Xunit;

namespace ViewPrism2.Oracle;

/// <summary>
/// S-17: ビューア設定の永続化と破損耐性(spec §2.9 REQ-059・§2.7 REQ-052、EQ-001)。
/// 全 7 項目を非既定値で保存→再読込で一致。次に customGapPx=9999・mode='xyz' に書き換えて再読込すると
/// 項目単位で既定へ(customGapPx=0・mode='normal')フォールバックし他項目は保持・例外なし。
/// v1.3 形式(Viewer* キーなし)も全既定値で読める。
/// </summary>
[Trait("oracle", "S-17")]
public sealed class S17ViewerSettingsPersistenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "ViewPrism2.Oracle", "s17-" + Guid.NewGuid().ToString("D"));

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
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void 全7項目の非既定値がラウンドトリップする()
    {
        var store = new SettingsStore(_directory);
        store.Save(new AppSettings
        {
            ViewerMode = "spread-right",
            ViewerResizeMode = "matchLargerHeight",
            ViewerAlignMode = "top",
            ViewerGapMode = "loose",
            ViewerCustomGapPx = 16,
            ViewerPageTurnMode = "singlePage",
            ViewerStartWithEmptyPage = true,
        });

        var loaded = new SettingsStore(_directory).Load();

        Assert.Equal("spread-right", loaded.ViewerMode);
        Assert.Equal("matchLargerHeight", loaded.ViewerResizeMode);
        Assert.Equal("top", loaded.ViewerAlignMode);
        Assert.Equal("loose", loaded.ViewerGapMode);
        Assert.Equal(16, loaded.ViewerCustomGapPx);
        Assert.Equal("singlePage", loaded.ViewerPageTurnMode);
        Assert.True(loaded.ViewerStartWithEmptyPage);
    }

    [Fact]
    public void 範囲外と列挙外は項目単位で既定化し他項目は保持する()
    {
        // 妥当な JSON だが viewerMode が列挙外・viewerCustomGapPx が範囲外。
        // 他項目(locale・viewerResizeMode)は妥当 → 保持されること。
        const string json =
            "{ \"locale\": \"en\", \"viewerMode\": \"xyz\", \"viewerCustomGapPx\": 9999, " +
            "\"viewerResizeMode\": \"matchLargerHeight\" }";
        Directory.CreateDirectory(_directory);
        var store = new SettingsStore(_directory);
        File.WriteAllText(store.SettingsFilePath, json);

        var loaded = store.Load(); // 例外なし

        Assert.Equal("normal", loaded.ViewerMode);        // 列挙外 → 既定
        Assert.Equal(0, loaded.ViewerCustomGapPx);        // 範囲外 → 既定 0
        Assert.Equal("matchLargerHeight", loaded.ViewerResizeMode); // 妥当 → 保持
        Assert.Equal("en", loaded.Locale);                // 妥当 → 保持
    }

    [Fact]
    public void v1_3形式_Viewerキーなし_は全ビューア設定が既定値で読める()
    {
        const string json = "{ \"locale\": \"ja\", \"displayMode\": \"list\" }";
        Directory.CreateDirectory(_directory);
        var store = new SettingsStore(_directory);
        File.WriteAllText(store.SettingsFilePath, json);

        var loaded = store.Load();

        Assert.Equal("normal", loaded.ViewerMode);
        Assert.Equal("noResize", loaded.ViewerResizeMode);
        Assert.Equal("middle", loaded.ViewerAlignMode);
        Assert.Equal("tight", loaded.ViewerGapMode);
        Assert.Equal(0, loaded.ViewerCustomGapPx);
        Assert.Equal("doublePage", loaded.ViewerPageTurnMode);
        Assert.False(loaded.ViewerStartWithEmptyPage);
        Assert.Equal("list", loaded.DisplayMode); // v1.3 項目は従来どおり
    }
}
