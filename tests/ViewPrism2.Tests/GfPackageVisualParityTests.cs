using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ViewPrism2.App.ViewModels;
using ViewPrism2.App.Views;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using ViewPrism2.Core.Services.Package;
using ViewPrism2.Infrastructure.Database;
using ViewPrism2.Infrastructure.I18n;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// GF-073-01(golden 所見 2026-07-12): B-1 実機が mock と乖離 — ①ウィンドウタイトルの
/// 「コレクションを書き出す」が本文にも太字で重複(mock は擬似タイトルバーのみ) ②コレクション
/// カードのフォルダグリフ欠落で文字が密集 ③ボタンテキストが左寄せ(Avalonia Button 既定) ④キャンセルが
/// テーマ既定グレー(mock は白+ボーダーの outline)。mock 視覚言語を headless 実レイアウトで恒久ガード化。
/// ECO-076(CAD mock 改版 5fdf4464): stepper の可視面を B-2 のみ→B-2〜B-4 へ拡大。probe は
/// CAD 視覚契約チェックリスト VC-1〜VC-4(snapshot_export_import.md)から先行生成(R7)。
/// </summary>
[Trait("cp", "CP-UI-G13")]
public sealed class GfPackageVisualParityTests : IDisposable
{
    private static HeadlessUnitTestSession Session => HeadlessApp.Session;

    private readonly TempDb _db = new();

    public void Dispose() => _db.Dispose();

