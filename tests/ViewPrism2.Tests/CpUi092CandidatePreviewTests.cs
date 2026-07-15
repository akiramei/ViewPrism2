using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ViewPrism2.App.Services;
using ViewPrism2.App.ViewModels;
using ViewPrism2.App.Views;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-CHIPWRAP-088 パレット面拡張(ECO-092・CAD VC-TAG-10=tag_tab.md・TAG-013=T-a 裁定 2026-07-15):
/// タグパレットの候補値=閲覧プレビュー。領域は最大 2 行・溢れは**非対話**の「ほか N 件」(件数のみ・
/// N=非表示数)・展開/ポップオーバーなし・定義順維持・長い値はチップ幅上限+省略+ツールチップ。
/// IMG-023A/B(ECO-091=操作面)とは別契約 — 是正前赤の真因=折返しのみ(ECO-089 面 A)で
/// 容量上限・「ほか N 件」・幅上限が未実装。
/// </summary>
[Trait("cp", "CP-CHIPWRAP-088")]
public sealed class CpUi092CandidatePreviewTests : IDisposable
{
    private const double WindowWidth = 1366;
    /// <summary>チップ幅上限(mock=130px 級)+枠 2px の許容。</summary>
    private const double ChipMaxWidth = 134;
    private const double RowTolerance = 6;

