using System.Collections.Concurrent;
using System.Reflection;
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
/// ECO-118: タグ付与/剥奪は選択規模の差分経路を通る(母集合規模の再読・再構築を通らない)。
/// 構造 probe(ECO-058/113/114 様式=固定時間閾値なし):
/// ① DB 再読が母集合スコープ API(GetImageTagsByFolderAsync/GetAllImageTagsAsync)を呼ばない
/// ② 非影響 ImageItemVM はインスタンス同一(全再構築されない)
/// ③ 意味論維持= 影響アイテムの反映(Added/チップ件数追随/FS フィルタ離脱=表示から除去)。
/// </summary>
[Trait("cp", "CP-UI-G1")]
public sealed class CpUiG1TagApplyDeltaTests : IDisposable
{
    private readonly TempDb _db = new();

    public void Dispose() => _db.Dispose();

    // ---- spy(CpUiG1TrashPopupTests の様式) ----

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

    // ---- 検体 ----

    private async Task SeedTagsAsync()
    {
        await _db.Tags.AddAsync(new Tag { Id = "t-red", Name = "赤", Type = TagType.Simple, Color = "#e5484d" });
        await _db.Tags.AddAsync(new Tag { Id = "t-blue", Name = "青", Type = TagType.Simple, Color = "#2f6bed" });
    }

    private async Task<(ImageTabViewModel Vm, RepositorySpy<ITagRepository> Spy)> NewImageTabAsync(int images = 6)
    {
        await SeedTagsAsync();
        var col = new SyncFolder { Id = IdGenerator.NewId(), Name = "C", Path = @"C:\col" };
        await _db.Folders.AddAsync(col);
        for (var i = 0; i < images; i++)
        {
            await _db.Images.AddAsync(new ImageRecord
            {
                Id = $"img-{i:D2}", SyncFolderId = col.Id, RelativePath = $"img-{i:D2}.jpg", FileName = $"img-{i:D2}.jpg",
                FileSize = 10, Hash = new string('0', 64), Status = ImageStatus.Normal,
                CreatedDate = "2026-06-11T00:00:00.000Z", ModifiedDate = "2026-06-11T00:00:00.000Z",
            });
        }
        // img-00 に既存タグ(FS チップ行を出す+剥奪検体)
        await _db.Tags.TagImagesAsync(["img-00"], "t-blue", null);

        var tags = Spy<ITagRepository>(_db.Tags, out var spy);
        var vm = new ImageTabViewModel(
            _db.Folders, _db.Images, tags, new ImageSorter(),
            new ViewService(_db.Views, _db.Clock), new NodeGraphBuilder(),
            new PathConditionConverter(), new ConditionEvaluator(),
            new SimilaritySearchService(_db.Folders, _db.Images, _db.Features, _db.Similarities, new FakePHashImageReader(), _db.Clock),
            new MergeService(_db.Images, tags, _db.Merges),
            new TrashService(_db.Images, _db.Folders, new FilePresenceProbe()),
            new StubWindows(), new AppSettings(), new WorkspaceService(_db.Workspaces, _db.Clock), TestLoc.Ja());
        await vm.InitializeAsync(col.Id);
        vm.ToggleEditCommand.Execute(null);
        return (vm, spy);
    }

    private static AddRowVM Row(ImageTabViewModel vm, string tagId) =>
        vm.AddGroups.SelectMany(g => g.Tags).Single(r => r.Id == tagId);

    // ---- ① + ② + ③(付与) ----

    [Fact]
    public async Task 付与は母集合スコープの再読と全再構築を通らず影響アイテムだけ更新する()
    {
        var (vm, spy) = await NewImageTabAsync();
        vm.HandleItemClick(vm.Items.Single(i => i.Id == "img-01"), ctrl: false, shift: false);
        vm.TabAddCommand.Execute(null);

        var before = vm.Items.Where(i => !i.IsFolder).ToDictionary(i => i.Id, i => i);
        var folderScopeBefore = spy.Count(nameof(ITagRepository.GetImageTagsByFolderAsync));

        await vm.ClickAddRowCommand.ExecuteAsync(Row(vm, "t-red"));

        // ① 母集合スコープの DB 再読なし(是正前= ReloadTagsAsync が毎回 1 回呼ぶ)
        Assert.Equal(folderScopeBefore, spy.Count(nameof(ITagRepository.GetImageTagsByFolderAsync)));
        // ② 非影響アイテムはインスタンス同一(是正前= 全 26 万件相当の再構築)
        foreach (var item in vm.Items.Where(i => !i.IsFolder && i.Id != "img-01"))
            Assert.Same(before[item.Id], item);
        // ③ 意味論: パネルの Added 反映+件数ラベル不変
        Assert.True(Row(vm, "t-red").Added);
        var tagged = await _db.Tags.GetImageTagsAsync("img-01");
        Assert.Contains(tagged, t => t.TagId == "t-red");
    }

