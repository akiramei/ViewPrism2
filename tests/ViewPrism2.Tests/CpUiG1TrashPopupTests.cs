using System.Collections.Concurrent;
using System.Reflection;
using CommunityToolkit.Mvvm.Input;
using ViewPrism2.App.Services;
using ViewPrism2.App.ViewModels;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;
using ViewPrism2.Core.Services;
using ViewPrism2.Core.Services.Repair;
using ViewPrism2.Core.Services.Similarity;
using ViewPrism2.Infrastructure.Imaging;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-UI-G1(unit 部分・ECO-019): ゴミ箱ポップアップ(トラッシュモーダルを画像タブ内オーバーレイへ作り直す)。
/// モック(ViewPrismUI:資料/画像タブ/ViewPrism2 画像タブゴミ箱ポップアップ.html)の挙動を回帰固定:
/// ⋯「ゴミ箱」で deleted 一覧を読み popup を開く / 複数選択・すべて選択 / 復元・完全削除・ゴミ箱を空。
/// 完全削除/空は確認(ConfirmAsync)を要し、INV-009 で物理ファイルは不変(DB 行のみ除去)。
/// </summary>
[Trait("cp", "CP-UI-G1")]
public sealed class CpUiG1TrashPopupTests : IDisposable
{
    private readonly TempDb _db = new();
    private SyncFolder _col = null!;

    public void Dispose() => _db.Dispose();

    private sealed class StubWindowService : IWindowService
    {
        public bool ConfirmResult { get; set; } = true;
        public int ConfirmCalls { get; private set; }
        public Task<bool> ConfirmAsync(string title, string message, string confirmLabel, bool destructive = false, string? cancelLabel = null) { ConfirmCalls++; return Task.FromResult(ConfirmResult); }
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

    /// <summary>物理存在を固定で返すフェイク(復元の存在分岐 T6/T7 制御用)。</summary>
    private sealed class FakeProbe(bool exists) : IFilePresenceProbe
    {
        public bool Exists(string absoluteImagePath) => exists;
    }

    private class RepositorySpy<T> : DispatchProxy where T : class
    {
        public T Inner { get; set; } = null!;
        public ConcurrentQueue<string> Calls { get; } = new();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Assert.NotNull(targetMethod);
            Calls.Enqueue(targetMethod.Name);
            return targetMethod.Invoke(Inner, args);
        }

        public int Count(string methodName) => Calls.Count(name => name == methodName);
    }

    private static T Spy<T>(T inner, out RepositorySpy<T> spy) where T : class
    {
        var proxy = DispatchProxy.Create<T, RepositorySpy<T>>();
        spy = (RepositorySpy<T>)(object)proxy;
        spy.Inner = inner;
        return proxy;
    }

    private StubWindowService _win = null!;

    /// <summary>normal/deleted を混在で投入(deleted は引数 deletedNames で指定)。probe で復元の存在分岐を制御。</summary>
    private async Task<ImageTabViewModel> NewAsync(
        string[] normalNames,
        string[] deletedNames,
        IFilePresenceProbe? probe = null,
        IImageRepository? images = null,
        string[]? missingNames = null)
    {
        _col = new SyncFolder { Id = IdGenerator.NewId(), Name = "C", Path = @"C:\col" };
        await _db.Folders.AddAsync(_col);
        async Task Add(string name, ImageStatus st) => await _db.Images.AddAsync(new ImageRecord
        {
            Id = name, // テスト可読性のため id=name
            SyncFolderId = _col.Id,
            RelativePath = name, FileName = name, FileSize = 10, Hash = new string('0', 64),
            Status = st, CreatedDate = "2026-06-11T00:00:00.000Z", ModifiedDate = "2026-06-11T00:00:00.000Z",
        });
        foreach (var n in normalNames) await Add(n, ImageStatus.Normal);
        foreach (var n in deletedNames) await Add(n, ImageStatus.Deleted);
        foreach (var n in missingNames ?? []) await Add(n, ImageStatus.Missing);

        _win = new StubWindowService();
        images ??= _db.Images;
        var vm = new ImageTabViewModel(
            _db.Folders, images, _db.Tags, new ImageSorter(),
            new ViewService(_db.Views, _db.Clock), new NodeGraphBuilder(),
            new PathConditionConverter(), new ConditionEvaluator(),
            new SimilaritySearchService(_db.Folders, images, _db.Features, _db.Similarities, new FakePHashImageReader(), _db.Clock),
            new MergeService(images, _db.Tags, _db.Merges),
            new TrashService(images, _db.Folders, probe ?? new FilePresenceProbe()),
            _win, new AppSettings(), new WorkspaceService(_db.Workspaces, _db.Clock), TestLoc.Ja());
        await vm.InitializeAsync(_col.Id);
        return vm;
    }

