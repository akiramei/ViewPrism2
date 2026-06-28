using CommunityToolkit.Mvvm.Input;
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
/// CP-UI-G1(unit 部分・ECO-014): 整理モード(類似+マージ統合「整理トレイ」)の VM 契約。
/// タグ編集モードとの排他、グリッドクリックでのマージ先/整理対象割当、整理対象の昇格、
/// 条件検索(E-CRITERIA-037)による候補抽出、マージ実行(E-MERGE-034 原子・タグ union・
/// source=deleted・物理非破壊 INV-009)、完了状態とトレイのリセットを実 DB + 実 Core サービスで検査する。
/// 描画(整理トレイ・検索結果カード・完了状態)は surface 増分後の golden(承認者 maintainer)。
/// 類似検索 find は pHash 実体が必要なため本層では検査せず、surface 増分の golden/結合で確認する。
/// </summary>
[Trait("cp", "CP-UI-G1")]
public sealed class CpUiG1OrganizeTests : IDisposable
{
    private readonly TempDb _db = new();
    private SyncFolder _col = null!;
    private readonly Dictionary<string, string> _idByName = new(StringComparer.Ordinal);

    public void Dispose() => _db.Dispose();

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

    private async Task<ImageTabViewModel> NewVmAsync(params string[] names)
    {
        _col = new SyncFolder { Id = IdGenerator.NewId(), Name = "C", Path = @"C:\col" };
        await _db.Folders.AddAsync(_col);
        foreach (var name in names)
        {
            var id = IdGenerator.NewId();
            _idByName[name] = id;
            await _db.Images.AddAsync(new ImageRecord
            {
                Id = id,
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
            new StubWindowService(), new AppSettings());
        await vm.InitializeAsync(_col.Id);
        return vm;
    }

    private static ImageItemVM Item(ImageTabViewModel vm, string name)
        => vm.Items.Single(i => !i.IsFolder && i.Name == name);

    private string Id(string name) => _idByName[name];

    [Fact]
    public async Task 整理モードはタグ編集モードと排他になる()
    {
        var vm = await NewVmAsync("a.jpg", "b.jpg");

        vm.ToggleOrganizeCommand.Execute(null);
        Assert.True(vm.OrganizeMode);
        Assert.False(vm.EditMode);
        Assert.True(vm.IsOrganizeContext);
        Assert.True(vm.ShowRightPane);

        // タグ編集へ切替えると整理は終了する
        vm.ToggleEditCommand.Execute(null);
        Assert.False(vm.OrganizeMode);
        Assert.True(vm.EditMode);

        // 整理へ戻すとタグ編集は終了する
        vm.ToggleOrganizeCommand.Execute(null);
        Assert.True(vm.OrganizeMode);
        Assert.False(vm.EditMode);

        // もう一度押すと整理を終了
        vm.ToggleOrganizeCommand.Execute(null);
        Assert.False(vm.OrganizeMode);
    }

    [Fact]
    public async Task グリッドクリックはマージ先を決めてから整理対象をトグルする()
    {
        var vm = await NewVmAsync("a.jpg", "b.jpg", "c.jpg");
        vm.ToggleOrganizeCommand.Execute(null);

        Assert.True(vm.ShowMergeTargetPrompt);    // 最初はマージ先を促す
        Assert.False(vm.CanExecuteMerge);

        vm.HandleItemClick(Item(vm, "a.jpg"), ctrl: false, shift: false); // 1枚目=マージ先
        Assert.True(vm.HasMergeTarget);
        Assert.Equal("a.jpg", vm.MergeTarget!.Name);
        Assert.False(vm.ShowMergeTargetPrompt);
        Assert.True(vm.ShowOrganizeTargetsPrompt);  // 相手がまだいない

        vm.HandleItemClick(Item(vm, "b.jpg"), ctrl: false, shift: false); // 2枚目=整理対象
        vm.HandleItemClick(Item(vm, "c.jpg"), ctrl: false, shift: false); // 3枚目=整理対象
        Assert.Equal(2, vm.OrganizeTargets.Count);
        Assert.Equal("2 枚", vm.OrganizeTargetsCountLabel);
        Assert.True(vm.CanExecuteMerge);
        Assert.Equal("マージを実行（2 枚）", vm.MergeButtonLabel);

        // マージ先の再クリックは無操作(整理対象に落ちない)
        vm.HandleItemClick(Item(vm, "a.jpg"), ctrl: false, shift: false);
        Assert.Equal("a.jpg", vm.MergeTarget!.Name);
        Assert.Equal(2, vm.OrganizeTargets.Count);

        // 整理対象の再クリックで解除
        vm.HandleItemClick(Item(vm, "b.jpg"), ctrl: false, shift: false);
        Assert.Single(vm.OrganizeTargets);
        Assert.Equal("c.jpg", vm.OrganizeTargets[0].Name);
    }

    [Fact]
    public async Task 整理対象の昇格でマージ先が入れ替わる()
    {
        var vm = await NewVmAsync("a.jpg", "b.jpg", "c.jpg");
        vm.ToggleOrganizeCommand.Execute(null);
        vm.HandleItemClick(Item(vm, "a.jpg"), false, false); // マージ先 a
        vm.HandleItemClick(Item(vm, "b.jpg"), false, false); // 整理対象 b
        vm.HandleItemClick(Item(vm, "c.jpg"), false, false); // 整理対象 c

        vm.PromoteToMergeTargetCommand.Execute(Id("b.jpg")); // b をマージ先へ昇格

        Assert.Equal("b.jpg", vm.MergeTarget!.Name);          // 新マージ先 = b
        var targetNames = vm.OrganizeTargets.Select(t => t.Name).OrderBy(n => n).ToList();
        Assert.Equal(["a.jpg", "c.jpg"], targetNames);        // 元マージ先 a は整理対象へ戻る
    }

    [Fact]
    public async Task 条件検索はマージ先を除いた候補を一致表示で返す()
    {
        var vm = await NewVmAsync("dup1.jpg", "dup2.jpg", "dup3.jpg", "other.jpg");
        vm.ToggleOrganizeCommand.Execute(null);
        vm.HandleItemClick(Item(vm, "dup1.jpg"), false, false); // マージ先 dup1

        vm.SetSearchMethodCommand.Execute("criteria");
        Assert.True(vm.IsCriteriaMethod);
        vm.CriteriaName = "dup";
        await vm.RunSearchCommand.ExecuteAsync(null);

        Assert.True(vm.ShowSearchResults);
        var names = vm.SearchResults.Select(r => r.Name).OrderBy(n => n).ToList();
        Assert.Equal(["dup2.jpg", "dup3.jpg"], names); // dup1(マージ先)と other は出ない
        Assert.All(vm.SearchResults, r => Assert.Equal("条件一致", r.ScoreText));

        // 候補を整理対象へ追加
        vm.AddCandidateToTargetsCommand.Execute(Id("dup2.jpg"));
        Assert.Single(vm.OrganizeTargets);
        Assert.Equal("dup2.jpg", vm.OrganizeTargets[0].Name);
        Assert.True(vm.SearchResults.Single(r => r.Name == "dup2.jpg").Added);
    }

    [Fact]
    public async Task マージ実行で整理対象が削除され完了状態になりタグが統合される()
    {
        var vm = await NewVmAsync("keep.jpg", "drop.jpg");

        // drop.jpg にタグを付与しておく(マージで keep.jpg へ union されることを確認)
        var tagService = new TagService(_db.Tags);
        var tag = await tagService.CreateAsync("お気に入り", TagType.Simple);
        Assert.True(tag.IsSuccess);
        Assert.True((await tagService.TagImageAsync(Id("drop.jpg"), tag.Value!.Id, null)).IsSuccess);

        vm.ToggleOrganizeCommand.Execute(null);
        vm.HandleItemClick(Item(vm, "keep.jpg"), false, false); // マージ先
        vm.HandleItemClick(Item(vm, "drop.jpg"), false, false); // 整理対象

        await vm.ExecuteMergeCommand.ExecuteAsync(null);

        Assert.True(vm.OrganizeDone);
        Assert.Equal("2 枚を 1 枚へまとめ、1 枚を削除しました。", vm.DoneSummary);
        Assert.False(vm.CanUndo); // 取り消しは IMG-011(別 ECO)

        // source=drop は deleted 化し母集合から外れる(物理非破壊・論理操作)
        var normals = (await _db.Images.GetAllNormalAsync()).Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain(Id("drop.jpg"), normals);
        Assert.Contains(Id("keep.jpg"), normals);
        Assert.DoesNotContain(vm.Items, i => i.Name == "drop.jpg");

        // タグ union: keep.jpg が drop.jpg のタグを引き継ぐ(E-MERGE-034 INV-011)
        var keepTags = (await _db.Tags.GetAllImageTagsAsync())
            .Where(it => it.ImageId == Id("keep.jpg")).Select(it => it.TagId).ToList();
        Assert.Contains(tag.Value.Id, keepTags);
    }

    [Fact]
    public async Task 別の整理を続けるで完了状態とトレイがリセットされる()
    {
        var vm = await NewVmAsync("keep.jpg", "drop.jpg");
        vm.ToggleOrganizeCommand.Execute(null);
        vm.HandleItemClick(Item(vm, "keep.jpg"), false, false);
        vm.HandleItemClick(Item(vm, "drop.jpg"), false, false);
        await vm.ExecuteMergeCommand.ExecuteAsync(null);
        Assert.True(vm.OrganizeDone);

        vm.ContinueOrganizeCommand.Execute(null);

        Assert.False(vm.OrganizeDone);
        Assert.True(vm.OrganizeMode);            // 整理モードは維持
        Assert.False(vm.HasMergeTarget);         // トレイは空に戻る
        Assert.Empty(vm.OrganizeTargets);
        Assert.True(vm.ShowMergeTargetPrompt);
    }
}
