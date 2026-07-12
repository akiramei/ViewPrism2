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
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }
}
