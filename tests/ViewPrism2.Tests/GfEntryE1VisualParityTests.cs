using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ViewPrism2.App.ViewModels;
using ViewPrism2.App.Views;
using ViewPrism2.Core.Services;
using ViewPrism2.Infrastructure.Database;
using ViewPrism2.Infrastructure.I18n;
using ViewPrism2.Infrastructure.Settings;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// ECO-077(SS-001 再裁定=入口を設定 ▸ データとバックアップへ集約): E-1 入口の視覚契約 probe。
/// CAD snapshot_export_import の視覚契約チェックリスト VC-5〜VC-8 から先行生成(R7・GF 後追い禁止)。
/// VC-5=E-1 行カード(L3: グリフ角丸スクエア+名前太字+副情報淡色+右端白 outline 青文字ボタン)/
/// VC-6=左ナビ選択状態(淡青背景+青文字)+節見出し+末尾注記/VC-7=サマリ書式(L8)/
/// VC-8=整理「…」は誘導 1 項目のみ(M5・実体 2 項目を置かない・淡紫ハイライト+太字)。
/// </summary>
[Trait("cp", "CP-UI-G12")]
public sealed class GfEntryE1VisualParityTests : IDisposable
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

    private static LocalizationService CreateLoc() => new(I18nResourceLoader.Load(
        Path.Combine(AppContext.BaseDirectory, "Assets", "i18n")));

    private SettingsViewModel NewVm(LocalizationService loc) =>
        new(loc, new Core.Models.AppSettings(), new SettingsStore(_db.Directory), null,
            initialSection: App.Services.SettingsSection.DataBackup);

    [Fact]
    public async Task VC5_VC6_データとバックアップ節は行カード3枚とナビ選択と節見出しと注記を備える()
    {
        var loc = CreateLoc();
        var vm = NewVm(loc);
        await Session.Dispatch(() =>
        {
            var window = new SettingsWindow { DataContext = vm };
            window.Show();
            RunJobs();
            try
            {
                // VC-6①: 左ナビ(一般/データとバックアップ)があり、データとバックアップが選択中=淡青背景
                var navItems = window.GetVisualDescendants().OfType<Border>()
                    .Where(b => b.Classes.Contains("navItem")).ToList();
                Assert.True(navItems.Count == 2, $"VC-6: 左ナビ項目が {navItems.Count} 個(期待 2)");
                var selected = navItems.Where(b => b.Classes.Contains("selected")).ToList();
                Assert.True(selected.Count == 1, $"VC-6: 選択中ナビが {selected.Count} 個(期待 1)");
                var navBg = Assert.IsAssignableFrom<ISolidColorBrush>(selected[0].Background).Color;
                Assert.True(navBg.A > 0 && navBg.B > navBg.R && navBg.B > 200,
                    $"VC-6: 選択中ナビ背景が淡青でない({navBg})");
                var navLabel = selected[0].GetVisualDescendants().OfType<TextBlock>()
                    .FirstOrDefault(t => t.Classes.Contains("navLabel"));
                Assert.True(navLabel is not null, "VC-6: 選択中ナビのラベルが無い");
                var navFg = Assert.IsAssignableFrom<ISolidColorBrush>(navLabel!.Foreground).Color;
                Assert.True(navFg.B > navFg.R && navFg.B > navFg.G, $"VC-6: 選択中ナビ文字が青でない({navFg})");

                // VC-5①: 行カード 3 枚(スナップショット/書き出す/取り込む)
                var cards = window.GetVisualDescendants().OfType<Border>()
                    .Where(b => b.Classes.Contains("entryCard")).ToList();
                Assert.True(cards.Count == 3, $"VC-5: 行カードが {cards.Count} 枚(期待 3)");

                // VC-5②: 左端グリフ=角丸スクエア地+アイコン。配色=DB 円筒(濃紺)/上矢印(緑)/下矢印(青)
                var glyphs = window.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>()
                    .Where(p => p.Classes.Contains("entryGlyph")).ToList();
                Assert.True(glyphs.Count == 3, $"VC-5: 行グリフが {glyphs.Count} 個(期待 3)");
                var glyphColors = glyphs.Select(GlyphColor).ToList();
                Assert.True(glyphColors.Any(c => c.G > c.R && c.G > c.B), "VC-5: 緑グリフ(書き出す)が無い");
                Assert.True(glyphColors.Any(c => c.B > 200 && c.B > c.G), "VC-5: 青グリフ(取り込む)が無い");
                Assert.True(glyphColors.Any(c => c.B is > 90 and < 200 && c.R < 100), "VC-5: 濃紺グリフ(スナップショット)が無い");

                // VC-5③: 名前太字+副情報淡色の 2 行構成
                var names = window.GetVisualDescendants().OfType<TextBlock>()
                    .Where(t => t.Classes.Contains("entryName")).ToList();
                Assert.True(names.Count == 3, $"VC-5: 名前行が {names.Count} 個(期待 3)");
                Assert.All(names, t => Assert.Equal(FontWeight.SemiBold, t.FontWeight));
                var subs = window.GetVisualDescendants().OfType<TextBlock>()
                    .Where(t => t.Classes.Contains("entrySub")).ToList();
                Assert.True(subs.Count == 3, $"VC-5: 副情報行が {subs.Count} 個(期待 3)");

                // VC-5④: 行右端に白 outline+青文字ボタン(開く/選ぶ…/ファイルを選ぶ…)
                var actions = window.GetVisualDescendants().OfType<Button>()
                    .Where(b => b.Classes.Contains("entryAction")).ToList();
                Assert.True(actions.Count == 3, $"VC-5: 行アクションボタンが {actions.Count} 個(期待 3)");
                Assert.All(actions, b =>
                {
                    var fg = Assert.IsAssignableFrom<ISolidColorBrush>(b.Foreground).Color;
                    Assert.True(fg.B > fg.R && fg.B > fg.G, $"VC-5: ボタン文字が青でない({fg})");
                    var bg = Assert.IsAssignableFrom<ISolidColorBrush>(b.Background).Color;
                    Assert.True(bg is { R: > 240, G: > 240, B: > 240 }, $"VC-5: ボタン地が白 outline でない({bg})");
                });

                // VC-6②: 節グループ見出し 2 つ(スナップショット(この端末内)/コレクションの移送(他端末・共有))
                var headings = window.GetVisualDescendants().OfType<TextBlock>()
                    .Where(t => t.Classes.Contains("groupHeading")).ToList();
                Assert.True(headings.Count == 2, $"VC-6: 節見出しが {headings.Count} 個(期待 2)");

                // VC-6③: 末尾注記「既存データは削除されません(取り込みは追加のみ)」=淡色
                var note = window.GetVisualDescendants().OfType<TextBlock>()
                    .FirstOrDefault(t => t.Name == "NoDeleteNote");
                Assert.True(note is not null && note.IsVisible, "VC-6: 末尾注記(NoDeleteNote)が無い");
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    private static Color GlyphColor(Avalonia.Controls.Shapes.Path p) =>
        p.Fill is ISolidColorBrush f ? f.Color
        : p.Stroke is ISolidColorBrush s ? s.Color
        : Colors.Transparent;

    [Fact]
    public async Task VC7_スナップショット行サマリはL8書式で0件はplaceholder最小()
    {
        // 先行 probe 初版(是正前=赤)はサマリ要素の存在検査。是正で SnapshotService 注入後、
        // 実 fixture の書式検証(1 件=「最終作成 yyyy/MM/dd HH:mm ・ 1 件」/0 件=SS-004 暫定の
        // placeholder 最小=件数のみ)へ強化した(同一 fix diff・実施記録参照)。
        var loc = CreateLoc();
        var service = new SnapshotService(_db.Manager, _db.Clock, _db.Directory, "9.9.9");
        var dir = Path.Combine(_db.Directory, "snapshots");
        var created = await service.CreateAsync(dir, TestContext.Current.CancellationToken);
        Assert.True(created.IsSuccess);
        var latest = service.List(dir)[0];
        var expected = loc.T("settings.dataBackup.snapshotSummary", new Dictionary<string, string>
        {
            ["date"] = latest.CreatedAtUtc.ToLocalTime()
                .ToString("yyyy/MM/dd HH:mm", System.Globalization.CultureInfo.InvariantCulture),
            ["count"] = "1",
        });

        var vm1 = new SettingsViewModel(loc, new Core.Models.AppSettings { SnapshotDirectory = dir },
            new SettingsStore(_db.Directory), null, service, App.Services.SettingsSection.DataBackup);
        var emptyDir = Path.Combine(_db.Directory, "snapshots-empty");
        Directory.CreateDirectory(emptyDir);
        var vm0 = new SettingsViewModel(loc, new Core.Models.AppSettings { SnapshotDirectory = emptyDir },
            new SettingsStore(_db.Directory), null, service, App.Services.SettingsSection.DataBackup);

        await Session.Dispatch(() =>
        {
            var window = new SettingsWindow { DataContext = vm1 };
            window.Show();
            RunJobs();
            try
            {
                var summary = window.GetVisualDescendants().OfType<TextBlock>()
                    .FirstOrDefault(t => t.Name == "SnapshotSummaryText");
                Assert.True(summary is not null, "VC-7: スナップショット行サマリ(SnapshotSummaryText)が無い");
                Assert.Equal(expected, summary!.Text); // L8: 最終作成 yyyy/MM/dd HH:mm ・ 1 件
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);

        // 0 件=placeholder 最小(件数のみ・最終作成は出さない=ECO-077 設計者適用・SS-004 暫定)
        Assert.Equal(loc.T("settings.dataBackup.snapshotSummaryEmpty"), vm0.SnapshotSummary);
    }

    [Fact]
    [Trait("cp", "CP-UI-G13")]
    public async Task VC8_三点メニューは設定への誘導1項目のみで書き出し取り込みの実体を持たない()
    {
        await Session.Dispatch(() =>
        {
            // ImageTabView の ⋯ Popup は XAML 静的内容(太字/ハイライトはインライン指定)。
            // ECO-079: 文言は Loc[key] バインドへ移行したため、実 loc(ja)を持つ VM を DataContext に与えて
            // 論理ツリーで解決させる($parent[UserControl] は論理祖先探索のため未表示 Popup でも解決)。
            var view = new ImageTabView { DataContext = TestImageTab.NewVm(_db) };
            RunJobs();
            var menu = view.GetLogicalDescendants().OfType<Popup>()
                .Select(p => p.Child)
                .OfType<Control>()
                .FirstOrDefault(c => c.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == "要確認の画像…"));
            Assert.True(menu is not null, "VC-8: ⋯ メニュー(統合裁定を含む Popup)が見つからない");

            var texts = menu!.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();

            // M5: 書き出す/取り込むの実体項目を置かない
            Assert.True(!texts.Contains("コレクションを書き出す…"),
                "VC-8: 実体項目「コレクションを書き出す…」が残っている(M5 違反)");
            Assert.True(!texts.Contains("コレクションを取り込む…"),
                "VC-8: 実体項目「コレクションを取り込む…」が残っている(M5 違反)");

            // 誘導 1 項目: 「設定でバックアップ・移送…」=太字+淡紫ハイライト(区切り線の下)
            var guide = menu.GetLogicalDescendants().OfType<TextBlock>()
                .FirstOrDefault(t => t.Name == "BackupGuideText");
            Assert.True(guide is not null, "VC-8: 誘導項目(BackupGuideText)が無い");
            Assert.Equal("設定でバックアップ・移送…", guide!.Text);
            Assert.Equal(FontWeight.SemiBold, guide.FontWeight);

            var highlight = menu.GetLogicalDescendants().OfType<Border>()
                .FirstOrDefault(b => b.Name == "BackupGuideHighlight");
            Assert.True(highlight is not null, "VC-8: 誘導項目の淡紫ハイライト(BackupGuideHighlight)が無い");
            var bg = Assert.IsAssignableFrom<ISolidColorBrush>(highlight!.Background).Color;
            Assert.True(bg.A > 0 && bg.B > bg.G && bg.R > bg.G && !(bg is { R: 255, G: 255, B: 255 }),
                $"VC-8: 誘導項目の地色が淡紫でない({bg})");
        }, TestContext.Current.CancellationToken);
    }
}
