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
/// CP-UI-G1(unit 部分・ECO-112): 画像タブ「ファイル操作」モード=⋯メニュー「ファイル操作」で入る
/// 第5の排他文脈モード(参照系・右ペインなし)。CAD= ViewPrismUI docs/screens/image_tab.md
/// 「ファイル操作モード(2026-07-19)」節+IMG-026 裁定(2026-07-19: 1-a/2-b/3-a/4-a)を回帰固定:
/// 開始=他モード解除+選択クリア+右ペインなし / 選択=inSelect 再利用・番号バッジなし(VC-IMG-13) /
/// ボタン出し分け 0/1/2件(VC-IMG-12) / パスをコピー=表示順・OSネイティブ改行・末尾改行なし(IMG-026①) /
/// フィードバック=ボタン内一時表示+解除遷移(IMG-026②) / 場所を開く=1件時のみ(CAD 確定)。
/// </summary>
[Trait("cp", "CP-UI-G1")]
public sealed class CpUiG1FileOpsModeTests : IDisposable
{
    private readonly TempDb _db = new();
    private SyncFolder _col = null!;
    private readonly FakeFileOps _fileOps = new();

    public void Dispose() => _db.Dispose();

    /// <summary>クリップボード/OS reveal のフェイク(呼び出し記録のみ・実 OS へ出さない)。</summary>
    private sealed class FakeFileOps : IFileOperationsService
    {
        public List<string> CopiedTexts { get; } = new();
        public List<string> RevealedPaths { get; } = new();
        public Task CopyTextAsync(string text) { CopiedTexts.Add(text); return Task.CompletedTask; }
        public void RevealInFileManager(string absolutePath) => RevealedPaths.Add(absolutePath);
    }

    private sealed class StubWindowService : IWindowService
    {
        public Task<bool> ConfirmAsync(string title, string message, string confirmLabel, bool destructive = false, string? cancelLabel = null) => Task.FromResult(true);
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
        public Task<bool> ShowMergeAsync(ImageEntry target, IReadOnlyList<ImageEntry> sources) => Task.FromResult(false);
        public Task ShowTrashAsync(string collectionId) => Task.CompletedTask;
    }

