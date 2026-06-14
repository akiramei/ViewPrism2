using ViewPrism2.App.Services;
using ViewPrism2.App.ViewModels;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;
using ViewPrism2.Core.Services;
using ViewPrism2.Core.Services.Repair;
using ViewPrism2.Infrastructure.Imaging;
using ViewPrism2.Infrastructure.Scanning;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-UI-G10(M-UI-REPAIR-027): 修復ライフサイクル UI の ViewModel ロジックを unit 検査する
/// (criteria 検索フォーム・空条件非活性・relink 候補/確定・トラッシュ復元/完全削除・missing 化表示)。
/// golden G-10 の表面は承認者の目視だが、状態遷移・空条件・案内文言を UI から呼ぶだけにするため
/// ViewModel ロジックを分離して検査する(コードビハインド判定禁止)。
/// </summary>
[Trait("cp", "CP-UI-G10")]
public sealed class CpUiRepairViewModelTests
{
    private const string Folder = "folder-1";

    private static LocalizationService CreateLoc()
    {
        var ja = new Dictionary<string, string>
        {
            ["repair.relink.success"] = "再リンクしました",
            ["repair.relink.failed"] = "再リンクに失敗しました",
            ["trash.restore.success"] = "復元しました",
            ["trash.restore.missing"] = "リンク切れになりました",
            ["trash.restore.failed"] = "復元に失敗しました",
            ["trash.purge.success"] = "完全削除しました",
            ["trash.purge.failed"] = "完全削除に失敗しました",
            ["error.validationError"] = "入力が不正です",
            ["error.notFound"] = "見つかりません",
        };
        var resources = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["ja"] = ja,
        };
        return new LocalizationService(resources);
    }

    private sealed class AcceptingWindows : IWindowService
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

    private static async Task SeedFolderAsync(TempDb db, string path = "C:/f")
        => await db.Folders.AddAsync(new SyncFolder { Id = Folder, Name = "F", Path = path });

    private static RepairViewModel CreateRepairVm(TempDb db)
        => new(
            Folder,
            db.Images,
            new RelinkService(db.Images, db.Tags),
            CreateLoc(),
            new AcceptingWindows());

    // ---- 検索=再リンク候補探索に統一(GF-V4-03) ----

    [Fact]
    public async Task GF_V4_03_検索はmissing選択で活性_未選択で非活性()
    {
        using var db = new TempDb();
        await SeedFolderAsync(db);
        await db.Images.AddAsync(Image("m1", "old.png", ImageStatus.Missing, hash: "H1"));
        var vm = CreateRepairVm(db);
        await vm.LoadAsync();

        Assert.False(vm.CanSearchCandidates);          // missing 未選択 → 検索不可
        vm.SelectedMissing = vm.MissingImages.First();
        Assert.True(vm.CanSearchCandidates);
    }

    [Fact]
    public async Task GF_V4_03_検索は選択missingの候補をpending含めて再探索する()
    {
        using var db = new TempDb();
        await SeedFolderAsync(db);
        await db.Images.AddAsync(Image("m1", "sub/old.png", ImageStatus.Missing, hash: "H1", size: 100));
        // リネーム後ファイル=pending(scan 3a)。旧 Normal 限定検索では拾えないが候補探索なら出る
        await db.Images.AddAsync(Image("p1", "_old.png", ImageStatus.Pending, hash: "H1", size: 100));
        var vm = CreateRepairVm(db);
        await vm.LoadAsync();
        vm.SelectedMissing = vm.MissingImages.First();

        await vm.SearchAsync();   // = 現在条件で再リンク候補を再探索(Pending∪Normal)

        Assert.Single(vm.Candidates);
        Assert.Equal("p1", vm.Candidates.First().Candidate.ImageId);   // pending 候補が出る
    }

    // ---- relink フロー ----

    [Fact]
    public async Task relink候補提示と確定_missing側ID不変でstatusNormal()
    {
        using var db = new TempDb();
        await SeedFolderAsync(db);
        await db.Images.AddAsync(Image("missing-1", "old.jpg", ImageStatus.Missing, hash: "H1"));
        await db.Images.AddAsync(Image("pending-1", "new.jpg", ImageStatus.Pending, hash: "H1"));
        var vm = CreateRepairVm(db);
        await vm.LoadAsync();

        Assert.False(vm.HasNoMissing);
        vm.SelectedMissing = vm.MissingImages.First();
        // 候補ロードは selection 変更で発火するが、unit では awaitable 経路で確実に待つ
        await vm.RefreshCandidatesAsync();
        Assert.Single(vm.Candidates);

        vm.SelectedCandidate = vm.Candidates.First();
        await vm.CommitRelinkAsync();

        Assert.Equal("再リンクしました", vm.StatusMessage);
        var relinked = await db.Images.GetByIdAsync("missing-1");
        Assert.Equal("missing-1", relinked!.Id);
        Assert.Equal(ImageStatus.Normal, relinked.Status);
    }

    [Fact]
    public async Task GF_V4_01_missing選択でhash拡張子サイズを自動事前入力し候補を手入力なしで発見()
    {
        using var db = new TempDb();
        await SeedFolderAsync(db);
        // 移動/リネームで normal として再登録された同一内容ファイル(同一 hash・拡張子・サイズ)
        await db.Images.AddAsync(Image("missing-1", "sub/old.jpg", ImageStatus.Missing, hash: "H1", size: 100));
        await db.Images.AddAsync(Image("moved-1", "moved.jpg", ImageStatus.Normal, hash: "H1", size: 100));
        await db.Images.AddAsync(Image("other-1", "other.jpg", ImageStatus.Normal, hash: "HX", size: 999));
        var vm = CreateRepairVm(db);
        await vm.LoadAsync();

        // missing を選択 → criteria が自動で事前入力される(view-prism 既定 useHash/useExtension/useSize)
        vm.SelectedMissing = vm.MissingImages.First();
        Assert.Equal("H1", vm.HashInput);
        Assert.Equal(".jpg", vm.ExtensionInput);
        Assert.Equal("100", vm.SizeMinInput);
        Assert.Equal("100", vm.SizeMaxInput);
        Assert.Null(vm.NameContainsInput);    // ファイル名はリネームで変わるため OFF

        // 候補が手入力なしで自動発見される(同一 hash+拡張子+サイズの normal=moved-1。other-1 は除外)
        await vm.RefreshCandidatesAsync();
        Assert.Single(vm.Candidates);
        Assert.Equal("moved-1", vm.Candidates.First().Candidate.ImageId);
    }

    [Fact]
    public async Task GF_V4_02_自動修復可能数は候補がちょうど1件のmissingだけを数える()
    {
        using var db = new TempDb();
        await SeedFolderAsync(db);
        // M1: 候補ちょうど 1 件(同一 hash+拡張子+サイズの normal)→ 自動修復可能
        await db.Images.AddAsync(Image("m1", "sub/a.jpg", ImageStatus.Missing, hash: "H1", size: 100));
        await db.Images.AddAsync(Image("c1", "moved-a.jpg", ImageStatus.Normal, hash: "H1", size: 100));
        // M2: 候補 2 件(曖昧)→ 自動修復可能としない
        await db.Images.AddAsync(Image("m2", "sub/b.jpg", ImageStatus.Missing, hash: "H2", size: 200));
        await db.Images.AddAsync(Image("c2a", "dup-a.jpg", ImageStatus.Normal, hash: "H2", size: 200));
        await db.Images.AddAsync(Image("c2b", "dup-b.jpg", ImageStatus.Normal, hash: "H2", size: 200));
        // M3: 候補 0 件 → 自動修復可能としない
        await db.Images.AddAsync(Image("m3", "sub/c.jpg", ImageStatus.Missing, hash: "H3", size: 300));

        var vm = CreateRepairVm(db);
        await vm.LoadAsync();

        Assert.Equal(3, vm.MissingImages.Count);
        Assert.Equal(1, vm.AutoRepairableCount);   // M1 のみ(M2=2件曖昧・M3=0件 は除外)
    }

    // ---- トラッシュ復元/完全削除(TrashViewModel 拡張) ----

    private static TrashViewModel CreateTrashVm(TempDb db, IFilePresenceProbe probe)
        => new(
            Folder,
            db.Images,
            db.Folders,
            CreateLoc(),
            new TrashService(db.Images, db.Folders, probe),
            new AcceptingWindows());

    private sealed class FakeProbe(bool exists) : IFilePresenceProbe
    {
        public bool Exists(string absoluteImagePath) => exists;
    }

    [Fact]
    public async Task トラッシュ復元_物理存在でNormal化_成功文言()
    {
        using var db = new TempDb();
        await SeedFolderAsync(db);
        await db.Images.AddAsync(Image("d1", "d1.jpg", ImageStatus.Deleted));
        var vm = CreateTrashVm(db, new FakeProbe(exists: true));
        await vm.LoadAsync();

        Assert.False(vm.CanOperate);          // 未選択は操作不可
        vm.SelectedItem = vm.Items.First();
        Assert.True(vm.CanOperate);

        await vm.RestoreAsync();

        Assert.Equal("復元しました", vm.StatusMessage);
        Assert.Equal(0, vm.Count);            // 復元で deleted 一覧から消える
    }

    [Fact]
    public async Task トラッシュ復元_物理不在でMissing化_missing通知文言()
    {
        using var db = new TempDb();
        await SeedFolderAsync(db);
        var img = Image("d1", "d1.jpg", ImageStatus.Deleted);
        await db.Images.AddAsync(img);
        var vm = CreateTrashVm(db, new FakeProbe(exists: false));
        await vm.LoadAsync();
        vm.SelectedItem = vm.Items.First();

        await vm.RestoreAsync();

        Assert.Equal("リンク切れになりました", vm.StatusMessage); // 幽霊 normal 防止の結果表示
        Assert.Equal(ImageStatus.Missing, (await db.Images.GetByIdAsync("d1"))!.Status);
    }

    [Fact]
    public async Task トラッシュ完全削除_確認後にimages行とタグがCASCADE消滅()
    {
        using var db = new TempDb();
        await SeedFolderAsync(db);
        var img = Image("d1", "d1.jpg", ImageStatus.Deleted);
        await db.Images.AddAsync(img);
        var tag = new Tag { Id = "t1", Name = "T", Type = TagType.Simple };
        await db.Tags.AddAsync(tag);
        await db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = img.Id, TagId = tag.Id });

        var vm = CreateTrashVm(db, new FakeProbe(exists: false));
        await vm.LoadAsync();
        vm.SelectedItem = vm.Items.First();

        await vm.PurgeAsync();

        Assert.Equal("完全削除しました", vm.StatusMessage);
        Assert.Null(await db.Images.GetByIdAsync("d1"));
        Assert.Empty(await db.Tags.GetImageTagsAsync("d1"));
    }
}