    [Fact]
    public async Task 付与でFSチップ件数が追随する()
    {
        var (vm, spy) = await NewImageTabAsync();
        vm.HandleItemClick(vm.Items.Single(i => i.Id == "img-01"), ctrl: false, shift: false);
        vm.TabAddCommand.Execute(null);

        await vm.ClickAddRowCommand.ExecuteAsync(Row(vm, "t-blue"));

        // t-blue は img-00(seed)+img-01 の 2 件になる
        var blue = vm.Chips.Single(c => c.Id == "t-blue");
        Assert.Equal("2", blue.Count);
    }

    // ---- ③(剥奪+FS フィルタ離脱=表示から除去) ----

    [Fact]
    public async Task FSタグフィルタ中の剥奪は対象を表示から除去し他アイテムは同一インスタンスのまま()
    {
        var (vm, spy) = await NewImageTabAsync();
        // フィルタ検体: img-01 にも t-blue を付与しフィルタ表示を 2 件にする
        vm.HandleItemClick(vm.Items.Single(i => i.Id == "img-01"), ctrl: false, shift: false);
        vm.TabAddCommand.Execute(null);
        await vm.ClickAddRowCommand.ExecuteAsync(Row(vm, "t-blue"));
        // t-blue チップでフィルタ(選択はフィルタ操作でも保持される前提はないため再選択)
        vm.ClickChip(vm.Chips.Single(c => c.Id == "t-blue"));
        Assert.Equal(2, vm.Items.Count(i => !i.IsFolder));

        // フィルタ操作は選択を保持しうる(既選択なら再クリックは解除になる)ため、状態を見て整える
        if (!vm.CurrentTags.Any(t => t.Id == "t-blue"))
            vm.HandleItemClick(vm.Items.Single(i => i.Id == "img-01"), ctrl: false, shift: false);
        var untouched = vm.Items.Single(i => i.Id == "img-00");
        vm.TabCurrentCommand.Execute(null);
        await vm.RemoveCurrentTagCommand.ExecuteAsync(vm.CurrentTags.Single(t => t.Id == "t-blue"));

        // フィルタ対象タグを失った img-01 は表示から消え、img-00 は同一インスタンス
        Assert.DoesNotContain(vm.Items, i => i.Id == "img-01");
        Assert.Same(untouched, vm.Items.Single(i => i.Id == "img-00"));
    }

    // ---- view 軸(R8 F3: チップ差分/REQ-094 未分類離脱+専用空状態/ゴースト選択 fallback) ----

    /// <summary>view 軸検体: タグノード t-red 1 つのビュー+画像 4 枚(img-00 のみ t-red 付与済み)。
    /// ビューは VM 構築前に作成する(InitializeAsync がビュー一覧を読むため)。</summary>
    private async Task<ImageTabViewModel> NewViewAxisVmAsync()
    {
        await SeedTagsAsync();
        var col = new SyncFolder { Id = IdGenerator.NewId(), Name = "C", Path = @"C:\col" };
        await _db.Folders.AddAsync(col);
        for (var i = 0; i < 4; i++)
        {
            await _db.Images.AddAsync(new ImageRecord
            {
                Id = $"img-{i:D2}", SyncFolderId = col.Id, RelativePath = $"img-{i:D2}.jpg", FileName = $"img-{i:D2}.jpg",
                FileSize = 10, Hash = new string('0', 64), Status = ImageStatus.Normal,
                CreatedDate = "2026-06-11T00:00:00.000Z", ModifiedDate = "2026-06-11T00:00:00.000Z",
            });
        }
        await _db.Tags.TagImagesAsync(["img-00"], "t-red", null);
        var viewService = new ViewService(_db.Views, _db.Clock);
        var view = (await viewService.CreateAsync("V118")).Value!;
        var node = new HierarchyNode { Id = IdGenerator.NewId(), ViewId = view.Id, TagId = "t-red", Position = 0 };
        Assert.True((await viewService.SaveHierarchyAsync(view.Id, [node], null)).IsSuccess);

        var vm = TestImageTab.NewVm(_db);
        await vm.InitializeAsync(col.Id);
        await vm.SelectAxisCommand.ExecuteAsync(view.Id);
        Assert.True(vm.IsViewAxis);
        vm.ToggleEditCommand.Execute(null);
        return vm;
    }

