using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ViewPrism2.App.ViewModels;
using ViewPrism2.App.Views;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using ViewPrism2.Infrastructure.Database;
using ViewPrism2.Infrastructure.I18n;
using ViewPrism2.Infrastructure.Settings;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// GF-072-01(golden 所見 2026-07-12): 実機 A-1 が mock(CAD snapshot_export_import・権威)より
/// 平板 — 一覧が表形式でない・検証状態がバッジでない・行グリフなし・CTA の + なし。
/// prose CAD が視覚言語を拘束契約に落とさず、実装が既存ダイアログ流儀で行間を埋めた欠陥様式。
/// mock の視覚言語を headless 実レイアウトで ground-truth 実測して恒久ガード化する。
/// </summary>
[Trait("cp", "CP-UI-G12")]
public sealed class GfSnapshotVisualParityTests : IDisposable
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

    /// <summary>検証済み 1 件+検証待ち(メタ欠落)1 件の保存先を用意して VM を作る。</summary>
    private async Task<(SnapshotViewModel Vm, string Dir)> NewVmAsync()
    {
        var dir = Path.Combine(_db.Directory, "snapshots");
        var service = new SnapshotService(_db.Manager, _db.Clock, _db.Directory, "9.9.9");
        var created = await service.CreateAsync(dir, TestContext.Current.CancellationToken);
        Assert.True(created.IsSuccess);
        File.WriteAllBytes(Path.Combine(dir, SnapshotService.FilePrefix + "junk.db"), [0x00]);

        var loc = new LocalizationService(I18nResourceLoader.Load(
            Path.Combine(AppContext.BaseDirectory, "Assets", "i18n")));
        var vm = new SnapshotViewModel(
            service, new AppSettings { SnapshotDirectory = dir }, new SettingsStore(_db.Directory), loc,
            _ => Task.FromResult<string?>(null), _ => Task.FromResult(false), () => { });
        return (vm, dir);
    }

    [Fact]
    public async Task A1一覧は表形式ヘッダとバッジと行グリフを備える()
    {
        var (vm, _) = await NewVmAsync();
        await Session.Dispatch(() =>
        {
            vm.Load();
            var window = new SnapshotWindow { DataContext = vm, Width = 760, Height = 560 };
            window.Show();
            RunJobs();
            try
            {
                // ① 表形式: 一覧ヘッダ行(作成日時/サイズ/状態)が存在する(mock の table header)
                var header = window.GetVisualDescendants().OfType<Border>()
                    .FirstOrDefault(b => b.Name == "ListHeader");
                Assert.True(header is not null, "GF-072-01①: 一覧に表ヘッダ(ListHeader)が無い");

                // ② 検証状態はバッジピル: verified(緑系)と unverified(黄系)が別配色の Border
                var badges = window.GetVisualDescendants().OfType<Border>()
                    .Where(b => b.Classes.Contains("statusBadge")).ToList();
                Assert.True(badges.Count == 2, $"GF-072-01②: 状態バッジが {badges.Count} 個(期待 2)");
                var colors = badges
                    .Select(b => Assert.IsAssignableFrom<ISolidColorBrush>(b.Background).Color)
                    .ToList();
                Assert.True(colors[0] != colors[1], "GF-072-01②: 検証済み/検証待ちのバッジ配色が同一");
                Assert.All(colors, c => Assert.True(c.A > 0, "GF-072-01②: バッジ背景が透明"));

                // ③ 行グリフ(mock の DB シリンダーアイコン)が各行にある
                var glyphs = window.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>()
                    .Where(p => p.Classes.Contains("rowGlyph")).ToList();
                Assert.True(glyphs.Count == 2, $"GF-072-01③: 行グリフが {glyphs.Count} 個(期待 2)");

                // ④ 作成 CTA は「+」プレフィックス付き(mock: + スナップショットを作成)
                var plus = window.GetVisualDescendants().OfType<TextBlock>()
                    .Any(t => t.Classes.Contains("ctaPlus"));
                Assert.True(plus, "GF-072-01④: 作成 CTA の + プレフィックスが無い");
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A1復元ボタンは検証済みのみ青outlineで活性()
    {
        var (vm, _) = await NewVmAsync();
        await Session.Dispatch(() =>
        {
            vm.Load();
            var window = new SnapshotWindow { DataContext = vm, Width = 760, Height = 560 };
            window.Show();
            RunJobs();
            try
            {
                var restores = window.GetVisualDescendants().OfType<Button>()
                    .Where(b => b.Classes.Contains("restoreButton")).ToList();
                Assert.True(restores.Count == 2, $"GF-072-01⑤: 復元ボタンが {restores.Count} 個(期待 2)");
                Assert.Equal(1, restores.Count(b => b.IsEnabled));       // 検証済みのみ活性
                var enabled = restores.Single(b => b.IsEnabled);
                var fg = Assert.IsAssignableFrom<ISolidColorBrush>(enabled.Foreground).Color;
                Assert.True(fg.B > fg.R && fg.B > fg.G, "GF-072-01⑤: 活性復元ボタンの文字色が青系でない(mock=#2F6BED outline)");
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }
}
