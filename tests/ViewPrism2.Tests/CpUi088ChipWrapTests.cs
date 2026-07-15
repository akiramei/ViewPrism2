using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using ViewPrism2.App.Views;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-CHIPWRAP-088(ECO-088・CAD VC-IMG-8): ビュー軸チップ行のオーバーフロー挙動。
/// mock のチップ行コンテナは flex-wrap:wrap= 折返しが正典。47 件級(都道府県)の定義値展開で
/// 全チップが可視幅内へ折り返して配置され、クリップ切り捨てで到達不能にならないことを実測する。
/// 是正前赤の真因= WrapPanel が横 StackPanel(無限幅測定)内にあり折返し契機を得られない。
/// </summary>
[Trait("cp", "CP-CHIPWRAP-088")]
public sealed class CpUi088ChipWrapTests : IDisposable
{
    private const double WindowWidth = 1366;

    private readonly TempDb _db = new();
    private SyncFolder _col = null!;
    private View _view = null!;

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task 定義値47件のチップは折り返しつつ最大2行とほかN件に収まる()
    {
        // VC-IMG-8: 折返し(mock flex-wrap)が機能し、クリップ切り捨てで到達不能にならない。
        // since ECO-091(IMG-023A=A-b): 47 件の全数直接表示は「最大 2 行+ほか N 件」へ進化 —
        // 可視チップは折返し(行数 2)で全て可視幅内・残りは「ほか N 件」(ポップオーバーで到達=CpUi091)。
        await SeedAsync(PrefectureNames47);
        await HeadlessApp.Session.Dispatch(async () =>
        {
            var rects = await RenderChipRectsAsync();
            Assert.InRange(rects.Count, 2, 46); // 一部が直接表示・残りは「ほか N 件」(ECO-091)

            foreach (var (label, rect) in rects)
            {
                Assert.True(rect.Right <= WindowWidth + 0.5,
                    $"チップ「{label}」が可視幅からはみ出す(right={rect.Right:0.0} > {WindowWidth})— "
                    + "折返し不動作のクリップ切り捨て(ECO-088)");
            }

            // 折返し自体は機能している(容量上限まで使う=2 行)
            var rowCount = rects.Select(r => Math.Round(r.Rect.Y)).Distinct().Count();
            Assert.Equal(2, rowCount);
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task 一行に収まる少数チップの配置は折返しなしのまま変わらない()
    {
        // VC-IMG-8 後段: 1 行に収まる場合の配置は不変(mock デモ= 5 件級・既存 golden の視覚)。
        await SeedAsync(["北海道", "青森県", "岩手県", "宮城県", "秋田県"]);
        await HeadlessApp.Session.Dispatch(async () =>
        {
            var rects = await RenderChipRectsAsync();
            Assert.Equal(5, rects.Count);
            Assert.All(rects, r => Assert.True(r.Rect.Right <= WindowWidth + 0.5));
            var rowCount = rects.Select(r => Math.Round(r.Rect.Y)).Distinct().Count();
            Assert.True(rowCount == 1, $"5 チップが {rowCount} 行に割れている(1 行不変の pin)");
            return true;
        }, CancellationToken.None);
    }

    // ---- ヘルパ ----

    /// <summary>ビュー軸で都道府県ノードへ潜り、値チップの実描画矩形(global)を収集する。</summary>
    private async Task<List<(string Label, Rect Rect)>> RenderChipRectsAsync()
    {
        var vm = TestImageTab.NewVm(_db); // UI スレッド内で構築(Brush スレッドアフィニティ=ECO-084 教訓)
        await vm.InitializeAsync(_col.Id);
        await vm.SelectAxisCommand.ExecuteAsync(_view.Id);
        Assert.True(vm.IsViewAxis);
        vm.ClickChipCommand.Execute(vm.Chips.Single(c => c.Label == "都道府県"));

        var window = new Window { Content = new ImageTabView { DataContext = vm }, Width = WindowWidth, Height = 900 };
        window.Show();
        RunJobs();

        var rects = window.GetVisualDescendants().OfType<Border>()
            .Where(b => b.Classes.Contains("tagChip") && b.IsVisible)
            .Select(b =>
            {
                var label = b.GetVisualDescendants().OfType<TextBlock>()
                    .Select(t => t.Text).FirstOrDefault(t => !string.IsNullOrEmpty(t)) ?? "?";
                var tb = b.GetTransformedBounds()!.Value;
                return (Label: label, Rect: tb.Bounds.TransformToAABB(tb.Transform));
            })
            .ToList();
        window.Close();
        return rects;
    }

    private static void RunJobs()
    {
        for (var i = 0; i < 8; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>DB のみの seed。textual タグ(候補値=values)をビュー階層ノード(defined)へ配置。画像 0 件(定義値展開は 0 件でも全数生成=ECO-086 裁定 d)。</summary>
    private async Task SeedAsync(IReadOnlyList<string> values)
    {
        await HeadlessApp.Session.Dispatch(() => true, CancellationToken.None); // 先行初期化(ECO-084 教訓)
        _col = new SyncFolder { Id = IdGenerator.NewId(), Name = "C", Path = @"C:\col-088" };
        await _db.Folders.AddAsync(_col);

        var tagService = new TagService(_db.Tags);
        var tag = (await tagService.CreateAsync("都道府県", TagType.Textual)).Value!;
        Assert.True((await tagService.SetTextualSettingsAsync(tag.Id, values, TagValueDomain.Suggest)).IsSuccess);

        var viewService = new ViewService(_db.Views, _db.Clock);
        _view = (await viewService.CreateAsync("V088")).Value!;
        var node = new HierarchyNode
        {
            Id = IdGenerator.NewId(), ViewId = _view.Id, TagId = tag.Id, Position = 0,
            ExpansionMode = HierarchyExpansionMode.Defined,
        };
        Assert.True((await viewService.SaveHierarchyAsync(_view.Id, [node], null)).IsSuccess);
    }

    private static readonly string[] PrefectureNames47 =
    [
        "北海道", "青森県", "岩手県", "宮城県", "秋田県", "山形県", "福島県",
        "茨城県", "栃木県", "群馬県", "埼玉県", "千葉県", "東京都", "神奈川県",
        "新潟県", "富山県", "石川県", "福井県", "山梨県", "長野県",
        "岐阜県", "静岡県", "愛知県", "三重県",
        "滋賀県", "京都府", "大阪府", "兵庫県", "奈良県", "和歌山県",
        "鳥取県", "島根県", "岡山県", "広島県", "山口県",
        "徳島県", "香川県", "愛媛県", "高知県",
        "福岡県", "佐賀県", "長崎県", "熊本県", "大分県", "宮崎県", "鹿児島県", "沖縄県",
    ];
}
