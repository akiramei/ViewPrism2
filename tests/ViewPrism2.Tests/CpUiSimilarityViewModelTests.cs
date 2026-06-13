using ViewPrism2.App.Services;
using ViewPrism2.App.ViewModels;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using ViewPrism2.Core.Services.Similarity;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// M-UI-SIMILARITY-023 の ViewModel ロジック検査(CP-UI-G9 の機械検査可能な部分)。
/// 結果整列・空状態(類似検索)・統合後タグプレビュー(マージ)・deleted 一覧(トラッシュ)を unit で検査する。
/// 画面の目視は承認者(maintainer)が G-9 で確認する。
/// </summary>
[Trait("cp", "CP-UI-G9")]
public sealed class CpUiSimilarityViewModelTests
{
    private const string FolderRoot = "C:/pics";

    // ---- SimilarSearchViewModel ----

    [Fact]
    public void 閾値は50から100にクランプされる()
    {
        var loc = CreateLoc();
        var baseEntry = Entry("base", "0000000000000000");
        var vm = new SimilarSearchViewModel(baseEntry, [baseEntry], null!, loc, new StubWindows());

        Assert.Equal(70, vm.Threshold); // 既定 70

        vm.Threshold = 10;
        Assert.Equal(SimilarSearchViewModel.MinThreshold, vm.Threshold);

        vm.Threshold = 999;
        Assert.Equal(SimilarSearchViewModel.MaxThreshold, vm.Threshold);

        vm.Threshold = 80;
        Assert.Equal(80, vm.Threshold);
    }

    [Fact]
    public async Task 検索結果は類似度降順で並び空状態が判定される()
    {
        using var db = new TempDb();
        await SeedFolderAsync(db, "folder-a", FolderRoot);
        var baseImg = await SeedImageAsync(db, "folder-a", "base.jpg", "0000000000000000");
        var near = await SeedImageWithIdAsync(db, "folder-a", "id-near", "near.jpg", "000000000000001f"); // 距離5→90
        var exact = await SeedImageWithIdAsync(db, "folder-a", "id-exact", "exact.jpg", "0000000000000000"); // 距離0→100

        var reader = new FakePHashImageReader();
        reader.SetPHash(Abs("base.jpg"), "0000000000000000");
        reader.SetPHash(Abs("near.jpg"), "000000000000001f");
        reader.SetPHash(Abs("exact.jpg"), "0000000000000000");
        var service = new SimilaritySearchService(db.Folders, db.Images, db.Features, db.Similarities, reader, db.Clock);

        var loc = CreateLoc();
        var baseEntry = ToEntry(baseImg);
        var entries = new[] { baseEntry, ToEntry(near), ToEntry(exact) };
        var vm = new SimilarSearchViewModel(baseEntry, entries, service, loc, new StubWindows());

        Assert.False(vm.IsEmpty); // 未検索は空状態でない

        await vm.SearchCommand.ExecuteAsync(null);

        // 100(id-exact) → 90(id-near) の降順
        Assert.Equal(2, vm.Results.Count);
        Assert.Equal("id-exact", vm.Results[0].Record.Id);
        Assert.Equal(100, vm.Results[0].Score);
        Assert.Equal("id-near", vm.Results[1].Record.Id);
        Assert.Equal(90, vm.Results[1].Score);
        Assert.False(vm.IsEmpty);
    }

    [Fact]
    public async Task 結果0件で空状態フラグが立つ()
    {
        using var db = new TempDb();
        await SeedFolderAsync(db, "folder-a", FolderRoot);
        var baseImg = await SeedImageAsync(db, "folder-a", "base.jpg", "0000000000000000");
        var far = await SeedImageAsync(db, "folder-a", "far.jpg", "ffffffffffffffff"); // 距離64→0

        var reader = new FakePHashImageReader();
        reader.SetPHash(Abs("base.jpg"), "0000000000000000");
        reader.SetPHash(Abs("far.jpg"), "ffffffffffffffff");
        var service = new SimilaritySearchService(db.Folders, db.Images, db.Features, db.Similarities, reader, db.Clock);

        var loc = CreateLoc();
        var baseEntry = ToEntry(baseImg);
        var vm = new SimilarSearchViewModel(baseEntry, [baseEntry, ToEntry(far)], service, loc, new StubWindows());

        await vm.SearchCommand.ExecuteAsync(null);

        Assert.Empty(vm.Results);
        Assert.True(vm.IsEmpty);
    }