    private readonly TempDb _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task 候補値47件カードは最大2行に収まりほかN件のNが一致し定義順を保つ()
    {
        // VC-TAG-10: 最大 2 行+非対話「ほか N 件」(N=非表示数)。定義順維持(動的並べ替えなし)。
        var tagService = new TagService(_db.Tags);
        var tag = (await tagService.CreateAsync("都道府県", TagType.Textual)).Value!;
        Assert.True((await tagService.SetTextualSettingsAsync(tag.Id, PrefectureNames47, TagValueDomain.Suggest)).IsSuccess);

        await HeadlessApp.Session.Dispatch(async () =>
        {
            var (window, _) = await RenderAsync();
            var chips = ValueChips(window);
            var more = MoreText(window);
            Assert.True(more is not null, "「ほか N 件」が存在しない(VC-TAG-10・ECO-092)");

            var rects = chips.Select(GlobalRect).Concat([GlobalRect(more!)]).ToList();
            Assert.True(RowsOf(rects) <= 2,
                $"候補値領域が {RowsOf(rects)} 行 — 最大 2 行(TAG-013=T-a)を超過(ECO-092)");

            var n = ParseCount(more!);
            Assert.Equal(47, chips.Count + n); // N=非表示数(到達性の会計はダイアログ側=表示は要約)

            // 定義順のまま先頭から表示(使用中優先などの動的並べ替えなし)
            var labels = chips.Select(ChipText).ToList();
            Assert.Equal(PrefectureNames47.Take(labels.Count), labels);

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ほかN件は非対話のテキストで操作可能に見える表現を持たない()
    {
        // VC-TAG-10: クリック・ホバー強調・ポインターカーソル等を付けない(T-a 契約 2)
        var tagService = new TagService(_db.Tags);
        var tag = (await tagService.CreateAsync("都道府県", TagType.Textual)).Value!;
        Assert.True((await tagService.SetTextualSettingsAsync(tag.Id, PrefectureNames47, TagValueDomain.Suggest)).IsSuccess);

        await HeadlessApp.Session.Dispatch(async () =>
        {
            var (window, _) = await RenderAsync();
            var more = MoreText(window);
            Assert.True(more is not null, "「ほか N 件」が存在しない(VC-TAG-10・ECO-092)");
            Assert.IsType<TextBlock>(more); // Button 等の対話部品でない
            // 祖先にも Button が居ない(行全体がボタン化されていない)
            Visual? v = more;
            while (v is not null && v is not Window)
            {
                Assert.False(v is Button, "「ほか N 件」の祖先に Button — 操作対象に見える(VC-TAG-10)");
                v = v.GetVisualParent();
            }
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task 長い候補値はチップ幅上限内で省略されツールチップで全文を確認できる()
    {
        // VC-TAG-10: 長い値=幅上限+省略表示+ツールチップ(完全文字列)
        var tagService = new TagService(_db.Tags);
        var tag = (await tagService.CreateAsync("撮影ロケーション", TagType.Textual)).Value!;
        Assert.True((await tagService.SetTextualSettingsAsync(tag.Id, LongValues, TagValueDomain.Suggest)).IsSuccess);

        await HeadlessApp.Session.Dispatch(async () =>
        {
            var (window, _) = await RenderAsync();
            var chips = ValueChips(window);
            Assert.NotEmpty(chips);
            foreach (var chip in chips)
            {
                var rect = GlobalRect(chip);
                Assert.True(rect.Width <= ChipMaxWidth,
                    $"候補値チップの幅 {rect.Width:0.0}px > 上限 {ChipMaxWidth}px(VC-TAG-10・ECO-092)");
            }
            // 先頭の長い値: ツールチップ=完全文字列
            var first = chips.First(c => ChipText(c).Length > 0 && LongValues[0].StartsWith(ChipText(c).TrimEnd('…')));
            Assert.Equal(LongValues[0], ToolTip.GetTip(first) as string);
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task 少数候補値カードは折畳まれずほかN件も出ない()
    {
        // 少数(2 件=性別級)は従来視覚の不変 pin(ECO-089 と同じ規模)
        var tagService = new TagService(_db.Tags);
        var tag = (await tagService.CreateAsync("性別", TagType.Textual)).Value!;
        Assert.True((await tagService.SetTextualSettingsAsync(tag.Id, ["男", "女"], TagValueDomain.Suggest)).IsSuccess);

        await HeadlessApp.Session.Dispatch(async () =>
        {
            var (window, _) = await RenderAsync();
            var chips = ValueChips(window);
            Assert.Equal(2, chips.Count);
            Assert.Null(MoreText(window));
            Assert.Equal(1, RowsOf(chips.Select(GlobalRect).ToList()));
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    // ---- GF-092-02: スクロールバー出没(±16px 級の幅変化)で折畳みが発振しない ----

    [Fact]
    public void 折畳み後の微小な幅変化では折畳みを維持する()
    {
        // パレットは ScrollViewer 内=折畳みでカード高さが変わるとバーが出没し幅が ±16px 級で変わる。
        // 微小変化で折畳みを解除すると「解除→カード伸長→バー出現→幅減→再折畳み→…」の発振になり
        // UI が異様に重くなる(GF-092-02=maintainer 実機・en 切替で顕在化)。
        var row = NewRow47();
        row.ReportCandidateLayout(SynthRects(47, panelWidth: 300), null, 300);
        Assert.Contains(row.CandidateDisplay, o => o is ChipMoreVM); // 折畳み成立(前提)
        var folded = row.CandidateDisplay.Count;

        // スクロールバー相当(−16px)。折畳み後の実描画(可視 k 件・2 行以内)を報告
        var visibleChips = folded - 1;
        row.ReportCandidateLayout(SynthRects(visibleChips, panelWidth: 284), new Rect(200, 26, 60, 20), 284);
        Assert.Contains(row.CandidateDisplay, o => o is ChipMoreVM); // 折畳み維持(発振しない)

        // 実質的な幅変化(>24px)は再計算(全表示へ戻して測り直す)
        row.ReportCandidateLayout(SynthRects(visibleChips, panelWidth: 360), new Rect(200, 26, 60, 20), 360);
        Assert.DoesNotContain(row.CandidateDisplay, o => o is ChipMoreVM); // リセット=次の描画で再計測
    }

    private static TagPaletteRowViewModel NewRow47() =>
        new(new Tag { Id = "t47", Name = "都道府県", Type = TagType.Textual },
            typeText: "テキスト", predefinedValues: PrefectureNames47, numeric: null,
            moreLabel: n => $"ほか {n} 件");

    /// <summary>合成矩形: チップ幅 55+ギャップ 6 で panelWidth に流し込む(折返しは行送り 26px)。</summary>
    private static List<Rect> SynthRects(int count, double panelWidth)
    {
        const double W = 55, H = 20, GapX = 6, RowH = 26;
        var rects = new List<Rect>();
        double x = 0, y = 0;
        for (var i = 0; i < count; i++)
        {
            if (x + W > panelWidth) { x = 0; y += RowH; }
            rects.Add(new Rect(x, y, W, H));
            x += W + GapX;
        }
        return rects;
    }

    // ---- ヘルパ ----

    private async Task<(Window Window, TagsTabViewModel Vm)> RenderAsync()
    {
        var vm = NewTagsVm();
        await vm.Palette.LoadAsync();
        var window = new Window { Content = new TagsTabView { DataContext = vm }, Width = WindowWidth, Height = 900 };
        window.Show();
        RunJobs();
        return (window, vm);
    }

    private static List<Border> ValueChips(Window window) =>
        window.GetVisualDescendants().OfType<Border>()
            .Where(b => b.Classes.Contains("valueChip") && b.IsVisible)
            .ToList();

    private static string ChipText(Border chip) =>
        chip.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).FirstOrDefault(t => !string.IsNullOrEmpty(t)) ?? "";

    private static TextBlock? MoreText(Window window) =>
        window.GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(t => t.Classes.Contains("valueMore") && t.IsVisible);

    private static int ParseCount(TextBlock more)
    {
        var digits = new string((more.Text ?? "").Where(char.IsDigit).ToArray());
        Assert.False(digits.Length == 0, $"「ほか N 件」から N を読めない: '{more.Text}'");
        return int.Parse(digits);
    }

    private static int RowsOf(IReadOnlyList<Rect> rects)
    {
        var rows = new List<double>();
        foreach (var y in rects.Select(r => r.Y).OrderBy(y => y))
        {
            if (rows.Count == 0 || y - rows[^1] > RowTolerance) rows.Add(y);
        }
        return rows.Count;
    }

    private static Rect GlobalRect(Visual v)
    {
        var tb = v.GetTransformedBounds()!.Value;
        return tb.Bounds.TransformToAABB(tb.Transform);
    }

    private static void RunJobs()
    {
        for (var i = 0; i < 10; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    private TagsTabViewModel NewTagsVm() =>
        new(new ViewService(_db.Views, _db.Clock), new TagService(_db.Tags), _db.Tags,
            TestLoc.Ja(), new StubWindows());

    private static readonly string[] LongValues =
    [
        "スタジオ第2ブース（窓際・自然光・レフ板あり）",
        "屋外・河川敷の午後逆光（ゴールデンアワー手前）",
        "市街地・夜景",
        "海岸（日没前後のマジックアワー）",
    ];

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

    private sealed class StubWindows : IWindowService
    {
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(true);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task ShowFolderManagementAsync() => Task.CompletedTask;
        public Task ShowSettingsAsync() => Task.CompletedTask;
        public Task ShowSnapshotsAsync() => Task.CompletedTask;
        public Task ShowCollectionExportAsync(string collectionId) => Task.CompletedTask;
        public Task ShowCollectionImportAsync(string collectionId) => Task.CompletedTask;
        public Task<bool> ShowTagEditorAsync(Tag? existing) => Task.FromResult(false);
        public Task<bool> ShowViewEditDialogAsync(View? existing) => Task.FromResult(false);
        public Task<IReadOnlyList<string>?> ShowNumericValueDialogAsync(Tag tag, NumericTagSettings? settings, int selectionCount)
            => Task.FromResult<IReadOnlyList<string>?>(null);
        public Task<NodeConditionResult?> ShowNodeConditionDialogAsync(Tag tag, HierarchyConditionType? currentType, string? currentValueJson)
            => Task.FromResult<NodeConditionResult?>(null);
        public Task ShowRelinkAsync(string folderId) => Task.CompletedTask;
        public void ShowViewer(IReadOnlyList<ImageEntry> ordered, int startIndex) { }
        public Task ShowSimilarSearchAsync(ImageEntry baseImage, IReadOnlyList<ImageEntry> collectionEntries) => Task.CompletedTask;
    }
}
