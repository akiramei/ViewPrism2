using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ViewPrism2.App.ViewModels;
using ViewPrism2.App.Views;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-TAGDLG-087: タグ作成/編集ダイアログの mock 忠実化(ECO-087)。
/// probe は CAD 視覚契約 VC-TAG-4〜8(ViewPrismUI tag_tab.md・d432596 で lazy 遡及)から生成。
/// R5: 是正前は 2 カラム・ヘッダサブタイトル・ピル行(×/件数ヘッダ)が存在せず赤になる
/// (named 要素の不在=ECO-056/084 様式・レイアウトは実測)。
/// </summary>
[Trait("cp", "CP-TAGDLG-087")]
public sealed class CpTagDlg087Tests : IDisposable
{
    private readonly TempDb _db = new();

    public void Dispose() => _db.Dispose();

    private async Task<(TagEditorViewModel Vm, TagService Service)> NewTextualVmAsync(LocalizationService? loc = null)
    {
        var tagService = new TagService(_db.Tags);
        var tag = (await tagService.CreateAsync("性別", TagType.Textual)).Value!;
        Assert.True((await tagService.SetTextualSettingsAsync(tag.Id, ["男", "女"])).IsSuccess);
        var vm = new TagEditorViewModel(tag, tagService, _db.Tags, loc ?? TestLoc.Ja());
        await vm.LoadAsync();
        return (vm, tagService);
    }

