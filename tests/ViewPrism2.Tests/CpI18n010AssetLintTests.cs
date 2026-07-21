using System.Text.RegularExpressions;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// ECO-107(CP-I18N-010 拡張): i18n lint の検査 3 次元拡充。
/// ①重複キー(パーサ後勝ちで沈黙するドリフト= ECO-091 実績)
/// ②未使用キー(「翻訳した=対応済み」の錯覚マスク= ECO-095/099 実績・起票時粗 652/1234)
/// ③解決タイミング(Resolve 済み文字列の VM 状態保持= ECO-104/106 で 2 面連続実機顕在化)
/// 走査様式は CpI18n010XamlLintTests(ECO-080=全ファイル一般化・RepoRoot 方式)に倣う。
/// </summary>
[Trait("cp", "CP-I18N-010")]
public sealed class CpI18n010AssetLintTests
{
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

    private static string AssetPath(string lang)
        => Path.Combine(RepoRoot(), "src", "ViewPrism2.App", "Assets", "i18n", lang + ".json");

    private static IEnumerable<string> SrcFiles(params string[] extensions)
        => extensions.SelectMany(ext =>
            Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src"), ext, SearchOption.AllDirectories))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    // ---- 次元① 重複キー: JSON パーサは後勝ちで沈黙するため生テキストで数える ----

    internal static List<string> DuplicateKeys(string rawJson)
        => Regex.Matches(rawJson, "^\\s*\"([^\"]+)\"\\s*:", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .GroupBy(k => k, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

    [Fact]
    public void 重複キー検出器は後勝ちドリフトを検出する()
    {
        // ECO-091 実績(en.json tag.editor.basicInfo ×2・値違い後勝ち)の合成再現=検出能力の実証
        const string fixture = """
            {
              "a.key": "旧",
              "b.key": "x",
              "a.key": "新(後勝ちで有効・旧行が沈黙残存)"
            }
            """;
        Assert.Equal(["a.key"], DuplicateKeys(fixture));
    }

    [Fact]
    public void 翻訳資産に重複キーがない()
    {
        foreach (var lang in new[] { "ja", "en" })
        {
            var dups = DuplicateKeys(File.ReadAllText(AssetPath(lang)));
            Assert.True(dups.Count == 0, $"{lang}.json に重複キー {dups.Count} 件: {string.Join(", ", dups)}");
        }
    }

    // ---- 次元② 未使用キー: 全キーは src で消費される(完全一致)か動的構築 registry に載る ----

    /// <summary>
    /// 動的構築キーのプレフィックス registry(ECO-107)。T()/Loc[] の非リテラル引数の全数調査で確定。
    /// 追加時は構築サイトを根拠として記載する。
    /// </summary>
    private static readonly string[] DynamicKeyPrefixes =
    [
        "viewer.tagControl.action.", // ViewerViewModel: Loc?.T($"viewer.tagControl.action.{key}.name"/".desc")
    ];

    [Fact]
    public void 全キーはsrcで消費されるか動的構築registryに載る()
    {
        // 粗い方向= キー文字列の完全一致が src(.cs/.axaml)のどこかに現れること。
        // 長いキーのリテラルに短いキーが包含される偽陰性は許容(lint は許容側に倒す)。
        var raw = File.ReadAllText(AssetPath("ja"));
        var keys = Regex.Matches(raw, "^\\s*\"([^\"]+)\"\\s*:", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value).ToList();

        var blob = string.Concat(SrcFiles("*.cs", "*.axaml").Select(File.ReadAllText));
        var unused = keys
            .Where(k => !blob.Contains(k, StringComparison.Ordinal))
            .Where(k => !DynamicKeyPrefixes.Any(p => k.StartsWith(p, StringComparison.Ordinal)))
            .ToList();

        Assert.True(
            unused.Count == 0,
            $"未使用キー {unused.Count} 件(翻訳した=対応済みの錯覚マスク・ECO-095/099 実績)。" +
            $"消費するか削除するか、動的構築なら registry へ根拠つきで追加する: " +
            string.Join(", ", unused.Take(25)) + (unused.Count > 25 ? " …" : ""));
    }

    // ---- 次元③ 解決タイミング: Resolve 済み文字列の VM 状態保持を検出(表示時解決が正) ----

    /// <summary>
    /// 文単位で「プロパティ/フィールドへの T()/ErrorMessages.Resolve() 結果の代入」を検出する。
    /// 除外= ローカル宣言(var/string=一時使用)・「=>」を含む文(算出プロパティ/ラムダ=表示時解決の良性)。
    /// 限界= ヘルパ関数経由の間接解決は映らない(既知の限界として記録)。
    /// </summary>
    internal static List<string> ResolvedStoreFindings(string fileName, string source)
    {
        var findings = new List<string>();
        foreach (var chunk in source.Split(';'))
        {
            var stmt = Regex.Replace(chunk, "\\s+", " ").Trim();
            if (!stmt.Contains("_localization.T(", StringComparison.Ordinal)
                && !stmt.Contains("ErrorMessages.Resolve(", StringComparison.Ordinal))
            {
                continue;
            }

            if (stmt.Contains("=>", StringComparison.Ordinal))
            {
                continue; // 算出プロパティ/ラムダ= 表示時解決(良性パターン)
            }

            var m = Regex.Match(stmt, "(?:^|[ (])(?:(?<decl>var|string) )?(?<target>[A-Za-z_]\\w*(?:\\.\\w+)*) = [^=]");
            if (!m.Success || m.Groups["decl"].Success)
            {
                continue; // 代入なし(引数内の一時使用)or ローカル宣言
            }

            findings.Add($"{fileName}:{m.Groups["target"].Value}");
        }

        return findings.Distinct(StringComparer.Ordinal).ToList();
    }

    [Fact]
    public void 解決タイミング検出器はECO104と106の是正前イディオムを検出する()
    {
        // ECO-104(保存バー)/ECO-106(パレット)の是正前コード(git 履歴の実在例)を合成再現
        const string fixture = """
            SaveError = ErrorMessages.Resolve(_localization, result.Error);
            StatusMessage = _localization.T("error.tagInUnsavedEdit");
            var message = _localization.T("tag.deleteTagConfirmation");
            public string? SaveError => SaveErrorCode is { } code ? ErrorMessages.Resolve(_localization, code) : null;
            """;
        var findings = ResolvedStoreFindings("Fixture.cs", fixture);
        Assert.Contains("Fixture.cs:SaveError", findings);      // ECO-104 是正前= 検出
        Assert.Contains("Fixture.cs:StatusMessage", findings);  // ECO-106 是正前= 検出
        Assert.Equal(2, findings.Count);                        // var 一時使用・算出プロパティ(=>)は非検出
    }

    /// <summary>
    /// 現存サイトの層別 allowlist(ECO-106 §3 の分類が初期値)。エントリは「ファイル名:代入先」+根拠。
    /// 新規サイトはここに現れず lint が fail する — 表示時解決(キー/コード保持)へ寄せるか、
    /// 根拠を書いてここへ追加するか、常駐クラスなら分離起票(R3)する。
    /// </summary>
    private static readonly Dictionary<string, string> ResolvedStoreAllowlist = new(StringComparer.Ordinal)
    {
        // ---- (a) モーダル/ダイアログスコープ: 設定(言語切替 UI)と同時に開けず、閉→再開で再解決される ----
        ["CollectionExportViewModel.cs:CollectionSummary"] = "エクスポートダイアログ内(モーダル)",
        ["CollectionExportViewModel.cs:StatusMessage"] = "エクスポートダイアログ内(モーダル)",
        ["CollectionImportViewModel.cs:TagCreatedChip"] = "インポートウィザード内(モーダル)",
        ["CollectionImportViewModel.cs:TagMappedChip"] = "インポートウィザード内(モーダル)",
        ["CollectionImportViewModel.cs:TagConflictChip"] = "インポートウィザード内(モーダル)",
        ["FolderManagementViewModel.cs:StatusMessage"] = "フォルダ管理ダイアログ内(モーダル)",
        ["FolderManagementViewModel.cs:row.RowMessage"] = "フォルダ管理ダイアログ内(モーダル)",
        ["NodeConditionDialogViewModel.cs:ErrorMessage"] = "条件ダイアログ内(モーダル)",
        ["NumericValueDialogViewModel.cs:ErrorMessage"] = "数値入力ダイアログ内(モーダル)",
        ["RelinkViewModel.cs:StatusMessage"] = "リリンクダイアログ内(モーダル)",
        ["RepairViewModel.cs:StatusMessage"] = "修復ダイアログ内(モーダル)",
        ["SnapshotViewModel.cs:CreatingCountsText"] = "スナップショットウィンドウ内(モーダル)",
        ["SnapshotViewModel.cs:StatusMessage"] = "スナップショットウィンドウ内(モーダル)",
        ["TagEditorViewModel.cs:ErrorMessage"] = "タグ編集ダイアログ内(モーダル)",
        ["ViewEditDialogViewModel.cs:ErrorMessage"] = "ビュー編集ダイアログ内(モーダル)",
        ["ScanSummaryViewModel.cs:StatusMessage"] = "スキャン結果確認ダイアログ内(モーダル・ECO-130。設定=言語切替導線へ同時到達不能)",
        ["ScanSummaryViewModel.cs:Outcome"] = "スキャン結果確認の閉窓時一回性の受け渡し(ECO-130。Error は呼び出し元 row.RowMessage=既層別サイトが即時表示)",

        // ---- (b) 開くたび再構築される射影(表示状態を跨いで保持しない) ----
        ["ColumnPickerViewModel.cs:Key"] = "列ピッカー行の射影(開くたび Rebuild・object initializer 検出)",

        // ---- (c) 精査済み(ECO-108 で悉皆処置): CultureChanged の再計算経路が再解決を担保 ----
        //      (キー保持化した _catalogError/_contentError/_scanNotice/WsDeleteMessage はサイト自体が消滅)
        ["ImageTabViewModel.cs:ColumnSortLabel"] = "精査済み(ECO-108): CultureChanged→Recompute で再解決",
        ["ImageTabViewModel.cs:ChipHintLabel"] = "精査済み(ECO-108): CultureChanged→Recompute で再解決",
        ["ImageTabViewModel.cs:CountLabel"] = "精査済み(ECO-108): CultureChanged→Recompute で再解決",
        ["ImageTabViewModel.cs:CurrentNote"] = "精査済み(ECO-108): CultureChanged→Recompute(BuildContextPanels 内包)で再解決",
        ["ImageTabViewModel.cs:NoCurrentLabel"] = "精査済み(ECO-108): CultureChanged→Recompute(BuildContextPanels 内包)で再解決",
        ["ImageTabViewModel.cs:row.NumCurrent"] = "精査済み(ECO-108): CultureChanged→Recompute(BuildAddGroups 内包)で再解決",
        ["WorkTabViewModel.cs:CountLabel"] = "精査済み(ECO-108): CultureChanged→Recompute で再解決",
        ["WorkTabViewModel.cs:ChipHintLabel"] = "精査済み(ECO-108): CultureChanged→Recompute で再解決",
        ["WorkTabViewModel.cs:CurrentNote"] = "精査済み(ECO-108): CultureChanged→Recompute(BuildContextPanels 内包)で再解決",
        ["WorkTabViewModel.cs:NoCurrentLabel"] = "精査済み(ECO-108): CultureChanged→Recompute(BuildContextPanels 内包)で再解決",
        ["WorkTabViewModel.cs:row.NumCurrent"] = "精査済み(ECO-108): CultureChanged→Recompute(BuildAddGroups 内包)で再解決",
        ["SettingsViewModel.cs:SnapshotSummary"] = "精査済み(ECO-108): CultureChanged→RefreshSnapshotSummary 再実行=追随を実測",

        // ---- (d) 既知限界(ECO-108 精査・根拠つき残置) ----
        ["WorkTabViewModel.cs:_undoNote"] = "精査済み(ECO-108): Core result.Message 依存の一時ノート(Core 文言の i18n 化は別スコープ・次操作でクリア)",
        ["ImageTabOrganizeViewModel.cs:_undoNote"] = "精査済み(ECO-108): 同上(WorkTab._undoNote と同型)",
    };

    [Fact]
    public void 解決済み文字列のVM状態保持は層別済みサイトに限る()
    {
        var findings = SrcFiles("*.cs")
            .SelectMany(p => ResolvedStoreFindings(Path.GetFileName(p), File.ReadAllText(p)))
            .ToList();

        var unexpected = findings.Where(f => !ResolvedStoreAllowlist.ContainsKey(f)).ToList();
        var stale = ResolvedStoreAllowlist.Keys.Except(findings, StringComparer.Ordinal).ToList();

        Assert.True(
            unexpected.Count == 0,
            $"未層別の解決済み文字列保持 {unexpected.Count} 件(常駐メッセージは言語切替に追随しない= " +
            $"ECO-104/106 実績)。表示時解決へ寄せるか、根拠つきで allowlist へ: {string.Join(", ", unexpected)}");
        Assert.True(
            stale.Count == 0,
            $"allowlist の死亡エントリ {stale.Count} 件(サイト消滅=リストから除去): {string.Join(", ", stale)}");
    }
}