    // ---- MergeViewModel ----

    [Fact]
    public void マージプレビューはMergeCalculatorで統合後タグを算出する()
    {
        using var db = new TempDb();
        var loc = CreateLoc();
        var tagById = new Dictionary<string, Tag>(StringComparer.Ordinal)
        {
            ["t1"] = new() { Id = "t1", Name = "色", Type = TagType.Textual },
            ["t2"] = new() { Id = "t2", Name = "場所", Type = TagType.Textual },
        };

        var target = EntryWithTags("target", [new EvalTagValue("t1", TagType.Textual, "赤")]);
        var source = EntryWithTags("source", [new EvalTagValue("t2", TagType.Textual, "東京")]);

        var vm = new MergeViewModel(target, [source], tagById, null!, loc);

        // 統合後タグ: t1=赤(マージ先), t2=東京(マージ元) — タグ名で表示
        Assert.Equal(2, vm.TagPreview.Count);
        Assert.Contains(vm.TagPreview, p => p.TagName == "色" && p.Value == "赤");
        Assert.Contains(vm.TagPreview, p => p.TagName == "場所" && p.Value == "東京");

        // 役割ラベル: 先頭=保持(マージ先)、続き=統合(マージ元)
        Assert.True(vm.Images[0].IsTarget);
        Assert.False(vm.Images[1].IsTarget);
    }

    [Fact]
    public void マージプレビュー_衝突はマージ先優先()
    {
        var loc = CreateLoc();
        var tagById = new Dictionary<string, Tag>(StringComparer.Ordinal)
        {
            ["t1"] = new() { Id = "t1", Name = "色", Type = TagType.Textual },
        };
        var target = EntryWithTags("target", [new EvalTagValue("t1", TagType.Textual, "keep")]);
        var source = EntryWithTags("source", [new EvalTagValue("t1", TagType.Textual, "overwrite")]);

        var vm = new MergeViewModel(target, [source], tagById, null!, loc);

        Assert.Single(vm.TagPreview);
        Assert.Equal("keep", vm.TagPreview[0].Value);
    }

    // ---- TrashViewModel ----

    [Fact]
    public async Task トラッシュは選択コレクションのdeleted画像のみを列挙する()
    {
        using var db = new TempDb();
        await SeedFolderAsync(db, "folder-a", FolderRoot);
        await SeedFolderAsync(db, "folder-b", "C:/other");
        await SeedImageAsync(db, "folder-a", "n.jpg", "0", ImageStatus.Normal);
        var del1 = await SeedImageAsync(db, "folder-a", "d1.jpg", "0", ImageStatus.Deleted);
        var del2 = await SeedImageAsync(db, "folder-a", "d2.jpg", "0", ImageStatus.Deleted);
        await SeedImageAsync(db, "folder-b", "other-deleted.jpg", "0", ImageStatus.Deleted);

        var vm = new TrashViewModel("folder-a", db.Images, db.Folders, CreateLoc());
        await vm.LoadAsync();

        Assert.Equal(2, vm.Count);
        Assert.False(vm.IsEmpty);
        var ids = vm.Items.Select(i => i.Record.Id).ToHashSet(StringComparer.Ordinal);
        Assert.Contains(del1.Id, ids);
        Assert.Contains(del2.Id, ids);
    }

    [Fact]
    public async Task トラッシュは空のとき空状態を示す()
    {
        using var db = new TempDb();
        await SeedFolderAsync(db, "folder-a", FolderRoot);
        await SeedImageAsync(db, "folder-a", "n.jpg", "0", ImageStatus.Normal);

        var vm = new TrashViewModel("folder-a", db.Images, db.Folders, CreateLoc());
        await vm.LoadAsync();

        Assert.Equal(0, vm.Count);
        Assert.True(vm.IsEmpty);
    }

    // ---- ヘルパ ----