    [Fact]
    public async Task ゴミ箱を開くとdeleted一覧を読み込みポップアップを表示する()
    {
        var vm = await NewAsync(["a.jpg"], ["x.jpg", "y.jpg"]);

        await vm.OpenTrashCommand.ExecuteAsync(null);

        Assert.True(vm.TrashOpen);
        Assert.True(vm.HasTrashItems);
        Assert.Equal(2, vm.TrashPopupCount);
        Assert.Equal(["x.jpg", "y.jpg"], vm.TrashPopupItems.Select(i => i.Name)); // 名前昇順
        Assert.False(vm.HasTrashSel);
        Assert.Equal("画像を選択して操作", vm.TrashSelCountLabel);
    }

    [Fact]
    public async Task ECO098_ゴミ箱はhidden_status全件APIを使わずdeletedだけを取得する()
    {
        var images = Spy<IImageRepository>(_db.Images, out var spy);
        var missing = Enumerable.Range(0, 256).Select(i => $"missing-{i:D3}.jpg").ToArray();
        var vm = await NewAsync(
            ["normal.jpg"], ["z-deleted.jpg", "a-deleted.jpg"],
            images: images, missingNames: missing);

        var other = new SyncFolder { Id = IdGenerator.NewId(), Name = "Other", Path = @"C:\other" };
        await _db.Folders.AddAsync(other);
        await _db.Images.AddAsync(new ImageRecord
        {
            Id = "foreign-deleted.jpg", SyncFolderId = other.Id,
            RelativePath = "foreign-deleted.jpg", FileName = "foreign-deleted.jpg",
            FileSize = 10, Hash = new string('0', 64), Status = ImageStatus.Deleted,
            CreatedDate = "2026-06-11T00:00:00.000Z", ModifiedDate = "2026-06-11T00:00:00.000Z",
        });

        Assert.Equal(0, spy.Count(nameof(IImageRepository.GetByFolderAsync)));
        vm.EnterDeleteCommand.Execute(null);
        vm.HandleItemClick(vm.Items.Single(i => i.Id == "normal.jpg"), ctrl: false, shift: false);
        await vm.DeleteToTrashCommand.ExecuteAsync(null);

        Assert.Equal(0, spy.Count(nameof(IImageRepository.GetByFolderAsync)));
        await vm.OpenTrashCommand.ExecuteAsync(null);

        Assert.Equal(0, spy.Count(nameof(IImageRepository.GetByFolderAsync)));
        Assert.Equal(1, spy.Count(nameof(IImageRepository.GetDeletedByFolderAsync)));
        Assert.Equal(
            ["a-deleted.jpg", "normal.jpg", "z-deleted.jpg"],
            vm.TrashPopupItems.Select(i => i.Name));
    }

    [Fact]
    public async Task 空のゴミ箱は空状態を表示する()
    {
        var vm = await NewAsync(["a.jpg"], []);
        await vm.OpenTrashCommand.ExecuteAsync(null);

        Assert.True(vm.TrashOpen);
        Assert.True(vm.TrashPopupEmpty);
        Assert.False(vm.HasTrashItems);
    }

    [Fact]
    public async Task すべて選択と選択解除がトグルする()
    {
        var vm = await NewAsync([], ["x.jpg", "y.jpg", "z.jpg"]);
        await vm.OpenTrashCommand.ExecuteAsync(null);

        vm.ToggleTrashSelectAllCommand.Execute(null);
        Assert.Equal(3, vm.TrashSelCount);
        Assert.Equal("選択を解除", vm.TrashSelectAllLabel);
        Assert.All(vm.TrashPopupItems, i => Assert.True(i.IsSelected));

        vm.ToggleTrashSelectAllCommand.Execute(null);
        Assert.Equal(0, vm.TrashSelCount);
        Assert.Equal("すべて選択", vm.TrashSelectAllLabel);
    }

