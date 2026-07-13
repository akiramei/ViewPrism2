using ViewPrism2.App.Services;
using ViewPrism2.App.ViewModels;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using ViewPrism2.Core.Services.Repair;
using ViewPrism2.Core.Services.Similarity;
using ViewPrism2.Infrastructure.Imaging;
using ViewPrism2.Infrastructure.Scanning;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-DISPLAY-PARITY-022(ECO-004): read-across で確定した表示 omission(A-1〜A-6)の VM フィールド公開を
/// exact 検査する。視覚描画は各 golden(CP-UI-G1/G6/G10)が担うため、ここでは各 ViewModel が表示要素を
/// 公開していることのみを検査する(原典 view-prism は非開示=§3 表示契約マニフェストから製造)。
///   A-1/A-6: RelinkCandidateViewModel.AbsolutePath/FileName + MissingImageViewModel.FileName/AbsolutePath
///   A-2: グリッド項目 VM(ImageItemViewModel).SizeText
///   A-3: TrashItemViewModel.SizeText
///   A-4: ViewRowViewModel.IsFavorite/Description
///   A-5: タグパレット行 VM(TagPaletteRowViewModel).Description
/// </summary>
[Trait("cp", "CP-DISPLAY-PARITY-022")]
public sealed class CpDisplayParity022Tests
{
    private const string Folder = "folder-1";