    private static void RunJobs()
    {
        for (var i = 0; i < 8; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    [Fact]
    public async Task B1はタイトル非重複でグリフ付きカードと中央揃えoutlineボタンを持つ()
    {
        var loc = new LocalizationService(I18nResourceLoader.Load(
            Path.Combine(AppContext.BaseDirectory, "Assets", "i18n")));
        var folder = new SyncFolder { Id = "col-1", Name = "旅行", Path = @"C:\col" };
        var exporter = new CollectionPackageExporter(_db.Manager, _db.Clock, "9.9.9");
        var vm = new CollectionExportViewModel(exporter, folder, loc, (_, _) => Task.FromResult<string?>(null));
        await Session.Dispatch(() =>
        {
            var window = new CollectionExportWindow { DataContext = vm };
            window.Show();
            RunJobs();
            try
            {
                // ① ウィンドウタイトルを本文に重複させない(mock は擬似タイトルバーのみ=実装では Window.Title)
                var title = loc.T("package.exportTitle");
                Assert.Equal(0, window.GetVisualDescendants().OfType<TextBlock>()
                    .Count(t => t.Text == title));

                // ② コレクションカードのフォルダグリフ(mock の左端アイコン)
                Assert.Single(window.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>(),
                    p => p.Classes.Contains("collectionGlyph"));

                // ③ フッターボタンはテキスト中央揃え(Avalonia 既定=Left の取り漏れ防止)
                var footer = window.GetVisualDescendants().OfType<Button>()
                    .Where(b => b.Classes.Contains("footerBtn")).ToList();
                Assert.True(footer.Count >= 2, $"GF-073-01③: footerBtn が {footer.Count} 個(期待 2 以上)");
                Assert.All(footer, b => Assert.Equal(HorizontalAlignment.Center, b.HorizontalContentAlignment));

                // ④ キャンセル/閉じるは outline(白背景+ボーダー)でテーマ既定グレーでない
                var outlines = footer.Where(b => b.Classes.Contains("outlineButton")).ToList();
                Assert.NotEmpty(outlines);
                Assert.All(outlines, b =>
                    Assert.Equal(Colors.White, Assert.IsAssignableFrom<ISolidColorBrush>(b.Background).Color));

                // ⑤ GF-073-02: 画像非含有 callout にグリフ(mock のカメラ×斜線)がある
                Assert.Contains(window.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>(),
                    p => p.Classes.Contains("calloutGlyph"));

                // ⑥ GF-073-02: callout の先頭文「画像ファイルは含まれません。」だけが太字(mock のリード強調)
                var noteText = window.GetVisualDescendants().OfType<TextBlock>()
                    .FirstOrDefault(t => t.Name == "NoImagesText");
                Assert.True(noteText is not null, "GF-073-02⑥: NoImagesText が無い");
                var lead = Assert.IsAssignableFrom<Avalonia.Controls.Documents.Run>(noteText!.Inlines![0]);
                Assert.True(lead.FontWeight >= FontWeight.SemiBold, "GF-073-02⑥: リード文が太字でない");
                Assert.True(noteText.Inlines.Count >= 2, "GF-073-02⑥: リード+本文の分離がない");
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// GF-073-03(golden 所見 2026-07-12): B-2 実機が mock と乖離 — ⑦ファイルカードにファイル
    /// グリフ+サイズ淡色行が無くプレーンテキスト 1 行 ⑧概要の作成日時が生 ISO(mock は
    /// yyyy/MM/dd HH:mm) ⑨概要値が左寄せ非強調(mock は右寄せ太字)。
    /// </summary>
    [Fact]
    public async Task B2はファイルカードにグリフとサイズを持ち概要値が右寄せ太字で日時整形される()
    {
        var loc = new LocalizationService(I18nResourceLoader.Load(
            Path.Combine(AppContext.BaseDirectory, "Assets", "i18n")));
        var collection = new SyncFolder { Id = "col-1", Name = "旅行", Path = @"C:\col" };
        var importer = new CollectionPackageImporter(_db.Manager, _db.Clock);
        var vm = new CollectionImportViewModel(importer, collection, loc,
            _ => Task.FromResult<string?>(null), () => Task.FromResult<IReadOnlyList<Tag>>([]));
        var pkg = Path.Combine(Path.GetTempPath(), $"gf07303-{Guid.NewGuid():N}.viewprism2-collection.json");
        File.WriteAllBytes(pkg, new byte[1536]); // ByteSizeFormatter で「1.5 KB」になるサイズ
        try
        {
            await Session.Dispatch(() =>
            {
                // picker/実ファイル読取を経ず「選択済み+互換OK」状態を直接投入(視覚検査が目的)
                vm.PackagePath = pkg;
                vm.Header = new PackageHeader(1, 1, [], null, null,
                    "2026-07-12T08:51:53.502Z", "1.0.0",
                    new PackageCollection("src-1", "旅行", null), [], 5);
                var window = new CollectionImportWindow { DataContext = vm };
                window.Show();
                RunJobs();
                try
                {
                    // ⑦ ファイルカードのファイルグリフ+サイズ淡色行(mock B-2 は 2 行構成)
                    Assert.Contains(window.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>(),
                        p => p.Classes.Contains("fileGlyph"));
                    var size = window.GetVisualDescendants().OfType<TextBlock>()
                        .FirstOrDefault(t => t.Name == "PackageSizeText");
                    Assert.True(size is not null, "GF-073-03⑦: PackageSizeText が無い");
                    Assert.Equal("1.5 KB", size!.Text);

                    // ⑧ 作成日時は yyyy/MM/dd HH:mm(生 ISO を見せない。A-1 SnapshotItemViewModel と同流儀)
                    var expectedDate = DateTime.Parse("2026-07-12T08:51:53.502Z",
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.RoundtripKind)
                        .ToLocalTime()
                        .ToString("yyyy/MM/dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
                    Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),
                        t => t.Text == expectedDate);

                    // ⑨ 概要 5 値は右寄せ太字(summaryValue)
                    var values = window.GetVisualDescendants().OfType<TextBlock>()
                        .Where(t => t.Classes.Contains("summaryValue")).ToList();
                    Assert.Equal(5, values.Count);
                    Assert.All(values, t =>
                    {
                        Assert.Equal(TextAlignment.Right, t.TextAlignment);
                        Assert.True(t.FontWeight >= FontWeight.SemiBold, $"GF-073-03⑨: {t.Text} が太字でない");
                    });
                }
                finally
                {
                    window.Close();
                }
            }, TestContext.Current.CancellationToken);
        }
        finally
        {
            File.Delete(pkg);
        }
    }

    /// <summary>
    /// GF-073-04 ⑩(バッジ式・検証済みで 2 まで点灯)+ECO-076 VC-1(CAD mock 改版 5fdf4464・
    /// snapshot_export_import visualContract): B-2 の stepper はバッジ 4 個・検証済みで 1-2 が青・
    /// 3-4 は灰・接続線は 1-2 間のみ青。完了チェック(緑)は B-4 到達まで出ない。
    /// 旧契約「B-3 以降は非表示」(GF-073-04 ⑪)は mock 改版で全可視面表示へ改訂
    /// (B-3=VC-2/B-4=VC-3/B-1=VC-4 の各テスト)。面別 Window.Title(L1)は改版対象外で維持。
    /// </summary>
    [Fact]
    public async Task B2のstepperはバッジ式で検証済みは2まで点灯し接続線は最初の区間のみ青になる()
    {
        var loc = new LocalizationService(I18nResourceLoader.Load(
            Path.Combine(AppContext.BaseDirectory, "Assets", "i18n")));
        var collection = new SyncFolder { Id = "col-1", Name = "旅行", Path = @"C:\col" };
        var importer = new CollectionPackageImporter(_db.Manager, _db.Clock);
        var vm = new CollectionImportViewModel(importer, collection, loc,
            _ => Task.FromResult<string?>(null), () => Task.FromResult<IReadOnlyList<Tag>>([]));
        await Session.Dispatch(() =>
        {
            vm.PackagePath = @"C:\x\p.viewprism2-collection.json";
            vm.Header = new PackageHeader(1, 1, [], null, null,
                "2026-07-12T08:51:53.502Z", "1.0.0",
                new PackageCollection("src-1", "友達", null), [], 5);
            var window = new CollectionImportWindow { DataContext = vm };
            window.Show();
            RunJobs();
            try
            {
                // ⑩ バッジ 4 個・検証済み(VerifyOk)は 1,2 の 2 個だけ active(mock B-2=VC-1)
                var badges = window.GetVisualDescendants().OfType<Border>()
                    .Where(b => b.Classes.Contains("stepBadge")).ToList();
                Assert.Equal(4, badges.Count);
                Assert.Equal(2, badges.Count(b => b.Classes.Contains("active")));

                // VC-1: 接続線 3 本のうち 1-2 間のみ青(active)
                var lines = window.GetVisualDescendants().OfType<Border>()
                    .Where(b => b.Classes.Contains("stepLine")).ToList();
                Assert.Equal(3, lines.Count);
                Assert.True(lines[0].Classes.Contains("active"), "VC-1: 接続線 1-2 が青でない");
                Assert.False(lines[1].Classes.Contains("active"), "VC-1: 接続線 2-3 が到達前に青");
                Assert.False(lines[2].Classes.Contains("active"), "VC-1: 接続線 3-4 が到達前に青");

                // VC-1: 完了チェック(緑・VC-3 の表現)は B-2 では出ない
                Assert.False(badges[3].Classes.Contains("done"), "VC-1: B-2 でバッジ 4 が done");
                Assert.DoesNotContain(window.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>(),
                    p => p.Classes.Contains("stepCheck") && p.IsVisible);
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// ECO-076 VC-2(CAD mock 改版 5fdf4464): B-3 でも stepper が表示され、1-3=青(現在=3)・4=灰・
    /// 接続線は 1→3 が青・3→4 は灰。面別 Window.Title(L1・GF-073-04 ⑫)は維持。
    /// 旧契約(B-3 以降非表示)pin の改訂先。
    /// </summary>
    [Fact]
    public async Task B3でもstepperが表示され3まで点灯し最後の接続線だけ灰でタイトルはプレビューを維持する()
    {
        var loc = new LocalizationService(I18nResourceLoader.Load(
            Path.Combine(AppContext.BaseDirectory, "Assets", "i18n")));
        var collection = new SyncFolder { Id = "col-1", Name = "旅行", Path = @"C:\col" };
        var importer = new CollectionPackageImporter(_db.Manager, _db.Clock);
        var vm = new CollectionImportViewModel(importer, collection, loc,
            _ => Task.FromResult<string?>(null), () => Task.FromResult<IReadOnlyList<Tag>>([]));
        await Session.Dispatch(() =>
        {
            vm.PackagePath = @"C:\x\p.viewprism2-collection.json";
            vm.Header = new PackageHeader(1, 1, [], null, null,
                "2026-07-12T08:51:53.502Z", "1.0.0",
                new PackageCollection("src-1", "友達", null), [], 5);
            vm.Step = 2;
            var window = new CollectionImportWindow { DataContext = vm };
            window.Show();
            RunJobs();
            try
            {
                var stepper = window.GetVisualDescendants().OfType<StackPanel>()
                    .FirstOrDefault(p => p.Name == "Stepper");
                Assert.True(stepper is not null, "VC-2: Stepper が無い");
                Assert.True(stepper!.IsVisible, "VC-2: B-3 で stepper が非表示(旧契約のまま)");

                var badges = window.GetVisualDescendants().OfType<Border>()
                    .Where(b => b.Classes.Contains("stepBadge")).ToList();
                Assert.Equal(4, badges.Count);
                Assert.Equal(3, badges.Count(b => b.Classes.Contains("active")));
                Assert.False(badges[3].Classes.Contains("active"), "VC-2: バッジ 4 が到達前に青");
                Assert.False(badges[3].Classes.Contains("done"), "VC-2: バッジ 4 が到達前に done");

                var lines = window.GetVisualDescendants().OfType<Border>()
                    .Where(b => b.Classes.Contains("stepLine")).ToList();
                Assert.True(lines[0].Classes.Contains("active"), "VC-2: 接続線 1-2 が青でない");
                Assert.True(lines[1].Classes.Contains("active"), "VC-2: 接続線 2-3 が青でない");
                Assert.False(lines[2].Classes.Contains("active"), "VC-2: 接続線 3-4 が到達前に青");

                // L1(面別 Window.Title)は改版対象外で維持(GF-073-04 ⑫)
                Assert.Equal(loc.T("package.previewTitle", new Dictionary<string, string> { ["name"] = "友達" }),
                    window.Title);
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// ECO-076 VC-3(CAD mock 改版 5fdf4464): B-4 は stepper の 4 が緑塗り丸+白チェック+緑ラベル
    /// (数字ではなくチェックマーク・緑=#0F9D76 は CAD capture B-4.png 実測)・接続線は全区間青。
    /// 面別 Window.Title(L1)は維持。
    /// </summary>
    [Fact]
    public async Task B4はstepperの完了が緑チェックになり接続線が全区間青でタイトルは取り込み結果になる()
    {
        var loc = new LocalizationService(I18nResourceLoader.Load(
            Path.Combine(AppContext.BaseDirectory, "Assets", "i18n")));
        var collection = new SyncFolder { Id = "col-1", Name = "旅行", Path = @"C:\col" };
        var importer = new CollectionPackageImporter(_db.Manager, _db.Clock);
        var vm = new CollectionImportViewModel(importer, collection, loc,
            _ => Task.FromResult<string?>(null), () => Task.FromResult<IReadOnlyList<Tag>>([]));
        await Session.Dispatch(() =>
        {
            vm.PackagePath = @"C:\x\p.viewprism2-collection.json";
            vm.Header = new PackageHeader(1, 1, [], null, null,
                "2026-07-12T08:51:53.502Z", "1.0.0",
                new PackageCollection("src-1", "友達", null), [], 5);
            vm.Step = 3;
            var window = new CollectionImportWindow { DataContext = vm };
            window.Show();
            RunJobs();
            try
            {
                var stepper = window.GetVisualDescendants().OfType<StackPanel>()
                    .FirstOrDefault(p => p.Name == "Stepper");
                Assert.True(stepper is not null && stepper.IsVisible, "VC-3: B-4 で stepper が非表示");

                // バッジ 4=done(緑塗り #0F9D76)・数字 4 は消え白チェックが出る
                var badges = window.GetVisualDescendants().OfType<Border>()
                    .Where(b => b.Classes.Contains("stepBadge")).ToList();
                Assert.True(badges[3].Classes.Contains("done"), "VC-3: バッジ 4 が done でない");
                Assert.Equal(Color.Parse("#0F9D76"),
                    Assert.IsAssignableFrom<ISolidColorBrush>(badges[3].Background).Color);
                var number = badges[3].GetVisualDescendants().OfType<TextBlock>()
                    .FirstOrDefault(t => t.Text == "4");
                Assert.True(number is null || !number.IsVisible, "VC-3: 完了到達後も数字 4 が見える");
                var check = badges[3].GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>()
                    .FirstOrDefault(p => p.Classes.Contains("stepCheck"));
                Assert.True(check is not null && check.IsVisible, "VC-3: 白チェックが無い");
                Assert.Equal(Colors.White,
                    Assert.IsAssignableFrom<ISolidColorBrush>(check!.Stroke).Color);

                // ラベル「完了」=緑(done)
                var labels = window.GetVisualDescendants().OfType<TextBlock>()
                    .Where(t => t.Classes.Contains("stepLabel")).ToList();
                Assert.Equal(4, labels.Count);
                Assert.True(labels[3].Classes.Contains("done"), "VC-3: ラベル 完了 が緑でない");

                // 接続線は全区間青(全到達)
                var lines = window.GetVisualDescendants().OfType<Border>()
                    .Where(b => b.Classes.Contains("stepLine")).ToList();
                Assert.All(lines, l => Assert.True(l.Classes.Contains("active"), "VC-3: 接続線に灰区間が残る"));

                // L1(面別 Window.Title)は改版対象外で維持(GF-073-04 ⑫)
                Assert.Equal(loc.T("package.resultTitle"), window.Title);
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// ECO-076 VC-4(CAD mock 改版 5fdf4464): 書き出し B-1 は単段のため stepper を出さない(面の発明禁止)。
    /// </summary>
    [Fact]
    public async Task 書き出しB1は単段のためstepperを出さない()
    {
        var loc = new LocalizationService(I18nResourceLoader.Load(
            Path.Combine(AppContext.BaseDirectory, "Assets", "i18n")));
        var folder = new SyncFolder { Id = "col-1", Name = "旅行", Path = @"C:\col" };
        var exporter = new CollectionPackageExporter(_db.Manager, _db.Clock, "9.9.9");
        var vm = new CollectionExportViewModel(exporter, folder, loc, (_, _) => Task.FromResult<string?>(null));
        await Session.Dispatch(() =>
        {
            var window = new CollectionExportWindow { DataContext = vm };
            window.Show();
            RunJobs();
            try
            {
                Assert.DoesNotContain(window.GetVisualDescendants().OfType<Border>(),
                    b => b.Classes.Contains("stepBadge"));
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// GF-073-07(golden 所見 2026-07-12): B-3 が mock と乖離 — ⑬タグ件数がプレーンテキスト
    /// (mock は緑/青/黄のチップ 3 個)・「競合の解決(未解決 N 件)」見出し欠落 ⑭競合カードの状態色
    /// なし(mock は要対応=淡黄・解決済み=白) ⑮画像5状態タイルが無彩色の横並び(mock は
    /// 配色つき 2×2+未解決ワイド+検算行) ⑯取り込み先ルート/未解決行のグリフ欠落
    /// ⑰タグ/画像の 2 カラムレイアウト未転写(CAD layoutInvariant)。
    /// 許容差分(裁定済み・§8.6 項目 9): ルート「変更」・「場所を指定」非搭載。
    /// </summary>
    [Fact]
    public async Task B3は2カラムでチップと競合状態色と配色タイルと検算行を持つ()
    {
        var loc = new LocalizationService(I18nResourceLoader.Load(
            Path.Combine(AppContext.BaseDirectory, "Assets", "i18n")));
        var collection = new SyncFolder { Id = "col-1", Name = "旅行", Path = @"C:\col" };
        var importer = new CollectionPackageImporter(_db.Manager, _db.Clock);
        var vm = new CollectionImportViewModel(importer, collection, loc,
            _ => Task.FromResult<string?>(null), () => Task.FromResult<IReadOnlyList<Tag>>([]));
        await Session.Dispatch(() =>
        {
            // B-3 状態を直接投入(視覚検査が目的。mock B-3 の数値を使用)
            vm.PackagePath = @"C:\x\p.viewprism2-collection.json";
            vm.Step = 2;
            vm.ImageCounts = new ImageMatchCounts(12405, 823, 34, 5, 17);
            vm.UnresolvedSamples.Add("friends/2025/ev_0431.png");
            vm.Conflicts.Add(new TagConflictRowViewModel(
                new TagPlanItem(new PackageTagDef("t1", "five", TagType.Numeric, null, null, null, [], 0, 5, null, null),
                    TagImportDecision.Conflict, null, null, null, "既存 'five' と名前が衝突"), [], () => { }));
            var window = new CollectionImportWindow { DataContext = vm };
            window.Show();
            RunJobs();
            try
            {
                // ⑬ タグ件数チップ 3 個+競合見出し
                Assert.Equal(3, window.GetVisualDescendants().OfType<Border>()
                    .Count(b => b.Classes.Contains("tagChip")));
                Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),
                    t => t.Name == "ConflictHeading");

                // ⑭ 競合カード: 要対応=非 resolved → スキップ解決で resolved クラスへ
                var card = window.GetVisualDescendants().OfType<Border>()
                    .First(b => b.Classes.Contains("conflictCard"));
                Assert.DoesNotContain("resolved", card.Classes);
                vm.Conflicts[0].SkipCommand.Execute(null);
                RunJobs();
                Assert.True(card.Classes.Contains("resolved"), "GF-073-07⑭: 解決済みで白カードへ切替わらない");

                // ⑮ 5 状態タイルの配色クラス(exact/moved/warn×3)+検算行
                // stateTile は B-4 の結果タイルと共有クラスのため、B-3 固有の配色変種で数える
                var tiles = window.GetVisualDescendants().OfType<Border>()
                    .Where(b => b.Classes.Contains("stateTile")).ToList();
                Assert.Equal(1, tiles.Count(t => t.Classes.Contains("exact")));
                Assert.Equal(1, tiles.Count(t => t.Classes.Contains("moved")));
                Assert.Equal(3, tiles.Count(t => t.Classes.Contains("warn")));
                var sum = window.GetVisualDescendants().OfType<TextBlock>()
                    .FirstOrDefault(t => t.Name == "StateSumText");
                Assert.True(sum is not null && sum.Text!.Contains("13,284"), "GF-073-07⑮: 検算行が無い");

                // ⑯ 取り込み先ルート/未解決行のグリフ
                Assert.Contains(window.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>(),
                    p => p.Classes.Contains("rootGlyph"));
                Assert.Contains(window.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>(),
                    p => p.Classes.Contains("sampleGlyph"));

                // ⑰ 広幅(既定 940)ではタグ/画像が 2 カラム(画像列は col=2)
                var imageColumn = window.GetVisualDescendants().OfType<StackPanel>()
                    .FirstOrDefault(p => p.Name == "ImageColumn");
                Assert.True(imageColumn is not null, "GF-073-07⑰: ImageColumn が無い");
                Assert.Equal(2, Grid.GetColumn(imageColumn!));
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }
}
