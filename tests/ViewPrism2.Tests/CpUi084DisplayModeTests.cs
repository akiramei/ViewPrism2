using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ViewPrism2.App.Services;
using ViewPrism2.App.ViewModels;
using ViewPrism2.App.Views;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using ViewPrism2.Core.Services.Repair;
using ViewPrism2.Core.Services.Similarity;
using ViewPrism2.Infrastructure.Imaging;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// ECO-084/REQ-094: ビュー軸の表示モード「すべて/未分類」。
/// プローブ(R5): 是正前は表示モードの切替導線そのものが存在しない(新機能)。
/// ECO-056 様式= コマンドはリフレクションで解決し「導線の不在」を実行時不合格として実測する
/// (最終形 API で直接書くと是正前はコンパイル不能となり R5 の実測が撮れないため)。
/// 意味論(CAD image_tab.md「表示モード」節・gate① 承認 2026-07-14):
///   display(N) = matched(N) − ⋃ matched(直下の子) / ルート=未分類発見器 / チップ件数追随 /
///   空状態 / ビュー毎 settings 記憶(デバイスローカル・パッケージ非搬送) / FS 軸では非表示。
/// </summary>
[Trait("cp", "CP-DISPMODE-084")]
public sealed class CpUi084DisplayModeTests : IDisposable
{
    private readonly TempDb _db = new();
    private SyncFolder _col = null!;
    private View _view = null!;

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task 未分類モードは直下の子にマッチしない画像だけを表示する()
    {
        // fixture: root.jpg=タグなし / c1.jpg=親 / c2.jpg=親+子。階層= 親ノード > 子ノード。
        var vm = await NewViewVmAsync();

        // すべて(既定): root は累積フィルタ=全 3 枚
        Assert.Equal(["c1.jpg", "c2.jpg", "root.jpg"], ImageNames(vm));

        var toUnc = ResolveCommand(vm, "SetDisplayModeUnclassifiedCommand"); // 是正前: 不在で不合格(ECO-084 プローブ)
        toUnc.Execute(null);

        // root の未分類= どのトップノードにもマッチしない画像だけ(未分類発見器)
        Assert.Equal(["root.jpg"], ImageNames(vm));

        // 親ノードへ潜る: matched=2(c1,c2)のうち子ノードにマッチする c2 を引く
        vm.ClickChipCommand.Execute(vm.Chips.Single(c => c.Label == "親"));
        Assert.Equal(["c1.jpg"], ImageNames(vm));

        // すべてへ戻すと累積フィルタへ復帰
        ResolveCommand(vm, "SetDisplayModeAllCommand").Execute(null);
        Assert.Equal(["c1.jpg", "c2.jpg"], ImageNames(vm));
    }

    [Fact]
    public async Task 未分類モードではチップ件数が子自身の未分類件数へ追随する()
    {
        var vm = await NewViewVmAsync();

        // すべて: root の「親」チップ= matched(親)=2
        Assert.Equal("2", vm.Chips.Single(c => c.Label == "親").Count);

        ResolveCommand(vm, "SetDisplayModeUnclassifiedCommand").Execute(null); // 是正前: 不在で不合格
        // 未分類: 「親」チップ= 親自身の未分類件数(c2 は子ノードへ)= 1
        Assert.Equal("1", vm.Chips.Single(c => c.Label == "親").Count);

        // 潜った先の「子」チップはリーフ= 両モード同数(1)
        vm.ClickChipCommand.Execute(vm.Chips.Single(c => c.Label == "親"));
        Assert.Equal("1", vm.Chips.Single(c => c.Label == "子").Count);
    }

    [Fact]
    public async Task タグ付与で未分類一覧から動的に抜ける()
    {
        // CAD 確定挙動: 未分類画像にタグを付ければその場で一覧から抜ける(分類進捗が見える)
        var vm = await NewViewVmAsync();
        ResolveCommand(vm, "SetDisplayModeUnclassifiedCommand").Execute(null); // 是正前: 不在で不合格
        Assert.Equal(["root.jpg"], ImageNames(vm));

        var tagService = new TagService(_db.Tags);
        var parent = (await _db.Tags.GetAllAsync()).Single(t => t.Name == "親");
        var rootImg = (await _db.Images.GetAllNormalAsync()).Single(r => r.FileName == "root.jpg");
        Assert.True((await tagService.TagImageAsync(rootImg.Id, parent.Id, null)).IsSuccess);

        // 再読込(アプリ再表示相当・同一 settings=未分類モードのまま): root.jpg が親ノードへ分類された
        // → root の未分類は 0 件+専用空状態(汎用の「画像がありません」ではない)
        var vm2 = NewVm(new AppSettings { ViewDisplayModes = { [_view.Id] = "unclassified" } });
        await vm2.InitializeAsync(_col.Id);
        await vm2.SelectAxisCommand.ExecuteAsync(_view.Id);
        Assert.Empty(ImageNames(vm2));
        Assert.True(GetBool(vm2, "ShowUnclassifiedEmpty"),
            "未分類 0 件の専用空状態(CAD: 未分類の画像はありません)が立たない");
        Assert.False(vm2.ShowEmptyMessage, "汎用空状態が専用空状態と二重表示される");
    }

