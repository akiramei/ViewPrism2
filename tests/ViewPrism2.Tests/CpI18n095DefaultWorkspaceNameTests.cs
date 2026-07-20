using ViewPrism2.Core.Services;
using ViewPrism2.Core.Services.Repair;
using ViewPrism2.Core.Services.Similarity;
using ViewPrism2.App.Services;
using ViewPrism2.App.ViewModels;
using ViewPrism2.Core.Models;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// ECO-095(案A): デフォルト作業スペース名の表示時解決。
/// DB シード名(WorkspaceService.DefaultName=「デフォルト」= UI ロケール文字列の永続焼き込み)を
/// 表示に使わず、is_default=1 の行は Loc(common.default)で解決する — DB 不変・切替に即追随・
/// 既存 DB も直る。適用面= サイドバー行/中央ヘッダー WsName/移動先メニュー(全数走査=ECO-095 §3)。
/// 非デフォルト(ユーザー名・回転降格の時刻名)は DB 名のまま(pin)。
/// </summary>
[Trait("cp", "CP-I18N-010")]
public sealed class CpI18n095DefaultWorkspaceNameTests : IDisposable
{
    private readonly TempDb _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task en初期化_デフォルト行とヘッダーがDefault表示になる()
    {
        var (vm, _) = NewVm(TestLoc.En());
        await vm.InitializeAsync();

        var row = Assert.Single(vm.Workspaces, r => r.IsDefault);
        Assert.Equal("Default", row.Name);     // 是正前=「デフォルト」(DB 焼き込み名)で赤
        Assert.Equal("Default", vm.WsName);
    }

    [Fact]
    public async Task ja初期化_デフォルト行はデフォルト表示のまま()
    {
        var (vm, _) = NewVm(TestLoc.Ja());
        await vm.InitializeAsync();

        var row = Assert.Single(vm.Workspaces, r => r.IsDefault);
        Assert.Equal("デフォルト", row.Name); // ja 視覚不変 pin
        Assert.Equal("デフォルト", vm.WsName);
    }

    [Fact]
    public async Task 言語切替_行とヘッダーが再解決で追随する()
    {
        var loc = TestLoc.Ja();
        var (vm, _) = NewVm(loc);
        await vm.InitializeAsync();
        Assert.Equal("デフォルト", vm.WsName);

        loc.SetLocale("en");

        var row = Assert.Single(vm.Workspaces, r => r.IsDefault);
        Assert.Equal("Default", row.Name);     // 是正前=再構築なしで赤
        Assert.Equal("Default", vm.WsName);

        loc.SetLocale("ja"); // 往復
        row = Assert.Single(vm.Workspaces, r => r.IsDefault);
        Assert.Equal("デフォルト", row.Name);
    }

    [Fact]
    public async Task 回転後_非デフォルトは時刻名のまま_移動先のデフォルトはDefault表示()
    {
        var (vm, service) = NewVm(TestLoc.En());
        await vm.InitializeAsync();

        // デフォルト回転(ACT-0074): 旧デフォルトは時刻名へ降格・新デフォルトが is_default=1
        await service.CreateRotatingDefaultAsync();
        await vm.RefreshAsync();

        var demoted = Assert.Single(vm.Workspaces, r => !r.IsDefault);
        Assert.NotEqual("Default", demoted.Name);     // 降格行(時刻名)は DB 名のまま(pin)
        Assert.NotEqual("デフォルト", demoted.Name);

        // 非デフォルトを現スペースにすると移動先にデフォルトが現れる — そこも表示解決される
        await vm.SelectWorkspaceCommand.ExecuteAsync(demoted.Id);
        var target = Assert.Single(vm.MoveTargets);
        Assert.Equal("Default", target.Name);         // 是正前=「デフォルト」で赤
    }

    // ---- ヘルパ(CpUi094 と同系) ----

    private (WorkTabViewModel Vm, WorkspaceService Service) NewVm(LocalizationService loc)
    {
        var service = new WorkspaceService(_db.Workspaces, _db.Clock);
        var vm = new WorkTabViewModel(service, _db.Folders, _db.Tags,
            new SimilaritySearchService(_db.Folders, _db.Images, _db.Features, _db.Similarities, new FakePHashImageReader(), _db.Clock),
            new MergeService(_db.Images, _db.Tags, _db.Merges),
            new TrashService(_db.Images, _db.Folders, new AlwaysPresentProbe()),
            new NullWindows(), new ImageSorter(), new AppSettings(), loc);
        return (vm, service);
    }

    private sealed class AlwaysPresentProbe : IFilePresenceProbe
    {
        public bool Exists(string absoluteImagePath) => true;
    }

    private sealed class NullWindows : IWindowService
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
    }
}
