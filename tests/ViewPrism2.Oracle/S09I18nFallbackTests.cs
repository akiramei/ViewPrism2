using ViewPrism2.Core.Services;
using Xunit;

namespace ViewPrism2.Oracle;

/// <summary>
/// S-09: i18n フォールバック連鎖(spec §2.7 REQ-050、EQ-001)。
/// locale=en で en 欠落キー(ja のみ存在)→ ja 文言、両方欠落キー → キー文字列。例外なし。
/// </summary>
[Trait("oracle", "S-09")]
public sealed class S09I18nFallbackTests
{
    private static LocalizationService NewService()
    {
        var resources = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["ja"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["common.ok"] = "OK(ja)",
                ["only.ja"] = "ja のみ文言",
            },
            ["en"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["common.ok"] = "OK(en)",
            },
        };
        return new LocalizationService(resources, initialLocale: "en");
    }

    [Fact]
    public void en欠落キーはjaの文言へフォールバックする()
    {
        var service = NewService();
        Assert.Equal("ja のみ文言", service.T("only.ja"));
    }

    [Fact]
    public void 両方欠落キーはキー文字列を返し例外を投げない()
    {
        var service = NewService();
        Assert.Equal("missing.everywhere", service.T("missing.everywhere"));
    }

    [Fact]
    public void en存在キーはenの文言を返す()
    {
        // フォールバック連鎖の前提確認(要求ロケール優先)
        var service = NewService();
        Assert.Equal("OK(en)", service.T("common.ok"));
    }
}
