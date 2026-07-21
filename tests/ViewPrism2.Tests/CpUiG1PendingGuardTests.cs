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
/// CP-UI-G1(ECO-129 R8 所見2/3): pending はモード操作(選択・整理・作業追加・ゴミ箱移動)の
/// 対象外=操作は裁定ダイアログのみ(§2.11.7)。v5.0 で pending が既定一覧へ並置されたことにより、
/// 「選択できるのに実行段で黙殺される」(TrashService の normal 限定拒否が Result 捨てで不可視)穴を
/// 選択段で塞ぐ。閲覧(ダブルクリックのビューア)は許可=裁定判断の助け。
/// </summary>
[Trait("cp", "CP-UI-G1")]
public sealed class CpUiG1PendingGuardTests : IDisposable
{
    private readonly TempDb _db = new();
    private SyncFolder _col = null!;

    public void Dispose() => _db.Dispose();

    private async Task<ImageTabViewModel> NewAsync()
    {
        _col = new SyncFolder { Id = IdGenerator.NewId(), Name = "C", Path = @"C:\col" };
        await _db.Folders.AddAsync(_col);

        async Task SeedAsync(string name, ImageStatus status, PendingOrigin? origin = null)
            => await _db.Images.AddAsync(new ImageRecord
            {
                Id = IdGenerator.NewId(),
                SyncFolderId = _col.Id,
                RelativePath = name,
                FileName = name,
                FileSize = 10,
                Hash = new string('0', 64),
                Status = status,
                PendingOrigin = origin,
                CreatedDate = "2026-06-11T00:00:00.000Z",
                ModifiedDate = "2026-06-11T00:00:00.000Z",
            });

        await SeedAsync("a.jpg", ImageStatus.Normal);
        await SeedAsync("p.jpg", ImageStatus.Pending, PendingOrigin.Changed);

        var vm = new ImageTabViewModel(
            _db.Folders, _db.Images, _db.Tags, new ImageSorter(),
            new ViewService(_db.Views, _db.Clock), new NodeGraphBuilder(),
            new PathConditionConverter(), new ConditionEvaluator(),
            new SimilaritySearchService(_db.Folders, _db.Images, _db.Features, _db.Similarities, new FakePHashImageReader(), _db.Clock),
            new MergeService(_db.Images, _db.Tags, _db.Merges),
            new TrashService(_db.Images, _db.Folders, new FilePresenceProbe()),
            new NullWindows(), new AppSettings(), new WorkspaceService(_db.Workspaces, _db.Clock), TestLoc.Ja());
        await vm.InitializeAsync(_col.Id);
        return vm;
    }

    private static ImageItemVM Item(ImageTabViewModel vm, string name)
        => vm.Items.Single(i => !i.IsFolder && i.Name == name);

    [Fact]
    public async Task pendingは無絞り込みFSブラウズにバッジつきで並置される()
    {
        var vm = await NewAsync();
        var pending = Item(vm, "p.jpg");
        Assert.True(pending.IsPending);
        Assert.False(Item(vm, "a.jpg").IsPending);
        Assert.Equal(1, vm.PendingCount);
        Assert.True(vm.HasPending);
    }

    [Fact]
    public async Task 削除モードでpendingは選択対象外_クリックしても選択されない()
    {
        var vm = await NewAsync();
        vm.EnterDeleteCommand.Execute(null);

        var pending = Item(vm, "p.jpg");
        Assert.False(pending.Selectable);                 // 視覚: チェック枠を出さない
        vm.HandleItemClick(pending, ctrl: false, shift: false);
        Assert.False(pending.IsSelected);                 // 操作: 選択されない(黙殺の穴を選択段で塞ぐ)

        var normal = Item(vm, "a.jpg");
        Assert.True(normal.Selectable);
        vm.HandleItemClick(normal, ctrl: false, shift: false);
        Assert.True(normal.IsSelected);                   // normal は従来どおり
    }

    [Fact]
    public async Task タグ編集と作業モードでもpendingは選択対象外()
    {
        var vm = await NewAsync();
        vm.ToggleEditCommand.Execute(null);
        var pending = Item(vm, "p.jpg");
        Assert.False(pending.Selectable);
        vm.HandleItemClick(pending, ctrl: false, shift: false);
        Assert.False(pending.IsSelected);
        vm.ToggleEditCommand.Execute(null);

        vm.ToggleWorkCommand.Execute(null);
        pending = Item(vm, "p.jpg");
        Assert.False(pending.Selectable);
        vm.HandleItemClick(pending, ctrl: false, shift: false);
        Assert.False(pending.IsSelected);
    }

    [Fact]
    public async Task 整理モードでpendingはマージ先にも整理対象にもならない()
    {
        var vm = await NewAsync();
        vm.ToggleOrganizeCommand.Execute(null);
        var pending = Item(vm, "p.jpg");
        vm.HandleItemClick(pending, ctrl: false, shift: false); // マージ先設定の試み
        Assert.Null(vm.Organize.MergeTargetId);                  // pending はマージ先にしない
    }

    private sealed class NullWindows : IWindowService
    {
        public Task<bool> ConfirmAsync(string title, string message, string confirmLabel,
            bool destructive = false, string? cancelLabel = null) => Task.FromResult(true);

        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);

        public Task ShowFolderManagementAsync() => Task.CompletedTask;

        public Task ShowSettingsAsync() => Task.CompletedTask;

        public Task ShowSnapshotsAsync() => Task.CompletedTask;

        public Task<bool> ShowTagEditorAsync(Tag? existing) => Task.FromResult(false);

        public Task<bool> ShowViewEditDialogAsync(View? existing) => Task.FromResult(false);

        public Task<IReadOnlyList<string>?> ShowNumericValueDialogAsync(
            Tag tag, NumericTagSettings? settings, int imageCount)
            => Task.FromResult<IReadOnlyList<string>?>(null);

        public Task<NodeConditionResult?> ShowNodeConditionDialogAsync(
            Tag tag, HierarchyConditionType? conditionType, string? conditionValueJson)
            => Task.FromResult<NodeConditionResult?>(null);

        public Task ShowRelinkAsync(string folderId) => Task.CompletedTask;

        public void ShowViewer(IReadOnlyList<ImageEntry> ordered, int startIndex)
        {
        }
    }
}