    [Fact]
    public async Task ビュー毎の最終選択モードが記憶され復元される()
    {
        // 裁定①(案B改): ビュー毎の最終選択モードを settings.json(REQ-052 基盤)へ記憶。DB/パッケージ不変。
        var settings = new AppSettings();
        var vm = await NewViewVmAsync(settings);
        ResolveCommand(vm, "SetDisplayModeUnclassifiedCommand").Execute(null); // 是正前: 不在で不合格
        Assert.Equal(["root.jpg"], ImageNames(vm));

        // 同じ settings で新 VM(=アプリ再起動相当)→ 当該ビューは未分類のまま復元
        var vm2 = NewVm(settings);
        await vm2.InitializeAsync(_col.Id);
        await vm2.SelectAxisCommand.ExecuteAsync(_view.Id);
        Assert.Equal(["root.jpg"], ImageNames(vm2));

        // すべてへ戻して再起動相当 → すべてで復元(既定へ退行しない・往復)
        ResolveCommand(vm2, "SetDisplayModeAllCommand").Execute(null);
        var vm3 = NewVm(settings);
        await vm3.InitializeAsync(_col.Id);
        await vm3.SelectAxisCommand.ExecuteAsync(_view.Id);
        Assert.Equal(["c1.jpg", "c2.jpg", "root.jpg"], ImageNames(vm3));
    }