    private static LocalizationService CreateLoc()
    {
        var ja = new Dictionary<string, string>
        {
            ["similar.selectAsSource"] = "マージ元に選択",
            ["merge.roleTarget"] = "保持",
            ["merge.roleSource"] = "統合",
            ["merge.completed"] = "マージが完了しました",
            ["merge.failed"] = "マージに失敗しました",
        };
        var resources = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["ja"] = ja,
        };
        return new LocalizationService(resources);
    }

    private static ImageEntry Entry(string id, string phash)
        => new(new ImageRecord
        {
            Id = id,
            SyncFolderId = "folder-a",
            RelativePath = id + ".jpg",
            FileName = id + ".jpg",
            FileSize = 1,
            Hash = phash,
            Status = ImageStatus.Normal,
            CreatedDate = "2026-01-01T00:00:00.000Z",
            ModifiedDate = "2026-01-01T00:00:00.000Z",
        }, Abs(id + ".jpg"), []);

    private static ImageEntry EntryWithTags(string id, IReadOnlyList<EvalTagValue> tags)
        => new(new ImageRecord
        {
            Id = id,
            SyncFolderId = "folder-a",
            RelativePath = id + ".jpg",
            FileName = id + ".jpg",
            FileSize = 1,
            Hash = new string('0', 64),
            Status = ImageStatus.Normal,
            CreatedDate = "2026-01-01T00:00:00.000Z",
            ModifiedDate = "2026-01-01T00:00:00.000Z",
        }, Abs(id + ".jpg"), tags);

    private static ImageEntry ToEntry(ImageRecord record)
        => new(record, Abs(record.RelativePath), []);

    private static string Abs(string relativePath)
        => Path.Combine(FolderRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static async Task SeedFolderAsync(TempDb db, string id, string path)
    {
        var folder = new SyncFolder { Id = id, Name = id, Path = path };
        Assert.True((await db.Folders.AddAsync(folder)).IsSuccess);
    }

    private static Task<ImageRecord> SeedImageAsync(
        TempDb db, string folderId, string name, string phash, ImageStatus status = ImageStatus.Normal)
        => SeedImageWithIdAsync(db, folderId, IdGenerator.NewId(), name, phash, status);

    private static async Task<ImageRecord> SeedImageWithIdAsync(
        TempDb db, string folderId, string id, string name, string phash, ImageStatus status = ImageStatus.Normal)
    {
        var image = new ImageRecord
        {
            Id = id,
            SyncFolderId = folderId,
            RelativePath = name,
            FileName = name,
            FileSize = name.Length,
            Hash = phash,
            Status = status,
            CreatedDate = "2026-01-01T00:00:00.000Z",
            ModifiedDate = "2026-01-01T00:00:00.000Z",
        };
        await db.Images.AddAsync(image);
        return image;
    }

    /// <summary>マージへ進まない最小スタブ(ShowMergeAsync=false)。</summary>
    private sealed class StubWindows : IWindowService
    {
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(true);

        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);

        public Task ShowFolderManagementAsync() => Task.CompletedTask;

        public Task ShowSettingsAsync() => Task.CompletedTask;

        public Task<bool> ShowTagEditorAsync(Tag? existing) => Task.FromResult(false);

        public Task<bool> ShowViewEditDialogAsync(View? existing) => Task.FromResult(false);

        public Task<IReadOnlyList<string>?> ShowNumericValueDialogAsync(
            Tag tag, NumericTagSettings? settings, int selectionCount)
            => Task.FromResult<IReadOnlyList<string>?>(null);

        public Task<NodeConditionResult?> ShowNodeConditionDialogAsync(
            Tag tag, HierarchyConditionType? currentType, string? currentValueJson)
            => Task.FromResult<NodeConditionResult?>(null);

        public Task ShowRelinkAsync(string folderId) => Task.CompletedTask;

        public void ShowViewer(IReadOnlyList<ImageEntry> ordered, int startIndex)
        {
        }

        public Task ShowSimilarSearchAsync(ImageEntry baseImage, IReadOnlyList<ImageEntry> collectionEntries)
            => Task.CompletedTask;

        public Task<bool> ShowMergeAsync(ImageEntry target, IReadOnlyList<ImageEntry> sources)
            => Task.FromResult(false);

        public Task ShowTrashAsync(string collectionId) => Task.CompletedTask;
    }
}
