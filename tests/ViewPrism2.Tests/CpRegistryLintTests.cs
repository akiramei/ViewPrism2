using System.Text.RegularExpressions;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// ECO-122: UI 部品表(04_component_registry)適合 lint・第1トランシェ(CMP-006 PopoverMenu)。
/// 恒久運用(2026-07-20 maintainer 指示)= ①部品該当箇所は部品契約へ委譲 ②画面側の個別プロパティで
/// Standard の外観を再現/上書きしない(ECO-119 型)。本 lint はその機械化:
/// - 検査A(生コントロール検出): Popup 直下の面 Border が popupMenu クロームを使っているか。
/// - 検査B(個別上書き検出): popupMenu 要素にクローム属性(Background/BorderBrush/BorderThickness/
///   CornerRadius/Padding/BoxShadow)を局所指定していないか。
/// allowlist は層別・全エントリ根拠つき(ECO-107 様式)+死亡エントリ検出(サイト消滅で fail)。
///
/// 検出限界の宣言(ECO-107 教訓1= 限界宣言には掃射手段を紐づける):
/// - Style 層からの上書き(Selector=Border.popupMenu への Setter)は未走査= 現状 Components.axaml の
///   定義 1 箇所のみ(grep 済み)。増えたら本 lint へ Style 走査を追加する。
/// - Popup 以外の浮遊面(Flyout/ToolTip)・メニュー内部の色/字形は本トランシェ対象外
///   (色トークンは視覚 probe= Gf* が RegistryContract 参照で pin)。
/// - 幅の契約値一致は実レイアウト側の GfPopoverMenuInstanceWidthTests が担う(lint は構造のみ)。
/// - 自己閉じ `&lt;Popup ... /&gt;`(子なし)は検査A の走査対象外(現状 0 件。子を持たない Popup は
///   面 Border 自体が無く別の欠陥として顕在化する)。
/// </summary>
[Trait("cp", "CP-REGISTRY-LINT-122")]
public sealed class CpRegistryLintTests
{
    private static string RepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, "ViewPrism2.sln"))) return d.FullName;
        }
        throw new DirectoryNotFoundException("ViewPrism2.sln が出力パスから見つからない");
    }

    /// <summary>検出キーはファイル名のみ(パス無し)。同名 axaml が別ディレクトリに現れたら要拡張。</summary>
    private static IEnumerable<(string FileName, string Text)> AxamlFiles()
    {
        var root = RepoRoot();
        return Directory.EnumerateFiles(Path.Combine(root, "src"), "*.axaml", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Select(p => (Path.GetFileName(p), File.ReadAllText(p)));
    }

    // ---- 検査A: Popup 直下の面 Border は popupMenu クロームを使う ----

    /// <summary>
    /// 検出キー= "ファイル名:最初の Border の Classes 値(無指定は NONE)"。
    /// 行番号キーは編集で滑るため使わない(i18n lint の「ファイル:代入先」様式)。
    /// </summary>
    internal static List<string> FindPopupsWithoutMenuChrome(string fileName, string text)
    {
        var findings = new List<string>();
        var popupBlock = new Regex(@"<Popup\b.*?</Popup>", RegexOptions.Singleline);
        var borderTag = new Regex(@"<Border\b[^>]*>", RegexOptions.Singleline);
        foreach (Match popup in popupBlock.Matches(text))
        {
            var border = borderTag.Match(popup.Value);
            if (!border.Success)
            {
                findings.Add($"{fileName}:NONE");
                continue;
            }
            var classes = Regex.Match(border.Value, @"Classes=""([^""]*)""");
            var cls = classes.Success ? classes.Groups[1].Value : "NONE";
            if (!cls.Split(' ').Contains("popupMenu")) findings.Add($"{fileName}:{cls}");
        }
        return findings;
    }

    /// <summary>
    /// 検査A allowlist。エントリ= 検出キー+根拠。新規サイトはここに現れず fail する —
    /// popupMenu クロームへ寄せるか、根拠(裁定/記帳)を書いてここへ追加するか、分離起票(R3)。
    /// </summary>
    private static readonly Dictionary<string, string> NonMenuChromeAllowlist = new(StringComparer.Ordinal)
    {
        // chip overflow ポップオーバー(IMG-023A 面)。部品表は CMP-006 インスタンスに列挙するが実装は
        // 専用クローム chipPopCard で、popupMenu クロームとの写像一致は未検証(51-cheat-log 2026-07-20)。
        // 契約適合と判定するかバリアント記帳とするかは CAD 側裁定待ち= 判定までの暫定許容。
        ["LabeledChipStrip.axaml:chipPopCard"] = "chip overflow=専用クローム(写像未検証・CAD 側判定待ち)",
    };

    [Fact]
    public void Popupの面BorderはpopupMenuクロームを使う()
    {
        var findings = AxamlFiles().SelectMany(f => FindPopupsWithoutMenuChrome(f.FileName, f.Text)).ToList();
        var unexpected = findings.Where(f => !NonMenuChromeAllowlist.ContainsKey(f)).ToList();
        var stale = NonMenuChromeAllowlist.Keys.Except(findings, StringComparer.Ordinal).ToList();
        Assert.True(unexpected.Count == 0,
            $"popupMenu クロームを使わない Popup 面 {unexpected.Count} 件(部品表 CMP-006 委譲= 恒久運用柱1)。"
            + $"クロームへ寄せるか、根拠つきで allowlist へ: {string.Join(", ", unexpected)}");
        Assert.True(stale.Count == 0,
            $"allowlist の死亡エントリ {stale.Count} 件(サイト消滅=リストから除去): {string.Join(", ", stale)}");
    }

    // ---- 検査B: popupMenu 要素へのクローム属性の局所上書き ----

    private static readonly string[] ChromeAttrs =
        ["Background", "BorderBrush", "BorderThickness", "CornerRadius", "Padding", "BoxShadow"];

    /// <summary>検出キー= "ファイル名:Width=値:上書き属性リスト"(Width がインスタンス識別子)。</summary>
    internal static List<string> FindChromeOverrides(string fileName, string text)
    {
        var findings = new List<string>();
        var menuTag = new Regex(@"<Border\b[^>]*Classes=""[^""]*\bpopupMenu\b[^""]*""[^>]*>", RegexOptions.Singleline);
        foreach (Match m in menuTag.Matches(text))
        {
            var overridden = ChromeAttrs.Where(a => Regex.IsMatch(m.Value, $@"\b{a}\s*=")).ToList();
            if (overridden.Count == 0) continue;
            var width = Regex.Match(m.Value, @"Width=""([^""]*)""");
            findings.Add($"{fileName}:Width={(width.Success ? width.Groups[1].Value : "?")}:{string.Join("|", overridden)}");
        }
        return findings;
    }

    /// <summary>検査B allowlist(様式は検査A と同じ)。</summary>
    private static readonly Dictionary<string, string> ChromeOverrideAllowlist = new(StringComparer.Ordinal)
    {
        // 並び替えメニュー(両タブ同型複製)= radius 13 はインスタンス契約差として REG-C3 裁定で確定
        // (VC-FL-1① mock 契約・「非対称ではない」)。Padding 0/BoxShadow はその mock 実測の随伴
        // (ECO-109 golden 承認面)。裁定済みの上書きのみ許容= 新規の上書きはここに現れず fail する。
        ["ImageTabView.axaml:Width=252:CornerRadius|Padding|BoxShadow"] = "並び替え=radius13(REG-C3 裁定・VC-FL-1①)",
        ["WorkTabView.axaml:Width=252:CornerRadius|Padding|BoxShadow"] = "並び替え=radius13(REG-C3 裁定・同型複製の同値)",
    };

    [Fact]
    public void popupMenu要素にクローム属性を局所上書きしない()
    {
        var findings = AxamlFiles().SelectMany(f => FindChromeOverrides(f.FileName, f.Text)).ToList();
        var unexpected = findings.Where(f => !ChromeOverrideAllowlist.ContainsKey(f)).ToList();
        var stale = ChromeOverrideAllowlist.Keys.Except(findings, StringComparer.Ordinal).ToList();
        Assert.True(unexpected.Count == 0,
            $"popupMenu クローム属性の局所上書き {unexpected.Count} 件(Standard 外観の個別再現/上書き禁止= "
            + $"恒久運用柱2・ECO-119 型)。契約どおりへ戻すか、裁定根拠つきで allowlist へ: {string.Join(", ", unexpected)}");
        Assert.True(stale.Count == 0,
            $"allowlist の死亡エントリ {stale.Count} 件(サイト消滅=リストから除去): {string.Join(", ", stale)}");
    }

    // ---- 陽性対照(ECO-053/078/107/120 の型= 検査を追加する変更は陽性対照を同梱)----

    [Fact]
    public void 陽性対照_クロームなしPopupを検出できる()
    {
        const string bad = """<Popup IsOpen="True"><Border Classes="myCard"><TextBlock /></Border></Popup>""";
        var findings = FindPopupsWithoutMenuChrome("Synthetic.axaml", bad);
        Assert.Equal(["Synthetic.axaml:myCard"], findings);
        const string good = """<Popup IsOpen="True"><Border Classes="popupMenu" Width="240"><TextBlock /></Border></Popup>""";
        Assert.Empty(FindPopupsWithoutMenuChrome("Synthetic.axaml", good));
        // R8 所見6: NONE 分岐 2 種も対照(Border 自体なし/Classes 無指定)
        const string noBorder = """<Popup IsOpen="True"><StackPanel /></Popup>""";
        Assert.Equal(["Synthetic.axaml:NONE"], FindPopupsWithoutMenuChrome("Synthetic.axaml", noBorder));
        const string noClasses = """<Popup IsOpen="True"><Border Width="240"><TextBlock /></Border></Popup>""";
        Assert.Equal(["Synthetic.axaml:NONE"], FindPopupsWithoutMenuChrome("Synthetic.axaml", noClasses));
    }

    [Fact]
    public void 陽性対照_クローム属性の局所上書きを検出できる()
    {
        const string bad = """<Border Classes="popupMenu" Width="240" CornerRadius="4" Padding="2">""";
        var findings = FindChromeOverrides("Synthetic.axaml", bad);
        Assert.Equal(["Synthetic.axaml:Width=240:CornerRadius|Padding"], findings);
        const string good = """<Border Classes="popupMenu" Width="240" MaxHeight="360">""";
        Assert.Empty(FindChromeOverrides("Synthetic.axaml", good));
    }
}
