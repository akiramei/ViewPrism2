using ViewPrism2.Core.Services;
using ViewPrism2.Infrastructure.I18n;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-I18N-010(Run 3 追加分): 翻訳資産の統合(M-I18N-011)。
/// Assets/i18n/{ja,en}.json が読み込め、ja/en が同一キー集合で、V1 新規キーが両言語に存在する。
/// 解決器のフォールバック規則は Run 1 の CpI18n010Tests で検査済み。
/// </summary>
[Trait("cp", "CP-I18N-010")]
public sealed class CpI18n010AssetTests
{
    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> LoadAssets()
        => I18nResourceLoader.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "i18n"));

    [Fact]
    public void 資産が読み込めjaとenは同一キー集合()
    {
        var resources = LoadAssets();

        Assert.True(resources.ContainsKey("ja"));
        Assert.True(resources.ContainsKey("en"));
        // ECO-107: 原典変換キーの未配線残骸 640 件を棚卸し削除(1234→594)。下限は現有規模の番兵。
        Assert.True(resources["ja"].Count > 550);
        Assert.Equal(
            resources["ja"].Keys.Order(StringComparer.Ordinal),
            resources["en"].Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void V1新規キーが両言語に存在し解決できる()
    {
        var resources = LoadAssets();
        // ECO-107: Run-3 pin のうち 5 キー(detail.selectImagePrompt/detail.resolution/view.allImages/
        // nodeGraph.empty/toolbar.columns)は原典撤去(ECO-024 系)で消費者を失い棚卸し削除= pin から除去
        string[] newKeys =
        [
            "folder.management", "folder.scanSummary", "folder.deleteConfirm",
            "relink.confirmMessage", "relink.commit",
            "error.duplicateFolderPath", "error.scanInProgress",
        ];
        foreach (var key in newKeys)
        {
            Assert.True(resources["ja"].ContainsKey(key), $"ja に {key} が無い");
            Assert.True(resources["en"].ContainsKey(key), $"en に {key} が無い");
        }

        var localization = new LocalizationService(resources, "ja");
        Assert.Equal("同期フォルダ管理", localization.T("folder.management"));
        localization.SetLocale("en");
        Assert.Equal("Sync Folder Management", localization.T("folder.management"));

        // プレースホルダ補間(K-I18N)
        localization.SetLocale("ja");
        var summary = localization.T("folder.scanSummary", new Dictionary<string, string>
        {
            ["added"] = "1",
            ["updated"] = "2",
            ["missing"] = "3",
            ["pending"] = "4",
            ["skipped"] = "5",
        });
        Assert.Equal("スキャン完了: 追加 1 / 更新 2 / リンク切れ 3 / 保留 4 / スキップ 5", summary);
    }

    [Fact]
    public void 資産ディレクトリ欠落でも例外を投げない()
    {
        var resources = I18nResourceLoader.Load(Path.Combine(Path.GetTempPath(), "no-such-dir-" + Guid.NewGuid()));

        var localization = new LocalizationService(resources, "ja");
        Assert.Equal("some.key", localization.T("some.key")); // キー文字列フォールバック(REQ-050)
    }
}
