using ViewPrism2.App.Services;
using ViewPrism2.App.ViewModels;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using ViewPrism2.Core.Services.Repair;
using ViewPrism2.Core.Services.Similarity;
using ViewPrism2.Infrastructure.Imaging;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-UI-G1(unit 部分・ECO-114): モード開始/終了は母集合パイプライン(全件評価/ソート/
/// Items 再構築)を通らない — 母集合はモード遷移で不変のため、在庫 Items のモード依存フラグ
/// だけをその場更新する(ECO-026/#2「切替で Items を作り直さない」のモード遷移版)。
/// 構造 probe= Items/Chips の**インスタンス同一性**(ECO-058 方式=固定時間閾値なし。
/// 再構築されていないことの構造証拠)+フラグ/マーカー遷移の意味論保持。
/// </summary>
[Trait("cp", "CP-UI-G1")]
public sealed class CpUiG1ModeTransitionTests : IDisposable
{
    private readonly TempDb _db = new();
    private SyncFolder _col = null!;

    public void Dispose() => _db.Dispose();

    /// <summary>a.jpg にシンプルタグを 1 つ付与した状態で VM を作る(タグドット遷移の検証用)。</summary>
    private async Task<ImageTabViewModel> NewWithTaggedImagesAsync(params string[] names)
    {
        _col = new SyncFolder { Id = IdGenerator.NewId(), Name = "C", Path = @"C:\col" };
        await _db.Folders.AddAsync(_col);
        foreach (var name in names)
        {
            await _db.Images.AddAsync(new ImageRecord
            {
                Id = IdGenerator.NewId(),
                SyncFolderId = _col.Id,
                RelativePath = name,
                FileName = name,
                FileSize = 10,
                Hash = new string('0', 64),
                Status = ImageStatus.Normal,
                CreatedDate = "2026-06-11T00:00:00.000Z",
                ModifiedDate = "2026-06-11T00:00:00.000Z",
            });
        }
        var tagService = new TagService(_db.Tags);
        var tag = (await tagService.CreateAsync("印", TagType.Simple, color: "#30a46c")).Value!;
        var first = (await _db.Images.GetAllNormalAsync()).Single(r => r.FileName == names[0]);
        Assert.True((await tagService.TagImageAsync(first.Id, tag.Id, null)).IsSuccess);

        var vm = TestImageTab.NewVm(_db);
        await vm.InitializeAsync(_col.Id);
        return vm;
    }

    private static ImageItemVM Item(ImageTabViewModel vm, string name)
        => vm.Items.Single(i => !i.IsFolder && i.Name == name);

    [Fact]
    public async Task モード開始と終了はItemsを再構築しない()
    {
        var vm = await NewWithTaggedImagesAsync("a.jpg", "b.jpg");
        var before = Item(vm, "a.jpg");
        Assert.False(before.Selectable);
        Assert.True(before.HasTagDots); // 閲覧時はタグドット表示

        vm.ToggleEditCommand.Execute(null); // タグ編集開始

        var during = Item(vm, "a.jpg");
        Assert.Same(before, during); // ECO-114: 母集合不変=同一インスタンスのその場更新(再構築しない)
        Assert.True(during.Selectable);
        Assert.False(during.HasTagDots); // 選択モード中はドット非表示(既存視覚契約の維持)

        vm.ToggleEditCommand.Execute(null); // 終了

        var after = Item(vm, "a.jpg");
        Assert.Same(before, after);
        Assert.False(after.Selectable);
        Assert.True(after.HasTagDots); // 離脱後にドットが復元される(モード中構築アイテムの欠落防止込み)
    }

    [Fact]
    public async Task ファイル操作モードの遷移も再構築せず白チェックフラグが切り替わる()
    {
        var vm = await NewWithTaggedImagesAsync("a.jpg", "b.jpg");
        var before = Item(vm, "a.jpg");

        vm.EnterFileOpsCommand.Execute(null);
        var during = Item(vm, "a.jpg");
        Assert.Same(before, during);
        Assert.True(during.Selectable);
        Assert.True(during.IsPlainCheck); // VC-IMG-13: 番号なし白✓モード

        vm.HandleItemClick(during, ctrl: false, shift: false);
        Assert.True(during.ShowPlainCheck);

        vm.ExitFileOpsCommand.Execute(null);
        Assert.Same(before, Item(vm, "a.jpg"));
        Assert.False(before.IsPlainCheck);
        Assert.False(before.IsSelected); // 終了で選択解除(CAD 契約の維持)
    }

    [Fact]
    public async Task モード遷移でチップと件数は再計算されない()
    {
        var vm = await NewWithTaggedImagesAsync("a.jpg", "b.jpg");
        Assert.True(vm.Chips.Count > 0); // FS 軸: タグ付き画像がありチップ行が出ている
        var chipBefore = vm.Chips[0];
        var countBefore = vm.CountLabel;

        vm.ToggleEditCommand.Execute(null);

        Assert.Same(chipBefore, vm.Chips.Count > 0 ? vm.Chips[0] : null); // ECO-114: チップ再構築なし
        Assert.Equal(countBefore, vm.CountLabel);

        vm.ToggleEditCommand.Execute(null);
        Assert.Same(chipBefore, vm.Chips.Count > 0 ? vm.Chips[0] : null);
    }

    [Fact]
    public async Task 選択中のモード離脱は同一インスタンスのまま選択マーカーを消す()
    {
        var vm = await NewWithTaggedImagesAsync("a.jpg", "b.jpg");
        vm.ToggleEditCommand.Execute(null);
        var item = Item(vm, "a.jpg");
        vm.HandleItemClick(item, ctrl: false, shift: false);
        Assert.True(item.IsSelected);
        Assert.Equal("1", item.SelectionOrderText);

        vm.ToggleWorkCommand.Execute(null); // 別モードへの直接遷移(排他)=選択クリア

        Assert.Same(item, Item(vm, "a.jpg"));
        Assert.False(item.IsSelected);
        Assert.Equal("", item.SelectionOrderText);
        Assert.True(item.Selectable); // 作業モードも選択系
    }
    /// <summary>ECO-115: タグ編集パネルの操作(タブ切替・行展開)はパネル状態しか変えないため
    /// Items を再構築しない(構造 probe=インスタンス同一性・ECO-114 と同型)。パネル意味論
    /// (展開で NumCells が出る・タブ復帰)の維持も併せて固定する。</summary>
    [Fact]
    public async Task パネルのタブ切替と行展開はItemsを再構築しない()
    {
        _ = await NewWithTaggedImagesAsync("a.jpg", "b.jpg"); // コレクション+画像+シンプルタグのシード
        // 展開検証用の数値タグ(定義域つき=展開で NumCells が構築される)。VM のタグカタログは
        // 初期化時ロードのため、タグを揃えてから検査用 VM を作り直す
        var tagService = new TagService(_db.Tags);
        var rating = (await tagService.CreateAsync("評価", TagType.Numeric, color: "#e8b931")).Value!;
        Assert.True((await tagService.SetNumericSettingsAsync(rating.Id, 1, 5, 1, "★")).IsSuccess);
        var vm = TestImageTab.NewVm(_db);
        await vm.InitializeAsync(_col.Id);

        vm.ToggleEditCommand.Execute(null);
        var item = Item(vm, "a.jpg");
        vm.HandleItemClick(item, ctrl: false, shift: false); // 選択=タグ追加パネルが構築される

        var before = Item(vm, "a.jpg");
        var chipBefore = vm.Chips.Count > 0 ? vm.Chips[0] : null;
        await vm.TabAddCommand.ExecuteAsync(null);
        Assert.Same(before, Item(vm, "a.jpg")); // ECO-115: タブ切替はパネル状態のみ=再構築しない
        Assert.Same(chipBefore, vm.Chips.Count > 0 ? vm.Chips[0] : null); // チップも不変(invariant 文言との整合)
        Assert.True(vm.OnAddTab);

        var row = vm.AddGroups.SelectMany(g => g.Tags).Single(r => r.Id == rating.Id);
        await vm.ClickAddRowCommand.ExecuteAsync(row); // 展開
        Assert.Same(before, Item(vm, "a.jpg"));
        var expanded = vm.AddGroups.SelectMany(g => g.Tags).Single(r => r.Id == rating.Id);
        Assert.True(expanded.Expanded);
        Assert.NotEmpty(expanded.NumCells); // 定義域 1..5=展開の意味論維持

        await vm.ClickAddRowCommand.ExecuteAsync(expanded); // 畳み
        Assert.Same(before, Item(vm, "a.jpg"));
        Assert.False(vm.AddGroups.SelectMany(g => g.Tags).Single(r => r.Id == rating.Id).Expanded);

        vm.TabCurrentCommand.Execute(null);
        Assert.Same(before, Item(vm, "a.jpg"));
        Assert.True(vm.OnCurrentTab);
    }

    /// <summary>
    /// ECO-124(26 万件経路 7 例目): 左ペイン(コレクション)開閉はサイドバー状態
    /// (Collapsed/Expanded/SidebarWidth)しか変えない=母集合パイプラインを通らない
    /// (WorkTab ToggleSidebar=通知のみ、と対称)。往復とも検査。
    /// </summary>
    [Fact]
    public async Task サイドバー開閉はItemsもチップも再構築しない()
    {
        var vm = await NewWithTaggedImagesAsync("a.jpg", "b.jpg");
        var before = Item(vm, "a.jpg");
        Assert.True(vm.Chips.Count > 0);
        var chipBefore = vm.Chips[0];
        Assert.True(vm.Expanded);

        vm.ToggleSidebarCommand.Execute(null); // 折り畳み(276→64)

        Assert.True(vm.Collapsed);
        Assert.Equal(64, vm.SidebarWidth);
        Assert.Same(before, Item(vm, "a.jpg")); // ECO-124: 母集合不変=再構築しない
        Assert.Same(chipBefore, vm.Chips.Count > 0 ? vm.Chips[0] : null);

        vm.ToggleSidebarCommand.Execute(null); // 展開(64→276)

        Assert.True(vm.Expanded);
        Assert.Equal(276, vm.SidebarWidth);
        Assert.Same(before, Item(vm, "a.jpg"));
        Assert.Same(chipBefore, vm.Chips.Count > 0 ? vm.Chips[0] : null);
    }
}