    [Fact]
    public async Task 復元は選択をpending化しゴミ箱から外しFSブラウズへバッジ付きで戻す()
    {
        // ECO-128(T6'): 復元は物理存在でも normal へ自動昇格せず pending(origin=Restored)。
        // pending は無絞り込み FS ブラウズにバッジ付きで並置される(INV-010 v5.0)=一覧へは戻る。
        var vm = await NewAsync(["a.jpg"], ["x.jpg", "y.jpg"], new FakeProbe(exists: true));
        await vm.OpenTrashCommand.ExecuteAsync(null);
        vm.ToggleTrashItemCommand.Execute(vm.TrashPopupItems.Single(i => i.Name == "x.jpg"));

        await vm.RestoreSelectedTrashCommand.ExecuteAsync(null);

        // x は復元(物理存在 → pending・origin=Restored)、ゴミ箱からは消える
        Assert.Equal(["y.jpg"], vm.TrashPopupItems.Select(i => i.Name));
        Assert.Equal(1, vm.TrashCount);
        var x = await _db.Images.GetByIdAsync("x.jpg");
        Assert.Equal(ImageStatus.Pending, x!.Status);
        Assert.Equal(PendingOrigin.Restored, x.PendingOrigin);
        // FS ブラウズ(無絞り込み)に未裁定バッジ付きで現れる
        Assert.Contains(vm.Items, i => !i.IsFolder && i.Name == "x.jpg" && i.IsPending);
        Assert.True(vm.HasIntegrityReview);
    }

    [Fact]
    public async Task 復元_物理不在はMissing化しゴミ箱から消える()
    {
        // ECO-051: 撤去した旧 TrashViewModel 検査からの移行(INV-013 幽霊 normal 防止の UI 層観測。
        // 分岐の意味論は Core=TrashService/S-26 が正・ここはポップアップ経由でも Missing に落ちることの配線検査)
        var vm = await NewAsync(["a.jpg"], ["x.jpg"], new FakeProbe(exists: false));
        await vm.OpenTrashCommand.ExecuteAsync(null);
        vm.ToggleTrashItemCommand.Execute(vm.TrashPopupItems.Single(i => i.Name == "x.jpg"));

        await vm.RestoreSelectedTrashCommand.ExecuteAsync(null);

        Assert.Empty(vm.TrashPopupItems); // deleted でなくなった(Missing へ)
        var x = await _db.Images.GetByIdAsync("x.jpg");
        Assert.Equal(ImageStatus.Missing, x!.Status); // 幽霊 normal を作らない(INV-013)
    }

    [Fact]
    public async Task 完全削除は確認を経てDB行を消す_物理は不変_確認却下なら何もしない()
    {
        var vm = await NewAsync([], ["x.jpg", "y.jpg"]);
        await vm.OpenTrashCommand.ExecuteAsync(null);
        vm.ToggleTrashItemCommand.Execute(vm.TrashPopupItems.Single(i => i.Name == "x.jpg"));

        // 確認却下 → 何もしない
        _win.ConfirmResult = false;
        await vm.PurgeSelectedTrashCommand.ExecuteAsync(null);
        Assert.Equal(1, _win.ConfirmCalls);
        Assert.NotNull(await _db.Images.GetByIdAsync("x.jpg"));

        // 確認承認 → DB 行削除(物理は TrashService が触れない=INV-009)
        _win.ConfirmResult = true;
        await vm.PurgeSelectedTrashCommand.ExecuteAsync(null);
        Assert.Null(await _db.Images.GetByIdAsync("x.jpg"));
        Assert.Equal(["y.jpg"], vm.TrashPopupItems.Select(i => i.Name));
    }

    [Fact]
    public async Task ゴミ箱を空は確認を経て全deletedを完全削除する()
    {
        var vm = await NewAsync(["a.jpg"], ["x.jpg", "y.jpg"]);
        await vm.OpenTrashCommand.ExecuteAsync(null);

        await vm.EmptyTrashCommand.ExecuteAsync(null);

        Assert.Equal(1, _win.ConfirmCalls);
        Assert.True(vm.TrashPopupEmpty);
        Assert.Equal(0, vm.TrashCount);
        Assert.Null(await _db.Images.GetByIdAsync("x.jpg"));
        Assert.Null(await _db.Images.GetByIdAsync("y.jpg"));
        Assert.NotNull(await _db.Images.GetByIdAsync("a.jpg")); // normal は無傷
    }
}