    [Fact]
    public async Task トグルはビュー軸のみ表示されFS軸では出ない()
    {
        // CAD: セグメントは表示軸セレクタ直後・ビュー軸選択時のみ。
        // VM 構築(Recompute=ChipVM の SolidColorBrush 生成)は UI スレッド内で行う — worker スレッド
        // 生成の Brush を compositor が参照すると VerifyAccess 死(本クラスで実測・タグ色チップを
        // headless 描画する初のクラス。既存描画テストはタグなし fixture のため潜伏していた)。
        await SeedAsync();
        await HeadlessApp.Session.Dispatch(async () =>
        {
            var vm = await BuildViewVmAsync();
            var window = new Window { Content = new ImageTabView { DataContext = vm }, Width = 1366, Height = 900 };
            window.Show();
            RunJobs();

            var segment = window.GetVisualDescendants().OfType<Border>()
                .FirstOrDefault(b => b.Name == "DisplayModeSegment");
            Assert.True(segment is not null, "DisplayModeSegment が存在しない(ECO-084 プローブ: トグル UI 不在)");
            Assert.True(segment!.IsVisible, "ビュー軸でトグルが表示されない");

            // FS 軸へ切替(fs 分岐は同期完了)→ 同一 Window でセグメントが消える(IsVisible バインド)
            var switchTask = vm.SelectAxisCommand.ExecuteAsync("fs");
            Assert.True(switchTask.IsCompleted);
            RunJobs();
            Assert.False(segment.IsVisible, "FS 軸でトグルが表示されている(CAD: ビュー軸のみ)");

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task セグメントのアクティブ視覚はトグル操作に追随する()
    {
        // VC-IMG-1(CAD visualContract): アクティブ側=白地+青文字は segBtn.active 既存スタイル
        // (グリッド/リスト切替と同一部品言語=golden 承認済)に委譲するため、ここでは
        // active クラスの付け替え(視覚状態の切替)そのものを実レイアウトで pin する。
        await SeedAsync();
        await HeadlessApp.Session.Dispatch(async () =>
        {
            var vm = await BuildViewVmAsync();
            var window = new Window { Content = new ImageTabView { DataContext = vm }, Width = 1366, Height = 900 };
            window.Show();
            RunJobs();

            var all = window.GetVisualDescendants().OfType<Button>().First(b => b.Name == "DisplayModeAllButton");
            var unc = window.GetVisualDescendants().OfType<Button>().First(b => b.Name == "DisplayModeUncButton");
            Assert.True(all.Classes.Contains("active") && !unc.Classes.Contains("active"), "既定はすべてがアクティブ");

            vm.SetDisplayModeUnclassifiedCommand.Execute(null);
            RunJobs();
            Assert.True(!all.Classes.Contains("active") && unc.Classes.Contains("active"), "未分類切替で active が移らない");

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task 狭幅でもセグメントは可視のままコントロールが重ならない()
    {
        // gate① 承認時指摘(CAD 収納契約): セグメントは収納対象外(畳まない・非表示にしない)。
        // 重なり・潜り込みの禁止(ECO-027 契約①)をビュー軸+セグメント表示=最混雑状態の狭幅で実測する。
        await SeedAsync();
        await HeadlessApp.Session.Dispatch(async () =>
        {
            var vm = await BuildViewVmAsync(); // UI スレッド内で構築(Brush スレッドアフィニティ)
            var window = new Window { Content = new ImageTabView { DataContext = vm }, Width = 760, Height = 900 };
            window.Show();
            RunJobs();

            var segment = window.GetVisualDescendants().OfType<Border>()
                .FirstOrDefault(b => b.Name == "DisplayModeSegment");
            Assert.True(segment is not null && segment.IsVisible && segment.Bounds.Width > 0,
                "狭幅(760px)でセグメントが消えた/潰れた(CAD: 収納対象外)");

            // セグメント内の両ラベルが描画幅を持つ(ラベル畳み禁止)
            var labels = segment!.GetVisualDescendants().OfType<TextBlock>().ToList();
            Assert.Equal(2, labels.Count);
            Assert.All(labels, t => Assert.True(t.Bounds.Width > 0, $"ラベル「{t.Text}」が畳まれた"));

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    // ---- ヘルパ ----

    private static void RunJobs()
    {
        for (var i = 0; i < 8; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static ICommand ResolveCommand(object vm, string name)
    {
        var prop = vm.GetType().GetProperty(name);
        Assert.True(prop is not null, $"{name} が存在しない(表示モード導線の不在= ECO-084 プローブ)");
        return (ICommand)prop!.GetValue(vm)!;
    }

    private static bool GetBool(object vm, string name)
    {
        var prop = vm.GetType().GetProperty(name);
        Assert.True(prop is not null, $"{name} が存在しない(ECO-084 プローブ)");
        return (bool)prop!.GetValue(vm)!;
    }

    private static List<string> ImageNames(ImageTabViewModel vm)
        => vm.Items.Where(i => !i.IsFolder).Select(i => i.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();

    /// <summary>fixture: root.jpg=タグなし / c1.jpg=親 / c2.jpg=親+子。ビュー階層= 親 > 子。ビュー軸選択済み。</summary>
    private async Task<ImageTabViewModel> NewViewVmAsync(AppSettings? settings = null)
    {
        // 共有 Headless セッションの先行初期化: プラットフォーム初期化前に VM が Dispatcher.UIThread へ
        // 触れると、初回 EnsureSharedApplication が VerifyAccess で死ぬ(クラス単独実行で実測・
        // ECO-083 の FailFast 監視が顕在化)。フル run では他クラスが先に温めるため潜伏する —
        // 順序を決定的にするためここで必ずセッションを先に立ち上げる。
        await HeadlessApp.Session.Dispatch(() => true, CancellationToken.None);
        await SeedAsync();
        return await BuildViewVmAsync(settings);
    }

    /// <summary>DB のみの seed(Avalonia オブジェクト非生成= どのスレッドでも安全)。</summary>
    private async Task SeedAsync()
    {
        _col = new SyncFolder { Id = IdGenerator.NewId(), Name = "C", Path = @"C:\col-084" };
        await _db.Folders.AddAsync(_col);
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in new[] { "root.jpg", "c1.jpg", "c2.jpg" })
        {
            var id = IdGenerator.NewId();
            ids[name] = id;
            await _db.Images.AddAsync(new ImageRecord
            {
                Id = id, SyncFolderId = _col.Id, RelativePath = name, FileName = name,
                FileSize = 1, Hash = "H" + name, Status = ImageStatus.Normal,
                CreatedDate = "2026-07-14T00:00:00.000Z", ModifiedDate = "2026-07-14T00:00:00.000Z",
            });
        }

        var tagService = new TagService(_db.Tags);
        var parentTag = (await tagService.CreateAsync("親", TagType.Simple)).Value!;
        var childTag = (await tagService.CreateAsync("子", TagType.Simple)).Value!;
        Assert.True((await tagService.TagImageAsync(ids["c1.jpg"], parentTag.Id, null)).IsSuccess);
        Assert.True((await tagService.TagImageAsync(ids["c2.jpg"], parentTag.Id, null)).IsSuccess);
        Assert.True((await tagService.TagImageAsync(ids["c2.jpg"], childTag.Id, null)).IsSuccess);

        var viewService = new ViewService(_db.Views, _db.Clock);
        _view = (await viewService.CreateAsync("V084")).Value!;
        var parentNode = new HierarchyNode { Id = IdGenerator.NewId(), ViewId = _view.Id, TagId = parentTag.Id, Position = 0 };
        var childNode = new HierarchyNode { Id = IdGenerator.NewId(), ViewId = _view.Id, TagId = childTag.Id, ParentId = parentNode.Id, Position = 0 };
        Assert.True((await viewService.SaveHierarchyAsync(_view.Id, [parentNode, childNode], null)).IsSuccess);
    }

    /// <summary>VM 構築+ビュー軸選択(ChipVM の Brush 生成を伴う — 描画するテストは UI スレッド内で呼ぶ)。</summary>
    private async Task<ImageTabViewModel> BuildViewVmAsync(AppSettings? settings = null)
    {
        var vm = NewVm(settings ?? new AppSettings());
        await vm.InitializeAsync(_col.Id);
        await vm.SelectAxisCommand.ExecuteAsync(_view.Id);
        Assert.True(vm.IsViewAxis);
        return vm;
    }

    private ImageTabViewModel NewVm(AppSettings settings) => TestImageTab.NewVm(_db, settings);
}
