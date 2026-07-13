using System.Text.RegularExpressions;
using ViewPrism2.Core.Services;
using ViewPrism2.Infrastructure.I18n;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// ECO-079(CP-I18N-010 拡張): 画像タブ・作業タブの多言語対応漏れの再発防止 lint(検査の谷間是正)。
/// 両 View の XAML に、文言バインディングでない直書き日本語(K-AVALONIA「XAML 直書き文字列禁止」違反=
/// REQ-051「言語変更は UI 全体へ反映」の破れ)が残っていないことを検査する。
/// これは ECO-079 のプローブ(是正前は ImageTabView 88 件 / WorkTabView 80 件で不合格)であり、
/// 同型逸脱の恒久ガードでもある。他サーフェスは全て Loc[] 配線済みのため対象外。
/// </summary>
[Trait("cp", "CP-I18N-010")]
public sealed class CpI18n010XamlLintTests
{
    private static readonly string[] TextAttrs =
        { "Text", "Content", "Watermark", "Header", "ToolTip.Tip", "PlaceholderText" };

    private static readonly Regex Japanese = new("[぀-ヿ一-鿿]");

    private static string RepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, "ViewPrism2.sln")))
            {
                return d.FullName;
            }
        }

        throw new DirectoryNotFoundException("ViewPrism2.sln が出力パスから見つからない");
    }

    private static List<string> HardcodedJapanese(string axamlPath)
    {
        var text = File.ReadAllText(axamlPath);
        // コメントブロックを除去(<!-- ... -->・複数行対応)して本文だけを検査する
        text = Regex.Replace(text, "<!--.*?-->", "", RegexOptions.Singleline);

        var findings = new List<string>();
        foreach (var attr in TextAttrs)
        {
            foreach (Match m in Regex.Matches(text, attr + "=\"([^\"]*)\""))
            {
                var value = m.Groups[1].Value;
                if (value.StartsWith('{'))
                {
                    continue; // {Binding ...} は配線済み
                }

                if (Japanese.IsMatch(value))
                {
                    findings.Add($"{attr}=\"{value}\"");
                }
            }
        }

        return findings;
    }

    [Theory]
    [InlineData("src/ViewPrism2.App/Views/ImageTabView.axaml")]
    [InlineData("src/ViewPrism2.App/Views/WorkTabView.axaml")]
    public void タブViewに直書き日本語文言が残っていない(string relativePath)
    {
        var findings = HardcodedJapanese(Path.Combine(RepoRoot(), relativePath));

        Assert.True(
            findings.Count == 0,
            $"{relativePath} に i18n 未配線の直書き日本語が {findings.Count} 件残存: "
            + string.Join(" | ", findings.Take(12)));
    }
}

/// <summary>
/// ECO-079: 画像タブ・作業タブが束ねた i18n キーが ja/en 両言語で解決し、言語切替で英語化することを固定する
/// (静的 lint と対で、キーの存在と切替の end-to-end を裏取りする)。
/// </summary>
[Trait("cp", "CP-I18N-010")]
public sealed class CpI18n010TabKeysTests
{
    private static LocalizationService Load()
        => new(I18nResourceLoader.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "i18n")), "ja");

    [Fact]
    public void 画像作業タブで束ねたキーがjaとenで解決し言語切替で英語化する()
    {
        var loc = Load();

        // ECO-079 で再利用/新設した代表キー(コレクション/ツールバー/モード/StringFormat 書式)
        (string Key, string Ja, string En)[] cases =
        {
            ("navigation.collections", "コレクション", "Collections"),
            ("navigation.workspaces", "作業スペース", "Workspaces"),
            ("toolbar.organize", "整理", "Organize"),
            ("toolbar.tagEdit", "タグ編集", "Tag Edit"),
            ("view.scanning", "スキャン中", "Scanning"),
            ("workspace.badgeDefault", "既定", "Default"),
            ("view.selectValueToApply", "値を選んで付与（{0}）", "Select a value to apply ({0})"),
            ("view.organizeTargetsBadge", "整理対象 {0}", "Organize targets {0}"),
            ("view.thresholdAtLeast", "{0} 以上", "{0} or more"),
        };

        foreach (var (key, ja, _) in cases)
        {
            Assert.Equal(ja, loc.T(key));
        }

        loc.SetLocale("en");
        foreach (var (key, _, en) in cases)
        {
            Assert.Equal(en, loc.T(key));
        }
    }
}