    private async Task<ImageTabViewModel> NewWithImagesAsync(params string[] names)
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
        var vm = new ImageTabViewModel(
            _db.Folders, _db.Images, _db.Tags, new ImageSorter(),
            new ViewService(_db.Views, _db.Clock), new NodeGraphBuilder(),
            new PathConditionConverter(), new ConditionEvaluator(),
            new SimilaritySearchService(_db.Folders, _db.Images, _db.Features, _db.Similarities, new FakePHashImageReader(), _db.Clock),
            new MergeService(_db.Images, _db.Tags, _db.Merges),
            new TrashService(_db.Images, _db.Folders, new FilePresenceProbe()),
            new StubWindowService(), new AppSettings(),
            new WorkspaceService(_db.Workspaces, _db.Clock), TestLoc.Ja(),
            scanCoordinator: null, fileOps: _fileOps);
        await vm.InitializeAsync(_col.Id);
        return vm;
    }

    private static ImageItemVM Item(ImageTabViewModel vm, string name)
        => vm.Items.Single(i => !i.IsFolder && i.Name == name);

    [Fact]
    public async Task ファイル操作メニューは他モードを解除し選択をクリアして開始する()
    {
        var vm = await NewWithImagesAsync("a.jpg", "b.jpg");
        vm.ToggleEditCommand.Execute(null);
        vm.HandleItemClick(Item(vm, "a.jpg"), ctrl: false, shift: false);
        Assert.True(vm.HasSelection);

        vm.EnterFileOpsCommand.Execute(null);

        Assert.True(vm.FileOpsMode);
        Assert.False(vm.EditMode);
        Assert.False(vm.WorkMode);
        Assert.False(vm.OrganizeMode);
        Assert.False(vm.DeleteMode);
        Assert.False(vm.HasSelection);   // 選択クリア
        Assert.False(vm.MoreMenuOpen);   // メニューを閉じる
        Assert.False(vm.ShowRightPane);  // 右ペインは開かない(CAD)
    }

    [Fact]
    public async Task ファイル操作モードのクリックは選択を有効化し番号バッジを出さない()
    {
        var vm = await NewWithImagesAsync("a.jpg", "b.jpg");
        vm.EnterFileOpsCommand.Execute(null);
        Assert.True(Item(vm, "a.jpg").Selectable); // inSelect=ファイル操作

        vm.HandleItemClick(Item(vm, "a.jpg"), ctrl: false, shift: false);
        var item = Item(vm, "a.jpg");
        Assert.True(item.IsSelected);
        // VC-IMG-13: 選択順の番号バッジは出さない=白✓(PlainCheck)
        Assert.Equal("", item.SelectionOrderText);
        Assert.True(item.ShowPlainCheck);

        // 退行ガード: タグ編集モードでは従来どおり連番バッジ(REQ-041 CR-3)
        vm.ExitFileOpsCommand.Execute(null);
        vm.ToggleEditCommand.Execute(null);
        vm.HandleItemClick(Item(vm, "a.jpg"), ctrl: false, shift: false);
        var editItem = Item(vm, "a.jpg");
        Assert.Equal("1", editItem.SelectionOrderText);
        Assert.False(editItem.ShowPlainCheck);
    }

    [Fact]
    public async Task ボタン出し分けは選択件数で転移する()
    {
        var vm = await NewWithImagesAsync("a.jpg", "b.jpg");
        vm.EnterFileOpsCommand.Execute(null);

        // 0 件: 終了のみ(VC-IMG-12①)
        Assert.False(vm.ShowCopyPaths);
        Assert.False(vm.ShowOpenLocation);

        // 1 件: パスをコピー(バッジ1)+ファイルの場所を開く(VC-IMG-12②)
        vm.HandleItemClick(Item(vm, "a.jpg"), ctrl: false, shift: false);
        Assert.True(vm.ShowCopyPaths);
        Assert.True(vm.ShowOpenLocation);
        Assert.Equal(1, vm.FileOpsSelCount);

        // 2 件以上: パスをコピー(バッジ=N)のみ(VC-IMG-12③)
        vm.HandleItemClick(Item(vm, "b.jpg"), ctrl: true, shift: false);
        Assert.True(vm.ShowCopyPaths);
        Assert.False(vm.ShowOpenLocation);
        Assert.Equal(2, vm.FileOpsSelCount);
    }

    [Fact]
    public async Task パスをコピーは表示順の絶対パスを1行1ファイル末尾改行なしで送る()
    {
        var vm = await NewWithImagesAsync("a.jpg", "b.jpg", "c.jpg");
        vm.EnterFileOpsCommand.Execute(null);
        // 選択順は b→a(逆順)。IMG-026① 裁定=コピーは表示順(未ソート既定=名前昇順)で a→b になること
        vm.HandleItemClick(Item(vm, "b.jpg"), ctrl: false, shift: false);
        vm.HandleItemClick(Item(vm, "a.jpg"), ctrl: true, shift: false);

        _ = vm.CopyPathsCommand.ExecuteAsync(null);

        var text = Assert.Single(_fileOps.CopiedTexts);
        Assert.Equal(@"C:\col\a.jpg" + Environment.NewLine + @"C:\col\b.jpg", text); // OS ネイティブ改行・末尾改行なし
    }

    [Fact]
    public async Task コピー完了フィードバックは一時表示され解除遷移で消える()
    {
        var vm = await NewWithImagesAsync("a.jpg", "b.jpg");
        vm.EnterFileOpsCommand.Execute(null);
        vm.HandleItemClick(Item(vm, "a.jpg"), ctrl: false, shift: false);

        _ = vm.CopyPathsCommand.ExecuteAsync(null);
        Assert.True(vm.CopyFeedbackActive); // コピー直後に「コピーしました ✓」(IMG-026②)

        // 解除遷移(ECO-104 教訓=タイマ以外も全列挙): 選択変化で消える
        vm.HandleItemClick(Item(vm, "b.jpg"), ctrl: true, shift: false);
        Assert.False(vm.CopyFeedbackActive);

        // 再コピー→モード離脱でも消える
        _ = vm.CopyPathsCommand.ExecuteAsync(null);
        Assert.True(vm.CopyFeedbackActive);
        vm.ExitFileOpsCommand.Execute(null);
        Assert.False(vm.CopyFeedbackActive);
    }

    [Fact]
    public async Task サイドバー開閉もコピーフィードバックの解除遷移である()
    {
        // ECO-124 R8 所見 1-1: Recompute→通知のみへの置換で ClearCopyFeedback(Recompute 先頭)が
        // 落ちる= ECO-113/114 は置換時に明示的に残した前例(ECO-104 教訓=解除遷移の全列挙)。
        var vm = await NewWithImagesAsync("a.jpg");
        vm.EnterFileOpsCommand.Execute(null);
        vm.HandleItemClick(Item(vm, "a.jpg"), ctrl: false, shift: false);

        _ = vm.CopyPathsCommand.ExecuteAsync(null);
        Assert.True(vm.CopyFeedbackActive);
        vm.ToggleSidebarCommand.Execute(null);
        Assert.False(vm.CopyFeedbackActive);
    }

    [Fact]
    public async Task フィードバック表示中もコピーボタンは活性のまま()
    {
        // R8 所見 2-1(2026-07-19): タイマをコマンド本体に置くと AsyncRelayCommand の並行実行禁止で
        // 表示中 2 秒間 CanExecute=false → :disabled 視覚でフィードバック文言がグレー化する。
        // 契約(IMG-026②)は「ボタン内一時表示」であり無効化ではない。
        var vm = await NewWithImagesAsync("a.jpg");
        vm.EnterFileOpsCommand.Execute(null);
        vm.HandleItemClick(Item(vm, "a.jpg"), ctrl: false, shift: false);

        _ = vm.CopyPathsCommand.ExecuteAsync(null);
        Assert.True(vm.CopyFeedbackActive);
        Assert.True(vm.CopyPathsCommand.CanExecute(null),
            "フィードバック表示中にコピーボタンが disabled になっている(タイマがコマンド実行を占有)");
    }

    [Fact]
    public async Task フィードバックはタイマ満了で自動復帰する()
    {
        // R8 所見 7-1(2026-07-19): 解除遷移側だけでなくタイマ満了経路(2 秒→自動復帰)も固定する。
        var vm = await NewWithImagesAsync("a.jpg");
        vm.CopyFeedbackDuration = TimeSpan.FromMilliseconds(1);
        vm.EnterFileOpsCommand.Execute(null);
        vm.HandleItemClick(Item(vm, "a.jpg"), ctrl: false, shift: false);

        await vm.CopyPathsCommand.ExecuteAsync(null);
        // 満了は fire-and-forget タスク側で起きる。短時間ポーリングで復帰を待つ(上限 2 秒=flaky 防止)
        for (var i = 0; i < 200 && vm.CopyFeedbackActive; i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.False(vm.CopyFeedbackActive);
    }

    [Fact]
    public async Task 場所を開くは1件選択時のみ実行する()
    {
        var vm = await NewWithImagesAsync("a.jpg", "b.jpg");
        vm.EnterFileOpsCommand.Execute(null);
        vm.HandleItemClick(Item(vm, "a.jpg"), ctrl: false, shift: false);
        vm.HandleItemClick(Item(vm, "b.jpg"), ctrl: true, shift: false);

        vm.OpenFileLocationCommand.Execute(null); // 2 件=無操作(表示もされない=VC-IMG-12③)
        Assert.Empty(_fileOps.RevealedPaths);

        vm.HandleItemClick(Item(vm, "b.jpg"), ctrl: true, shift: false); // b を外して 1 件へ
        vm.OpenFileLocationCommand.Execute(null);
        Assert.Equal(@"C:\col\a.jpg", Assert.Single(_fileOps.RevealedPaths));
    }

    [Fact]
    public async Task ツールバーはファイル操作中に全モード入口とその他を隠す()
    {
        var vm = await NewWithImagesAsync("a.jpg");

        Assert.False(vm.InAnyMode); // browse

        vm.EnterFileOpsCommand.Execute(null);

        Assert.True(vm.InAnyMode);          // ⋯ は !InAnyMode で隠れる
        Assert.False(vm.ShowEditEntry);
        Assert.False(vm.ShowOrganizeEntry);
        Assert.False(vm.ShowWorkEntry);

        vm.ExitFileOpsCommand.Execute(null); // 終了で browse へ戻る+選択解除
        Assert.False(vm.FileOpsMode);
        Assert.False(vm.HasSelection);
        Assert.True(vm.ShowEditEntry);
        Assert.True(vm.ShowWorkEntry);
    }

    [Fact]
    public async Task 他モード開始はファイル操作を解除する()
    {
        var vm = await NewWithImagesAsync("a.jpg");
        vm.EnterFileOpsCommand.Execute(null);
        Assert.True(vm.FileOpsMode);

        vm.ToggleEditCommand.Execute(null); // タグ編集開始=排他解除
        Assert.False(vm.FileOpsMode);
        Assert.True(vm.EditMode);

        vm.ToggleEditCommand.Execute(null);
        vm.EnterFileOpsCommand.Execute(null);
        vm.EnterDeleteCommand.Execute(null); // 削除開始=排他解除
        Assert.False(vm.FileOpsMode);
        Assert.True(vm.DeleteMode);
    }
}