    [Fact]
    public async Task view軸の付与はチップ件数を差分で追随させ非影響アイテムは同一インスタンスのまま()
    {
        var vm = await NewViewAxisVmAsync();
        Assert.Equal("1", vm.Chips.Single(c => c.Label == "赤").Count); // seed= img-00 のみ
        vm.HandleItemClick(vm.Items.Single(i => i.Id == "img-01"), ctrl: false, shift: false);
        vm.TabAddCommand.Execute(null);
        var before = vm.Items.Where(i => !i.IsFolder && i.Id != "img-01").ToDictionary(i => i.Id, i => i);

        await vm.ClickAddRowCommand.ExecuteAsync(Row(vm, "t-red"));

        Assert.Equal("2", vm.Chips.Single(c => c.Label == "赤").Count);
        foreach (var kv in before)
            Assert.Same(kv.Value, vm.Items.Single(i => i.Id == kv.Key));
    }

    [Fact]
    public async Task 未分類モードの付与は対象を離脱させ全員離脱で専用空状態になる()
    {
        var vm = await NewViewAxisVmAsync();
        vm.SetDisplayModeUnclassifiedCommand.Execute(null);
        // 未分類表示= t-red を持たない img-01/02/03 の 3 件
        Assert.Equal(3, vm.Items.Count(i => !i.IsFolder));

        // 3 件全員を選択して t-red を付与 → 全員離脱(REQ-094)
        vm.HandleItemClick(vm.Items.Single(i => i.Id == "img-01"), ctrl: false, shift: false);
        vm.HandleItemClick(vm.Items.Single(i => i.Id == "img-02"), ctrl: true, shift: false);
        vm.HandleItemClick(vm.Items.Single(i => i.Id == "img-03"), ctrl: true, shift: false);
        vm.TabAddCommand.Execute(null);
        await vm.ClickAddRowCommand.ExecuteAsync(Row(vm, "t-red"));

        Assert.Equal(0, vm.Items.Count(i => !i.IsFolder));
        Assert.True(vm.ShowUnclassifiedEmpty, "REQ-094 専用空状態が出ていない(R8 F2)");
    }

