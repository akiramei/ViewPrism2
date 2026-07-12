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
    /// GF-073-04(golden 所見 2026-07-12): stepper が mock と乖離 — ⑩テキスト矢印でバッジ未転写・
    /// 状態が静的(検証済みでも 1 のまま) ⑪CAD は stepper を B-2 面のみ可視と定義(prose「B-2 に可視」)
    /// なのに全ステップ常時表示 ⑫B-3/B-4 の擬似タイトル(取り込みプレビュー — 名前/取り込み結果)が
    /// Window.Title に未転写。
    /// </summary>
    [Fact]
    public async Task B2のstepperはバッジ式で検証済みは2まで点灯しB3以降は非表示で専用タイトルになる()
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
                // ⑩ バッジ 4 個・検証済み(VerifyOk)は 1,2 の 2 個だけ active(mock B-2)
                var badges = window.GetVisualDescendants().OfType<Border>()
                    .Where(b => b.Classes.Contains("stepBadge")).ToList();
                Assert.Equal(4, badges.Count);
                Assert.Equal(2, badges.Count(b => b.Classes.Contains("active")));

                // ⑪ B-3(プレビュー)では stepper 非表示 ⑫Window.Title=取り込みプレビュー — <名前>
                vm.Step = 2;
                RunJobs();
                var stepper = window.GetVisualDescendants().OfType<StackPanel>()
                    .FirstOrDefault(p => p.Name == "Stepper");
                Assert.True(stepper is not null, "GF-073-04⑪: Stepper が無い");
                Assert.False(stepper!.IsVisible, "GF-073-04⑪: B-3 で stepper が可視");
                Assert.Equal(loc.T("package.previewTitle", new Dictionary<string, string> { ["name"] = "友達" }),
                    window.Title);

                // ⑫ B-4(完了)の Window.Title=取り込み結果
                vm.Step = 3;
                RunJobs();
                Assert.Equal(loc.T("package.resultTitle"), window.Title);
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
