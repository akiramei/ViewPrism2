using System.Text.RegularExpressions;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// ECO-117: ScrollViewer.Padding 禁止則の恒久 lint(K-AVALONIA)。
/// Avalonia の ScrollViewer.Padding は Viewport から引かれず Extent とも一致しないため、
/// 内容末尾が Padding.Top のぶんスクロール到達不能になる(ECO-116 実測法則)。
/// 余白は内容側の Margin で持つ(先行注意書き= ViewerWindow.axaml / ViewEditDialog.axaml)。
/// 背景: 法則発見(2026-07-01/02)時に注意書きは書かれたが既存サイトの遡及掃射がなく、
/// 先行製造済みの違反 8 サイトが潜伏し続けた(ECO-117 R8 で時系列を訂正)。
/// 禁止則は遡及掃射+lint を紐づけて初めて規約になる。allowlist なしのゼロ基線。
/// </summary>
[Trait("cp", "CP-AXAML-LINT-117")]
public sealed class CpAxamlLintScrollViewerPaddingTests
{
    private static string RepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, "ViewPrism2.sln"))) return d.FullName;
        }
        throw new DirectoryNotFoundException("ViewPrism2.sln が出力パスから見つからない");
    }

    private static IEnumerable<string> AxamlFiles() =>
        Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src"), "*.axaml", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    [Fact]
    public void ScrollViewer要素にPadding属性を書かない()
    {
        // 開きタグは複数行に割れうるため Singleline で <ScrollViewer ... > を丸ごと取り、
        // その属性部に Padding= が現れたら違反(行番号つきで全列挙)。
        var openTag = new Regex(@"<ScrollViewer\b[^>]*?>", RegexOptions.Singleline);
        var violations = new List<string>();
        foreach (var path in AxamlFiles())
        {
            var text = File.ReadAllText(path);
            foreach (Match m in openTag.Matches(text))
            {
                if (!Regex.IsMatch(m.Value, @"\bPadding\s*=")) continue;
                var line = text[..m.Index].Count(c => c == '\n') + 1;
                violations.Add($"{Path.GetRelativePath(RepoRoot(), path)}:{line}: {Collapse(m.Value)}");
            }
        }
        Assert.True(violations.Count == 0,
            "ScrollViewer.Padding は禁止(内容末尾が Padding.Top のぶん到達不能= ECO-116 実測)。"
            + "余白は内容側の Margin へ:\n" + string.Join("\n", violations));
    }

    [Fact]
    public void ScrollViewerセレクタのStyleでPaddingを設定しない()
    {
        // Style 層からの混入も閉じる: Selector に ScrollViewer を含む Style ブロック内の
        // <Setter Property="Padding" を違反とする(起票時 0 件=白の維持)。
        var styleBlock = new Regex(@"<Style\s+Selector=""[^""]*ScrollViewer[^""]*"".*?</Style>", RegexOptions.Singleline);
        var violations = new List<string>();
        foreach (var path in AxamlFiles())
        {
            var text = File.ReadAllText(path);
            foreach (Match m in styleBlock.Matches(text))
            {
                if (!Regex.IsMatch(m.Value, @"<Setter\s+Property=""Padding""")) continue;
                var line = text[..m.Index].Count(c => c == '\n') + 1;
                violations.Add($"{Path.GetRelativePath(RepoRoot(), path)}:{line}: {Collapse(m.Value[..Math.Min(120, m.Value.Length)])}");
            }
        }
        Assert.True(violations.Count == 0,
            "ScrollViewer セレクタの Style で Padding を設定しない(ECO-116/117 禁止則):\n"
            + string.Join("\n", violations));
    }

    private static string Collapse(string s) => Regex.Replace(s, @"\s+", " ").Trim();
}