    [Fact]
    public async Task 表示外選択が混在した次のタグ操作は全面経路へ戻り表示帰属を再解決する()
    {
        // R8 F1: FS フィルタ中に leave した画像は選択に残る(ゴースト=旧経路と同じ)。
        // 次のタグ操作は差分経路の前提(選択⊆表示)が破れるため全面経路へ fallback し、
        // 帰属を回復した画像が表示へ戻る(旧経路とのパリティ)。付与はテキスト値チップ経路
        // (シンプル行は Added ガードで no-op になるため=R8 F1 の実測経路)。
        // 専用シード: テキストタグは VM 構築前に定義する(_tagById へ載せるため)
        var tagService = new TagService(_db.Tags);
        var tx = (await tagService.CreateAsync("地域", TagType.Textual)).Value!;
        Assert.True((await tagService.SetTextualSettingsAsync(tx.Id, ["x"], TagValueDomain.Suggest)).IsSuccess);
        var col = new SyncFolder { Id = IdGenerator.NewId(), Name = "C", Path = @"C:\col" };
        await _db.Folders.AddAsync(col);
        for (var i = 0; i < 3; i++)
        {
            await _db.Images.AddAsync(new ImageRecord
            {
                Id = $"img-{i:D2}", SyncFolderId = col.Id, RelativePath = $"img-{i:D2}.jpg", FileName = $"img-{i:D2}.jpg",
                FileSize = 10, Hash = new string('0', 64), Status = ImageStatus.Normal,
                CreatedDate = "2026-06-11T00:00:00.000Z", ModifiedDate = "2026-06-11T00:00:00.000Z",
            });
        }
        await _db.Tags.TagImagesAsync(["img-00", "img-01"], tx.Id, "x");
        var vm = TestImageTab.NewVm(_db);
        await vm.InitializeAsync(col.Id);
        vm.ToggleEditCommand.Execute(null);
        vm.ClickChip(vm.Chips.Single(c => c.Id == tx.Id));
        Assert.Equal(2, vm.Items.Count(i => !i.IsFolder));

        vm.HandleItemClick(vm.Items.Single(i => i.Id == "img-01"), ctrl: false, shift: false);
        vm.TabCurrentCommand.Execute(null);
        await vm.RemoveCurrentTagCommand.ExecuteAsync(vm.CurrentTags.Single(t => t.Id == tx.Id));
        Assert.DoesNotContain(vm.Items, i => i.Id == "img-01"); // leave(ゴースト選択残留)

        // ゴースト+表示中 img-00 の混在選択で値チップを再付与
        // → 前提(選択⊆表示)が破れ fallback 全面経路 → img-01 がフィルタ表示へ復帰
        vm.HandleItemClick(vm.Items.Single(i => i.Id == "img-00"), ctrl: true, shift: false);
        vm.TabAddCommand.Execute(null);
        await vm.ClickAddRowCommand.ExecuteAsync(Row(vm, tx.Id)); // 展開
        var chip = Row(vm, tx.Id).ValueChips.Single(c => c.Label == "x");
        await vm.ApplyTextValueCommand.ExecuteAsync(chip);
        Assert.Contains(vm.Items, i => i.Id == "img-01");
    }

    // ---- WorkTab read-across(全 DB 再読の排除) ----

    [Fact]
    public async Task 作業タブの付与は全DB付与行の再読を通らない()
    {
        await SeedTagsAsync();
        await _db.Folders.AddAsync(new SyncFolder { Id = "f1", Name = "F", Path = @"C:\f" });
        for (var i = 0; i < 3; i++)
        {
            await _db.Images.AddAsync(new ImageRecord
            {
                Id = $"w-{i}", SyncFolderId = "f1", RelativePath = $"w-{i}.jpg", FileName = $"w-{i}.jpg",
                FileSize = 10, Hash = new string('0', 64), Status = ImageStatus.Normal,
                CreatedDate = "2026-06-11T00:00:00.000Z", ModifiedDate = "2026-06-11T00:00:00.000Z",
            });
        }
        var workspaces = new WorkspaceService(_db.Workspaces, _db.Clock);
        await workspaces.AddImagesToDefaultAsync(["w-0", "w-1", "w-2"]);

        var tags = Spy<ITagRepository>(_db.Tags, out var spy);
        var vm = new WorkTabViewModel(
            workspaces, _db.Folders, tags,
            new SimilaritySearchService(_db.Folders, _db.Images, _db.Features, _db.Similarities, new FakePHashImageReader(), _db.Clock),
            new MergeService(_db.Images, tags, _db.Merges),
            new TrashService(_db.Images, _db.Folders, new FilePresenceProbe()),
            new StubWindows(), new ImageSorter(), new AppSettings(), TestLoc.Ja());
        await vm.InitializeAsync();
        vm.ToggleEditCommand.Execute(null);
        vm.HandleItemClick(vm.Items[0], ctrl: false, shift: false);
        vm.TabAddCommand.Execute(null);
        var allScopeBefore = spy.Count(nameof(ITagRepository.GetAllImageTagsAsync));

        var row = vm.AddGroups.SelectMany(g => g.Tags).Single(r => r.Id == "t-red");
        await vm.ClickAddRowCommand.ExecuteAsync(row);

        // 是正前= ReloadTagsAsync が GetAllImageTagsAsync(全 DB)を毎回 1 回呼ぶ
        Assert.Equal(allScopeBefore, spy.Count(nameof(ITagRepository.GetAllImageTagsAsync)));
        Assert.True(vm.AddGroups.SelectMany(g => g.Tags).Single(r => r.Id == "t-red").Added);
    }

    private sealed class StubWindows : IWindowService
    {
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(false);
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
}