    private static LocalizationService CreateLoc()
        => new(new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["ja"] = new Dictionary<string, string>
            {
                ["image.gridView.noImages"] = "画像がありません",
                ["common.name"] = "名前",
                ["tag.type.simple"] = "単純",
                ["tag.type.textual"] = "テキスト",
                ["tag.type.numeric"] = "数値",
            },
        });

    private static ImageRecord Image(string id, string name, ImageStatus status, string hash = "h", long size = 100)
        => new()
        {
            Id = id,
            SyncFolderId = Folder,
            RelativePath = name,
            FileName = name,
            FileSize = size,
            Hash = hash,
            Status = status,
            CreatedDate = "2026-01-01T00:00:00.000Z",
            ModifiedDate = "2026-01-01T00:00:00.000Z",
        };

    private sealed class NoopWindows : IWindowService
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

    // ---- A-1 / A-6: RelinkViewModel(旧 RelinkWindow 経路)の候補/missing 行の表示パリティ ----

    private static RelinkViewModel CreateRelinkVm(TempDb db)
        => new(
            Folder,
            db.Images,
            db.Folders,
            new RelinkService(db.Images, db.Tags),
            CreateLoc(),
            new NoopWindows());

    [Fact]
    public async Task A1_候補はファイル名とサムネイル絶対パスを公開する()
    {
        using var db = new TempDb();
        await db.Folders.AddAsync(new SyncFolder { Id = Folder, Name = "F", Path = "C:/coll" });
        await db.Images.AddAsync(Image("m1", "sub/old.png", ImageStatus.Missing, hash: "H1", size: 6395402));
        // リネーム後の同一内容ファイル(pending)= exact-hash 候補
        await db.Images.AddAsync(Image("p1", "_old.png", ImageStatus.Pending, hash: "H1", size: 6395402));

        var vm = CreateRelinkVm(db);
        await vm.LoadAsync();
        vm.SelectedMissing = vm.MissingImages.First();
        // 候補ロードは selection 変更で fire-and-forget 発火するが、unit では awaitable 経路で確実に待つ
        // (RepairViewModel と同型の RefreshCandidatesAsync)。
        await vm.RefreshCandidatesAsync();

        var candidate = Assert.Single(vm.Candidates);
        Assert.Equal("_old.png", candidate.FileName);                  // A-1: ファイル名(主見出し)
        Assert.Equal("_old.png", candidate.RelativePath);              // パス
        Assert.False(string.IsNullOrEmpty(candidate.SizeText));        // サイズ
        Assert.False(string.IsNullOrEmpty(candidate.ModifiedText));    // 更新日時
        Assert.NotNull(candidate.AbsolutePath);                        // A-1: サムネイル描画用の物理パス
        Assert.Contains("_old.png", candidate.AbsolutePath);           // collection root + relative
        Assert.Contains("coll", candidate.AbsolutePath);
    }

    [Fact]
    public async Task A6_リンク切れ画像行はファイル名と絶対パスを公開する()
    {
        using var db = new TempDb();
        await db.Folders.AddAsync(new SyncFolder { Id = Folder, Name = "F", Path = "C:/coll" });
        await db.Images.AddAsync(Image("m1", "sub/broken.png", ImageStatus.Missing, hash: "H1"));

        var vm = CreateRelinkVm(db);
        await vm.LoadAsync();

        var missing = Assert.Single(vm.MissingImages);
        Assert.Equal("sub/broken.png", missing.FileName);              // A-6: ファイル名(主表示)
        Assert.Equal("sub/broken.png", missing.RelativePath);          // パス(従)
        Assert.NotNull(missing.AbsolutePath);                          // collection root 解決済み
        Assert.Contains("coll", missing.AbsolutePath);
    }

    // ---- A-2: グリッド項目 VM(ImageItemViewModel)の SizeText ----

    [Fact]
    public async Task A2_グリッド項目VMはサイズ文字列を公開する()
    {
        // ECO-024: 原典 ImageBrowserViewModel 撤去に伴い、A-2(グリッド項目 VM がサイズ文字列を公開・
        // ECO-004/DC-GRID-001)の検査を新 surface ImageTabViewModel(ImageItemVM.SizeLabel)へ移行する。
        using var db = new TempDb();
        var col = new SyncFolder { Id = Folder, Name = "C", Path = @"C:\col" };
        await db.Folders.AddAsync(col);
        await db.Images.AddAsync(Image("i1", "a.jpg", ImageStatus.Normal, hash: new string('0', 64), size: 1024));

        var vm = new ImageTabViewModel(
            db.Folders, db.Images, db.Tags, new ImageSorter(),
            new ViewService(db.Views, db.Clock), new NodeGraphBuilder(),
            new PathConditionConverter(), new ConditionEvaluator(),
            new SimilaritySearchService(db.Folders, db.Images, db.Features, db.Similarities, new FakePHashImageReader(), db.Clock),
            new MergeService(db.Images, db.Tags, db.Merges),
            new TrashService(db.Images, db.Folders, new FilePresenceProbe()),
            new NoopWindows(), new AppSettings(), new WorkspaceService(db.Workspaces, db.Clock), TestLoc.Ja());
        await vm.InitializeAsync(col.Id);

        var item = Assert.Single(vm.Items, i => !i.IsFolder);
        Assert.False(string.IsNullOrEmpty(item.SizeLabel));            // A-2: グリッド項目 VM がサイズ文字列を公開
        Assert.Equal("1 KB", item.SizeLabel);                          // 新 surface FmtSize(1024 → "1 KB")
    }

    // ---- A-3: トラッシュ項目 VM の SizeText ----

    [Fact]
    public async Task A3_トラッシュ項目VMはサイズ文字列を公開する()
    {
        // ECO-051: 旧 TrashViewModel(到達不能な V3 モーダル)撤去に伴い、A-3(トラッシュ項目 VM が
        // サイズ文字列を公開)の検査を生存 surface=インペイン ポップアップ(TrashPopupItemVM.SizeLabel)へ
        // 移行する(ECO-024 の A-2 移行と同型)。
        using var db = new TempDb();
        var col = new SyncFolder { Id = Folder, Name = "C", Path = @"C:\col" };
        await db.Folders.AddAsync(col);
        await db.Images.AddAsync(Image("d1", "d1.jpg", ImageStatus.Deleted, size: 1048576));

        var vm = new ImageTabViewModel(
            db.Folders, db.Images, db.Tags, new ImageSorter(),
            new ViewService(db.Views, db.Clock), new NodeGraphBuilder(),
            new PathConditionConverter(), new ConditionEvaluator(),
            new SimilaritySearchService(db.Folders, db.Images, db.Features, db.Similarities, new FakePHashImageReader(), db.Clock),
            new MergeService(db.Images, db.Tags, db.Merges),
            new TrashService(db.Images, db.Folders, new FilePresenceProbe()),
            new NoopWindows(), new AppSettings(), new WorkspaceService(db.Workspaces, db.Clock), TestLoc.Ja());
        await vm.InitializeAsync(col.Id);
        await vm.OpenTrashCommand.ExecuteAsync(null);

        var item = Assert.Single(vm.TrashPopupItems);
        Assert.Equal("1.0 MB", item.SizeLabel);                        // A-3: 共有整形器(1048576 → 1.0 MB)
    }

    // ---- A-4(ECO-007/E1 改訂): ViewRowViewModel は TagCount 公開・★は行非表示・Description は tooltip ----

    [Fact]
    public void A4_ビュー行VMはタグ数を公開し説明はtooltip用に公開する()
    {
        var favWithDesc = new ViewRowViewModel(
            new View
            {
                Id = "v1",
                Name = "Fav",
                Description = "とても便利なビュー",
                IsFavorite = true,
                ModifiedAt = "2026-01-01T00:00:00.000Z",
            },
            tagCount: 3);
        Assert.Equal(3, favWithDesc.TagCount);                         // E1/DE-4: タグ数バッジ
        Assert.Equal("とても便利なビュー", favWithDesc.Description);    // E1/DE-3: 説明(tooltip)
        Assert.True(favWithDesc.HasDescription);
        Assert.True(favWithDesc.IsFavorite);                           // E1/DE-2: ★データは保持(行には出さない)

        var plain = new ViewRowViewModel(new View
        {
            Id = "v2",
            Name = "Plain",
            Description = null,
            IsFavorite = false,
            ModifiedAt = "2026-01-01T00:00:00.000Z",
        });
        Assert.Equal(0, plain.TagCount);                               // 既定 0(配置なし)
        Assert.False(plain.IsFavorite);
        Assert.Null(plain.Description);                                // null は tooltip 非表示
        Assert.False(plain.HasDescription);

        var blank = new ViewRowViewModel(new View
        {
            Id = "v3",
            Name = "Blank",
            Description = "   ",
            IsFavorite = false,
            ModifiedAt = "2026-01-01T00:00:00.000Z",
        });
        Assert.False(blank.HasDescription);                           // 空白のみは tooltip 非表示
        Assert.Null(blank.Description);                               // 空白のみは null を返す(tooltip 抑止)
    }

    // ---- A-5(ECO-007/E2 撤回): タグパレット行 VM は説明を行に公開しない ----

    [Fact]
    public void A5_タグパレット行VMは説明を行に公開しない()
    {
        // E2: パレット行 VM に Description/HasDescription は無い(Tag.Description はデータとして残るが
        // 行 VM では非公開 — 作成/編集ダイアログでのみ参照)。型に当該メンバが無いことを reflection で検査。
        var rowType = typeof(TagPaletteRowViewModel);
        Assert.Null(rowType.GetProperty("Description"));
        Assert.Null(rowType.GetProperty("HasDescription"));

        // 行の公開要素(色+名前+型)は維持されること
        var row = new TagPaletteRowViewModel(
            new Tag { Id = "t1", Name = "色", Type = TagType.Simple, Color = "#30a46c", Description = "作品の主要色" },
            typeText: "単純",
            predefinedValues: [],
            numeric: null);
        Assert.Equal("色", row.Name);
        Assert.Equal("#30a46c", row.Color);
        Assert.True(row.HasColor);
        Assert.Equal("単純", row.TypeText);
        Assert.True(row.IsSimple);
        // Tag 本体の Description はデータとして保持(ダイアログ参照用)
        Assert.Equal("作品の主要色", row.Tag.Description);
    }
}
