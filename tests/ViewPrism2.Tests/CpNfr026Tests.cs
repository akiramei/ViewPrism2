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
/// ECO-026: 画像タブ一覧の性能不変条件(CP-NFR-026)。壁時計でなく決定論ガードで退行を捕まえる:
/// #4 タグ列引きの辞書化(複数列/重複 tagId 先勝ち/simple 有無=セル内容不変)、
/// #2 表示形式切替で Items(ImageItemVM)を作り直さない(同一インスタンス維持)。
/// (#1 グリッド仮想化=画面外非実体化 は探索プローブ + maintainer 実機 golden で受入。#3/#5 は意味論不変を既存 Tests が担保)
/// </summary>
[Trait("cp", "CP-NFR-026")]
public sealed class CpNfr026Tests : IDisposable
{
    private readonly TempDb _db = new();

    public void Dispose() => _db.Dispose();

    // ---------------- #4: BuildCells のタグ列引き(辞書化)=セル内容不変 ----------------

    private static readonly Tag Rating = new() { Id = "t-rating", Name = "評価", Type = TagType.Numeric, Color = "#e8b931" };
    private static readonly Tag Job = new() { Id = "t-job", Name = "職種", Type = TagType.Textual, Color = "#2f6bed" };
    private static readonly Tag Gacha = new() { Id = "t-featured", Name = "おすすめ", Type = TagType.Simple, Color = "#8b5cf6" };

    private static IReadOnlyDictionary<string, Tag> Tags() => new Dictionary<string, Tag>(StringComparer.Ordinal)
    {
        [Rating.Id] = Rating, [Job.Id] = Job, [Gacha.Id] = Gacha,
    };

    private static ImageEntry Entry(params (string TagId, TagType Type, string? Value)[] tags)
    {
        var rec = new ImageRecord
        {
            Id = "i1", SyncFolderId = "sf", RelativePath = "a.png", FileName = "a.png", FileSize = 2048,
            Hash = new string('0', 64), CreatedDate = "2026-01-01T00:00:00.000Z", ModifiedDate = "2026-02-03T00:00:00.000Z",
        };
        var t = tags.Select(x => new EvalTagValue(x.TagId, x.Type, x.Value)).ToList();
        return new ImageEntry(rec, "C:/a.png", t);
    }

    [Fact]
    public void 複数タグ列でも各列の値が正しく引ける_辞書化()
    {
        var cols = ListColumnBuilder.Build(
            """
            [{"type":"basic","key":"name"},{"type":"tag","key":"t-rating"},
             {"type":"tag","key":"t-job"},{"type":"tag","key":"t-featured"}]
            """,
            Tags(), k => k);
        var entry = Entry(("t-rating", TagType.Numeric, "3"), ("t-job", TagType.Textual, "戦士"), ("t-featured", TagType.Simple, ""));

        var cells = ListColumnBuilder.BuildCells(entry, cols, s => $"{s}B", d => d);

        // name / rating(★3) / job(戦士) / featured(付与=有)
        Assert.Equal("a.png", cells[0].Text);
        Assert.True(cells[1].HasValue);
        Assert.Equal(3, cells[1].Stars);
        Assert.Equal("戦士", cells[2].Text);
        Assert.True(cells[3].HasValue); // simple は値が空でも付与済み=present
    }

    [Fact]
    public void 未設定タグ列は空値になる_辞書化()
    {
        var cols = ListColumnBuilder.Build(
            """[{"type":"basic","key":"name"},{"type":"tag","key":"t-rating"},{"type":"tag","key":"t-job"},{"type":"tag","key":"t-featured"}]""",
            Tags(), k => k);
        var entry = Entry(); // タグ無し

        var cells = ListColumnBuilder.BuildCells(entry, cols, s => $"{s}B", d => d);

        Assert.False(cells[1].HasValue); // 数値=未設定
        Assert.False(cells[2].HasValue); // テキスト=—
        Assert.False(cells[3].HasValue); // シンプル=オフ
    }

    [Fact]
    public void 重複tagIdは先勝ち_旧線形探索の先頭一致と同義()
    {
        var cols = ListColumnBuilder.Build("""[{"type":"basic","key":"name"},{"type":"tag","key":"t-job"}]""", Tags(), k => k);
        var entry = Entry(("t-job", TagType.Textual, "先"), ("t-job", TagType.Textual, "後"));

        var cells = ListColumnBuilder.BuildCells(entry, cols, s => $"{s}B", d => d);

        Assert.Equal("先", cells[1].Text);
    }

    // ---------------- #2: 表示形式切替で Items を作り直さない ----------------

    private async Task<ImageTabViewModel> NewWithImagesAsync(params string[] names)
    {
        var col = new SyncFolder { Id = IdGenerator.NewId(), Name = "C", Path = @"C:\col" };
        await _db.Folders.AddAsync(col);
        foreach (var name in names)
        {
            await _db.Images.AddAsync(new ImageRecord
            {
                Id = IdGenerator.NewId(), SyncFolderId = col.Id, RelativePath = name, FileName = name,
                FileSize = 10, Hash = new string('0', 64), Status = ImageStatus.Normal,
                CreatedDate = "2026-06-11T00:00:00.000Z", ModifiedDate = "2026-06-11T00:00:00.000Z",
            });
        }

        var vm = new ImageTabViewModel(
            _db.Folders, _db.Images, _db.Tags, new ImageSorter(),
            new ViewService(_db.Views, _db.Clock), new NodeGraphBuilder(),
            new PathConditionConverter(), new ConditionEvaluator(),
            new SimilaritySearchService(_db.Folders, _db.Images, _db.Features, _db.Similarities, new FakePHashImageReader(), _db.Clock),
            new MergeService(_db.Images, _db.Tags, _db.Merges),
            new TrashService(_db.Images, _db.Folders, new FilePresenceProbe()),
            new StubWindowService(), new AppSettings(), new WorkspaceService(_db.Workspaces, _db.Clock), TestLoc.Empty());
        await vm.InitializeAsync(col.Id);
        return vm;
    }

    [Fact]
    public async Task 表示形式切替はItemsを作り直さない_同一インスタンス維持()
    {
        var vm = await NewWithImagesAsync("a.jpg", "b.jpg", "c.jpg");
        var before = vm.Items.ToArray();
        Assert.NotEmpty(before);

        vm.SetListCommand.Execute(null);
        Assert.Equal(before.Length, vm.Items.Count);
        for (var i = 0; i < before.Length; i++)
        {
            Assert.Same(before[i], vm.Items[i]); // list へ切替でも同じ ImageItemVM(作り直していない)
        }

        vm.SetGridCommand.Execute(null);
        Assert.Equal(before.Length, vm.Items.Count);
        for (var i = 0; i < before.Length; i++)
        {
            Assert.Same(before[i], vm.Items[i]); // grid へ戻しても同一インスタンス
        }
    }

    private sealed class StubWindowService : IWindowService
    {
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(true);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task ShowFolderManagementAsync() => Task.CompletedTask;
        public Task ShowSettingsAsync() => Task.CompletedTask;
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
