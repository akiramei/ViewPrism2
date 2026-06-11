using ViewPrism2.Core.Services;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>CP-I18N-010: i18n 解決器のフォールバック・補間が REQ-050 と一致する(OC-8)。</summary>
[Trait("cp", "CP-I18N-010")]
public sealed class CpI18n010Tests
{
    private static LocalizationService Create()
    {
        // テスト用ミニ辞書(リソースは辞書注入 — 資産ファイル統合は後続 Run)
        var ja = new Dictionary<string, string>
        {
            ["common.ok"] = "OK(ja)",
            ["common.onlyJa"] = "ja のみ",
            ["greet.hello"] = "こんにちは {name}",
        };
        var en = new Dictionary<string, string>
        {
            ["common.ok"] = "OK(en)",
            ["greet.hello"] = "Hello {name}",
        };
        var resources = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["ja"] = ja,
            ["en"] = en,
        };
        return new LocalizationService(resources);
    }

    [Fact]
    public void 既定ロケールはja()
    {
        var service = Create();
        Assert.Equal("ja", service.CurrentLocale);
        Assert.Equal("OK(ja)", service.T("common.ok"));
    }

    [Fact]
    public void enに存在するキーはenの文言()
    {
        var service = Create();
        service.SetLocale("en");
        Assert.Equal("OK(en)", service.T("common.ok"));
    }

    [Fact]
    public void enに欠落するキーはjaへフォールバック()
    {
        var service = Create();
        service.SetLocale("en");
        Assert.Equal("ja のみ", service.T("common.onlyJa"));
    }

    [Fact]
    public void 両方に欠落するキーはキー文字列を返す_例外なし()
    {
        var service = Create();
        service.SetLocale("en");
        Assert.Equal("missing.key", service.T("missing.key"));
    }

    [Fact]
    public void 補間_プレースホルダを引数辞書から置換()
    {
        var service = Create();
        service.SetLocale("en");
        var text = service.T("greet.hello", new Dictionary<string, string> { ["name"] = "Tom" });
        Assert.Equal("Hello Tom", text);
    }

    [Fact]
    public void 補間_引数欠落のプレースホルダはそのまま残る()
    {
        var service = Create();
        service.SetLocale("en");
        Assert.Equal("Hello {name}", service.T("greet.hello"));
        Assert.Equal(
            "Hello {name}",
            service.T("greet.hello", new Dictionary<string, string> { ["other"] = "x" }));
    }

    [Fact]
    public void SetLocaleでCultureChangedが1回発火する()
    {
        var service = Create();
        var fired = 0;
        service.CultureChanged += (_, _) => fired++;

        service.SetLocale("en");

        Assert.Equal(1, fired);
        Assert.Equal("en", service.CurrentLocale);
    }
}