    private static void RunJobs()
    {
        for (var i = 0; i < 8; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    [Fact]
    public async Task ボディは2カラムで左に基本情報右に種別別設定が並ぶ()
    {
        // VC-TAG-4: 左=基本情報(タグ名)・右=種別別設定(選択肢リスト)が横並び。
        // 是正前=1 カラム縦積み(X 範囲が重なる)で赤。
        var (vm, _) = await NewTextualVmAsync();
        await HeadlessApp.Session.Dispatch(() =>
        {
            var window = new TagEditorWindow { DataContext = vm };
            window.Show();
            RunJobs();

            var nameBox = window.GetVisualDescendants().OfType<TextBox>()
                .First(t => t.Text == "性別");
            var valuesList = window.GetVisualDescendants().OfType<ListBox>()
                .First(l => l.Name == "ValuesList");
            var nameTb = nameBox.GetTransformedBounds()!.Value;
            var listTb = valuesList.GetTransformedBounds()!.Value;
            var nameRect = nameTb.Bounds.TransformToAABB(nameTb.Transform);
            var listRect = listTb.Bounds.TransformToAABB(listTb.Transform);
            Assert.True(listRect.X >= nameRect.Right,
                $"2 カラムでない: 選択肢リスト(X={listRect.X:0.0})がタグ名(Right={nameRect.Right:0.0})の右に並んでいない(VC-TAG-4)");

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ヘッダのサブタイトルが種別切替に追随する()
    {
        // VC-TAG-5: ヘッダに種別で変わるサブタイトル。是正前= HeaderSubtitle 不在で赤。
        var tagService = new TagService(_db.Tags);
        var vm = new TagEditorViewModel(null, tagService, _db.Tags, TestLoc.Ja());
        await HeadlessApp.Session.Dispatch(() =>
        {
            var window = new TagEditorWindow { DataContext = vm };
            window.Show();
            RunJobs();

            var subtitle = window.GetVisualDescendants().OfType<TextBlock>()
                .FirstOrDefault(t => t.Name == "HeaderSubtitle");
            Assert.True(subtitle is not null, "HeaderSubtitle が存在しない(ヘッダ種別サブタイトルの不在=VC-TAG-5 プローブ)");

            vm.SelectTypeCommand.Execute(vm.TypeOptions[1]); // テキスト
            RunJobs();
            Assert.Equal("選択式のテキストタグを作成します", subtitle!.Text);

            vm.SelectTypeCommand.Execute(vm.TypeOptions[2]); // 数値
            RunJobs();
            Assert.Equal("数値入力タグを作成します", subtitle.Text);

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task 選択肢はピル行と件数ヘッダで表示され行の削除ボタンで消せる()
    {
        // VC-TAG-6: 件数ヘッダ「登録済みの選択肢 (2)」+行内 × 削除。是正前=どちらも不在で赤。
        var (vm, _) = await NewTextualVmAsync();
        await HeadlessApp.Session.Dispatch(() =>
        {
            var window = new TagEditorWindow { DataContext = vm };
            window.Show();
            RunJobs();

            var countHeader = window.GetVisualDescendants().OfType<TextBlock>()
                .FirstOrDefault(t => t.Name == "OptionCountHeader");
            Assert.True(countHeader is not null, "OptionCountHeader が存在しない(件数ヘッダの不在=VC-TAG-6 プローブ)");
            Assert.Contains("(2)", countHeader!.Text, StringComparison.Ordinal);

            // 検査対象は選択肢リスト配下のみ(ヘッダの X も同意匠クラスのため全域では数え過ぎる)
            var valuesList = window.GetVisualDescendants().OfType<ListBox>().First(l => l.Name == "ValuesList");
            var removeButtons = valuesList.GetVisualDescendants().OfType<Button>()
                .Where(b => b.Classes.Contains("pillRemove"))
                .ToList();
            Assert.True(removeButtons.Count == 2, $"ピル行の × 削除が行ごとに無い(実測 {removeButtons.Count}/2=VC-TAG-6 プローブ)");

            removeButtons[0].Command?.Execute(removeButtons[0].CommandParameter);
            RunJobs();
            Assert.Equal(["女"], vm.PredefinedValues);
            Assert.Contains("(1)", countHeader.Text, StringComparison.Ordinal);

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task 選択肢0件では破線プレースホルダが出る()
    {
        // VC-TAG-6: 0 件時プレースホルダ「選択肢を1つ以上追加してください」。
        var tagService = new TagService(_db.Tags);
        var vm = new TagEditorViewModel(null, tagService, _db.Tags, TestLoc.Ja());
        vm.SelectTypeCommand.Execute(vm.TypeOptions[1]); // テキスト
        await HeadlessApp.Session.Dispatch(() =>
        {
            var window = new TagEditorWindow { DataContext = vm };
            window.Show();
            RunJobs();

            var placeholder = window.GetVisualDescendants().OfType<TextBlock>()
                .FirstOrDefault(t => t.IsVisible && t.Text == "選択肢を1つ以上追加してください");
            Assert.True(placeholder is not null, "0 件プレースホルダが出ない(VC-TAG-6 プローブ)");

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task 未選択のピルでもドラッグハンドルから並べ替えを開始できる()
    {
        // GF-087-01(golden 所見: ドラッグで並べ替えられない)。真因=ListBox(SelectingItemsControl)の
        // クラスハンドラが「未選択項目の押下=選択更新成功」で e.Handled=true にするため、XAML の
        // PointerPressed(bubble・handledEventsToo=false)へ届かず D&D が始まらない(従来からの潜伏欠陥。
        // ↑↓ボタンが実用経路としてマスクし、ECO-087 の撤去で顕在化)。
        // 是正=mock どおりドラッグハンドル(⠿)を明示のドラッグ起点(direct ハンドラ・選択と競合しない)へ。
        // 是正前赤=pillHandle 不在。press 到達は handled より内側(バブルの先頭)で受けることを実測。
        // DoDragDropAsync 以降の実ドラッグは headless で再現不能=golden(実機)の検査項目。
        var (vm, _) = await NewTextualVmAsync();
        await HeadlessApp.Session.Dispatch(() =>
        {
            var window = new TagEditorWindow { DataContext = vm };
            window.Show();
            RunJobs();

            var valuesList = window.GetVisualDescendants().OfType<ListBox>().First(l => l.Name == "ValuesList");
            var handles = valuesList.GetVisualDescendants().OfType<Border>()
                .Where(b => b.Classes.Contains("pillHandle"))
                .ToList();
            Assert.True(handles.Count == 2, $"ドラッグハンドルが行ごとに無い(実測 {handles.Count}/2=GF-087-01 プローブ)");

            // 未選択状態の 2 行目ハンドルへの press が、選択処理(クラスハンドラ)に食われる前に
            // ハンドル(direct)へ届き Handled=true で消費される(=ドラッグ起点が成立し選択と分離)
            Assert.Null(vm.SelectedPredefinedValue);
            var reached = false;
            var handledByHandle = false;
            handles[1].AddHandler(Avalonia.Input.InputElement.PointerPressedEvent,
                (_, args) => { reached = true; handledByHandle = args.Handled; },
                Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
            var tb = handles[1].GetTransformedBounds()!.Value;
            var rect = tb.Bounds.TransformToAABB(tb.Transform);
            window.MouseDown(rect.Center, Avalonia.Input.MouseButton.Left);
            RunJobs();
            Assert.True(reached, "未選択ピルのハンドル押下がドラッグ起点へ届かない(選択処理に食われる=GF-087-01)");
            Assert.True(handledByHandle, "ハンドル押下がドラッグ起点で消費されない(選択処理と分離されていない=GF-087-01)");
            window.MouseUp(rect.Center, Avalonia.Input.MouseButton.Left);
            RunJobs();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("ja")]
    [InlineData("en")]
    public async Task 新設文言は日英ともウィンドウ内に収まる(string locale)
    {
        // VC-TAG-8: サブタイトル・件数ヘッダ等の新設文言の日英収まり(余白実測=GF-084-01/086-01 教訓)。
        // サブタイトルは作成時のみ表示(mock=作成ダイアログ)のため新規 VM+テキスト型で検査する。
        var tagService = new TagService(_db.Tags);
        var vm = new TagEditorViewModel(null, tagService, _db.Tags,
            locale == "en" ? TestLoc.En() : TestLoc.Ja());
        vm.SelectTypeCommand.Execute(vm.TypeOptions[1]); // テキスト
        vm.PredefinedValues.Add("男");
        vm.PredefinedValues.Add("女");
        await HeadlessApp.Session.Dispatch(() =>
        {
            var window = new TagEditorWindow { DataContext = vm };
            window.Show();
            RunJobs();

            foreach (var name in new[] { "HeaderSubtitle", "OptionCountHeader" })
            {
                var text = window.GetVisualDescendants().OfType<TextBlock>()
                    .FirstOrDefault(t => t.Name == name);
                Assert.True(text is not null, $"{locale}: {name} が存在しない(VC-TAG-5/6 プローブ)");
                var tb = text!.GetTransformedBounds()!.Value;
                var rect = tb.Bounds.TransformToAABB(tb.Transform);
                Assert.True(rect.Width > 0 && rect.Right <= window.Bounds.Width + 0.5,
                    $"{locale}: {name} が切れる/はみ出す(right={rect.Right:0.0} > {window.Bounds.Width:0.0}=VC-TAG-8)");
            }

            window.Close();
            return true;
        }, CancellationToken.None);
    }
}
