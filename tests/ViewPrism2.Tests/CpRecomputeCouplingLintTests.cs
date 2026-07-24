using System.Text.RegularExpressions;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// ECO-125: Recompute 結合点の台帳 lint。26 万件経路の欠陥 7 例(064/062/113/114/115/118/124)は
/// 全て「Recompute(母集合パイプライン)への不要/過剰結合」が事後の症状観測で見つかったもの。
/// 経路種別(モード/パネル等)軸の棚卸しは軸の谷間を残す(ECO-124 教訓1)ため、本 lint は
/// **結合点そのもの**を全数 pin する: 両タブ live VM の Recompute() 呼び出しサイトを
/// 「ファイル:メソッド→件数」で固定し、**新規サイトは fail して分類(A=正当/B=不要結合/
/// C=条件付き)を強制する**。allowlist は全エントリ分類根拠つき(ECO-107 様式)+死亡エントリ検出。
///
/// 検出限界の宣言:
/// - 対象は直呼び `Recompute()` のみ。Organize 子 VM へ注入された `_recompute` デリゲート経由は
///   4 サイト(R8 所見6 で実測): マージ実行/Undo 成功=データ変化 A・**RunSearchAsync 成功末尾=
///   母集合不変の全面 Recompute(C 相当・cheat-log 2026-07-21 予告記帳=症状観測で分離起票)**・
///   Undo 失敗=軽微。デリゲート先の付け替えは配線サイト(ImageTabViewModel の注入元)の diff で
///   可視= 本 lint の件数 pin 対象外(限界)。
/// - 撮影ハーネス ImageTabSeedViewModel は対象外(性能契約適用外・cheat-log 2026-07-21 記帳済み)。
/// - メソッド同定は宣言行の正規表現(プロパティ/フィールド初期化子は非マッチ)。件数一致のみを
///   pin し、メソッド内の呼び出し位置は見ない(位置の妥当性は分類根拠と probe が担う)。
/// </summary>
[Trait("cp", "CP-RECOMPUTE-LINT-125")]
public sealed class CpRecomputeCouplingLintTests
{
    private static string RepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, "ViewPrism2.sln"))) return d.FullName;
        }
        throw new DirectoryNotFoundException("ViewPrism2.sln が出力パスから見つからない");
    }

    private static readonly string[] TargetFiles =
    [
        @"src\ViewPrism2.App\ViewModels\ImageTabViewModel.cs",
        @"src\ViewPrism2.App\ViewModels\WorkTabViewModel.cs",
    ];

    /// <summary>ファイル内の Recompute() 直呼びを「メソッド名→件数」へ集計する(コメント除外)。</summary>
    internal static Dictionary<string, int> CountCallSites(string text)
    {
        var methodDecl = new Regex(@"^\s*(?:public|private|internal|protected)[^=]*?\s(\w+)\s*\(");
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var current = "(top)";
        foreach (var raw in text.Split('\n'))
        {
            var line = raw;
            var comment = line.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0) line = line[..comment];
            var decl = methodDecl.Match(line);
            if (decl.Success) current = decl.Groups[1].Value;
            if (line.Contains("void Recompute()")) continue; // 定義自身
            var idx = 0;
            while ((idx = line.IndexOf("Recompute()", idx, StringComparison.Ordinal)) >= 0)
            {
                // 直呼びのみ(this.Recompute()/Recompute())。_recompute() 等の別識別子は前置文字で除外
                if (idx == 0 || !char.IsLetterOrDigit(line[idx - 1]) && line[idx - 1] != '_' && line[idx - 1] != '.')
                    counts[current] = counts.GetValueOrDefault(current) + 1;
                else if (idx > 0 && line[idx - 1] == '.')
                    counts[current] = counts.GetValueOrDefault(current) + 1;
                idx += "Recompute()".Length;
            }
        }
        return counts;
    }

    /// <summary>
    /// 結合点台帳(ECO-125 fix 時の全数分類= 2026-07-21)。キー= "ファイル名:メソッド"・値= (件数, 分類根拠)。
    /// 分類: A=正当(母集合再解決が意味論上必要)/A'=正当(規模前提つき・将来差分化候補の記帳あり)/
    /// C=条件付き正当(cheat-log 予告記帳済み・症状観測で分離起票)。B(不要結合)は ECO-125 で 3 サイト
    /// 是正済み=残存 0。新規サイトはここに現れず fail する — 分類根拠を書いて追加するか、
    /// 部分更新(RefreshSelectionMarkers/BuildContextPanels/ApplyModeTransition 様式)へ寄せる。
    /// </summary>
    private static readonly Dictionary<string, (int Count, string Why)> Ledger = new(StringComparer.Ordinal)
    {
        // ---- ImageTabViewModel(26)----
        ["ImageTabViewModel.cs:ImageTabViewModel"] = (1, "A: 言語切替=焼き込みラベル再解決(ECO-108)"),
        ["ImageTabViewModel.cs:LoadCatalogAsync"] = (1, "A: カタログロード完了"),
        ["ImageTabViewModel.cs:LoadContentAsync"] = (2, "A: ロード開始の空UI整合(_loaded=false=軽量)/完了"),
        ["ImageTabViewModel.cs:ReloadTagCatalogAsync"] = (2, "A: view削除退避/タグカタログ再読=チップ・列変化"),
        ["ImageTabViewModel.cs:RefreshContentAsync"] = (1, "A: クロスタブ status 変更後の母集合再取得(作業タブ delete/restore/裁定/マージ=normal/pending 母集合が変わる=再解決が意味論上必要・ECO-131/GF-128-01)"),
        ["ImageTabViewModel.cs:ApplyModeTransition"] = (1, "A: 未ロードガード=O(0)(ECO-114)"),
        ["ImageTabViewModel.cs:SelectAxis"] = (1, "A: 軸切替=母集合変化"),
        ["ImageTabViewModel.cs:LoadViewAsync"] = (1, "A: view不在fallback=FS退避"),
        ["ImageTabViewModel.cs:ReloadViewGraphAsync"] = (1, "A: graph再構築=母集合変化"),
        ["ImageTabViewModel.cs:OpenIntegrityReview"] = (1, "A: 統合裁定確定後のみ(adjudicated ガード)= relink/normal 化/行置換/deleted 化で母集合が変わる=再解決が意味論上必要(ECO-140)"),
        ["ImageTabViewModel.cs:OpenBackupSettings"] = (1, "C: 同上(取り込みが表示中コレクションを変えうる=ECO-077 由来)"),
        ["ImageTabViewModel.cs:SetSortAsc"] = (1, "A: 全件ソート本質"),
        ["ImageTabViewModel.cs:SetSortDesc"] = (1, "A: 全件ソート本質"),
        ["ImageTabViewModel.cs:SelectColumnSort"] = (1, "A: 全件ソート本質"),
        ["ImageTabViewModel.cs:ClearColumnSort"] = (1, "A: 全件ソート本質"),
        ["ImageTabViewModel.cs:SetDisplayMode"] = (1, "A: すべて/未分類=表示集合変化(REQ-094)"),
        ["ImageTabViewModel.cs:GoHome"] = (1, "A: ナビ=母集合変化"),
        ["ImageTabViewModel.cs:GoCrumb"] = (1, "A: ナビ=母集合変化"),
        ["ImageTabViewModel.cs:ClickChip"] = (2, "A: view潜り/FSフィルタ変化=母集合変化(選択縮退込み=ECO-097)"),
        ["ImageTabViewModel.cs:HandleItemClick"] = (1, "A: フォルダ潜り=母集合変化"),
        ["ImageTabViewModel.cs:FullReloadTagsAsync"] = (1, "A: ECO-118 fallback 設計(タグ列ソート中等)"),
        ["ImageTabViewModel.cs:DeleteToTrash"] = (1, "A': データ変化=正当。削除の差分化は ECO-118 の削除版候補(症状未観測=記帳のみ)"),
        ["ImageTabViewModel.cs:OnScanUpdated"] = (2, "A: Started(表示中コレクション自身のみ=取込順固定の即時実現・ECO-060)/Completed=データ変化。他コレクションの Started は部分更新(ECO-125 B-3)"),
        // ---- WorkTabViewModel(13)— Recompute は workspace 規模(ECO-113 R3/ECO-118 の規模前提つき許容)----
        ["WorkTabViewModel.cs:WorkTabViewModel"] = (1, "A: 言語切替=焼き込みラベル再解決(ECO-108)"),
        ["WorkTabViewModel.cs:LoadCurrentImagesAsync"] = (2, "A: ロード開始/完了"),
        ["WorkTabViewModel.cs:ReloadTagsAsync"] = (1, "A': 選択スコープ再読後(ECO-118 裁定=Recompute は workspace 規模につき残置)"),
        ["WorkTabViewModel.cs:RunSearch"] = (1, "A: 検索結果変化"),
        ["WorkTabViewModel.cs:AddCandidateToTargets"] = (1, "A': workspace 規模前提(ImageTab 側は ECO-125 B-1 是正=規模差の面間非対称は規模前提の失効時に read-across)"),
        ["WorkTabViewModel.cs:UndoMerge"] = (1, "A: 補償適用=データ変化"),
        ["WorkTabViewModel.cs:ContinueOrganize"] = (1, "A': workspace 規模前提(ImageTab 側は ECO-125 B-2 是正・同上)"),
        ["WorkTabViewModel.cs:ClickChip"] = (1, "A: フィルタ変化+選択依存更新"),
        ["WorkTabViewModel.cs:SelectColumnSort"] = (1, "A: 全件ソート本質"),
        ["WorkTabViewModel.cs:SetSortAsc"] = (1, "A: 全件ソート本質"),
        ["WorkTabViewModel.cs:SetSortDesc"] = (1, "A: 全件ソート本質"),
        ["WorkTabViewModel.cs:ClearColumnSort"] = (1, "A: 全件ソート本質"),
    };

    [Fact]
    public void Recompute結合点は台帳と全数一致する()
    {
        var root = RepoRoot();
        var actual = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var rel in TargetFiles)
        {
            var name = Path.GetFileName(rel);
            foreach (var (method, count) in CountCallSites(File.ReadAllText(Path.Combine(root, rel))))
                actual[$"{name}:{method}"] = count;
        }

        var unexpected = actual.Where(kv => !Ledger.TryGetValue(kv.Key, out var e) || e.Count != kv.Value)
            .Select(kv => $"{kv.Key}={kv.Value}(台帳 {(Ledger.TryGetValue(kv.Key, out var e) ? e.Count : 0)})").ToList();
        var stale = Ledger.Keys.Except(actual.Keys, StringComparer.Ordinal).ToList();

        Assert.True(unexpected.Count == 0,
            $"台帳外の Recompute 結合 {unexpected.Count} 件。母集合再解決が意味論上必要かを分類し、"
            + "不要なら部分更新(RefreshSelectionMarkers/BuildContextPanels/ApplyModeTransition 様式)へ、"
            + $"必要なら分類根拠を書いて台帳へ(ECO-125): {string.Join(", ", unexpected)}");
        Assert.True(stale.Count == 0,
            $"台帳の死亡エントリ {stale.Count} 件(サイト消滅=台帳から除去): {string.Join(", ", stale)}");
    }

    [Fact]
    public void 陽性対照_新規結合と件数増を検出できる()
    {
        const string sample = """
            private void Foo()
            {
                Recompute();
                _recompute(); // デリゲート=対象外
                // Recompute(); コメント=対象外
                this.Recompute();
            }
            private void Recompute() { }
            """;
        var counts = CountCallSites(sample);
        Assert.Equal(2, counts["Foo"]);
        Assert.False(counts.ContainsKey("Recompute"));
    }
}
