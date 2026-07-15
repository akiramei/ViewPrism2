using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ViewPrism2.App.ViewModels;
using ViewPrism2.App.Views;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-TAGDLG-087 拡張(ECO-093=案 B 裁定 2026-07-15・CAD VC-TAG-11=tag_tab.md/VPUI afc8878):
/// タグ作成/編集ダイアログのプレビュー帯(テキスト型)は候補値を**先頭 3 件+非対話「ほか N 件」**で
/// 要約表示する(TAG-013=T-a と同様式)。帯は単一行を保ち、右ドックの説明文と重ならない。
/// 是正前赤の真因= PreviewChips が全候補値を横 StackPanel(折返し・クリップなし)で列挙し、
/// 47 件級で説明文と重なって判読不能(maintainer 実機・ECO-092 golden 所見)。
/// </summary>
[Trait("cp", "CP-TAGDLG-087")]
public sealed class CpUi093PreviewSummaryTests : IDisposable
{
    private readonly TempDb _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task 候補値47件のプレビュー帯は先頭3件とほかN件に要約され説明文と重ならない()
    {
        var (vm, _) = await NewTextualVmAsync(PrefectureNames47);
        await HeadlessApp.Session.Dispatch(() =>
        {
            var window = new TagEditorWindow { DataContext = vm };
            window.Show();
            RunJobs();

            // 先頭 3 件のみ(VC-TAG-11)
            var chips = PreviewChipBorders(window);
            Assert.Equal(3, chips.Count);
            Assert.Equal(PrefectureNames47.Take(3), chips.Select(ChipText));

            // 非対話「ほか N 件」= N は非表示数(44)
            var more = PreviewMore(window);
            Assert.True(more is not null, "プレビュー帯に「ほか N 件」がない(VC-TAG-11・ECO-093)");
            var digits = new string((more!.Text ?? "").Where(char.IsDigit).ToArray());
            Assert.Equal(44, int.Parse(digits));

            // 単一行+右ドック説明文(caption)と重ならない(是正前=47 件で交差)
            var captionText = TestLoc.Ja().T("tag.preview.caption");
            var caption = window.GetVisualDescendants().OfType<TextBlock>()
                .First(t => t.Text == captionText);
            var capRect = GlobalRect(caption);
            foreach (var el in chips.Select(GlobalRect).Concat([GlobalRect(more)]))
            {
                Assert.False(el.Intersects(capRect),
                    $"プレビュー帯要素が説明文と交差(el right={el.Right:0.0} / caption X={capRect.X:0.0})— ECO-093 の実機症状");
            }

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task 少数候補値のプレビュー帯は全件表示でほかN件は出ない()
    {
        // VC-TAG-11 後段: 3 件以下は従来視覚の不変(golden G-6/ECO-087 承認済み)
        var (vm, _) = await NewTextualVmAsync(["男", "女"]);
        await HeadlessApp.Session.Dispatch(() =>
        {
            var window = new TagEditorWindow { DataContext = vm };
            window.Show();
            RunJobs();

            var chips = PreviewChipBorders(window);
            Assert.Equal(2, chips.Count);
            Assert.Null(PreviewMore(window));

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    // ---- ヘルパ ----

    private async Task<(TagEditorViewModel Vm, TagService Service)> NewTextualVmAsync(IReadOnlyList<string> values)
    {
        var tagService = new TagService(_db.Tags);
        var tag = (await tagService.CreateAsync("都道府県T", TagType.Textual)).Value!;
        Assert.True((await tagService.SetTextualSettingsAsync(tag.Id, values)).IsSuccess);
        var vm = new TagEditorViewModel(tag, tagService, _db.Tags, TestLoc.Ja());
        await vm.LoadAsync();
        return (vm, tagService);
    }

    private static List<Border> PreviewChipBorders(Window window) =>
        window.GetVisualDescendants().OfType<Border>()
            .Where(b => b.Classes.Contains("previewChip") && b.IsVisible)
            .ToList();

    private static TextBlock? PreviewMore(Window window) =>
        window.GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(t => t.Classes.Contains("previewMore") && t.IsVisible);

    private static string ChipText(Border chip) =>
        chip.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).FirstOrDefault(t => !string.IsNullOrEmpty(t)) ?? "";

    private static Rect GlobalRect(Visual v)
    {
        var tb = v.GetTransformedBounds()!.Value;
        return tb.Bounds.TransformToAABB(tb.Transform);
    }

    private static void RunJobs()
    {
        for (var i = 0; i < 8; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
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
