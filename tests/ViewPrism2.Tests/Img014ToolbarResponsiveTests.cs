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
/// IMG-014: 画像タブ ツールバーの狭幅レスポンシブ収納。判定は「ツールバー実測幅」で行い、段階は
/// 通常 → ラベル畳み(&lt;820) → 「整理」を⋯へ退避(&lt;640) → 折り返し(XAML)。確定契約(px 値でなく挙動):
/// ①どの幅でも重ならない ②畳む順序=ラベル→退避 ③離脱/実行ラベルは維持(モード中はラベル畳まない)。
/// 本テストは VM 側の段階ロジック(ReportToolbarWidth + ヒステリシス + 不変条件)を回帰固定する。
/// </summary>
[Trait("cp", "CP-TOOLBAR-RESPONSIVE-027")]
public sealed class Img014ToolbarResponsiveTests : IDisposable
{
    private readonly TempDb _db = new();

    public void Dispose() => _db.Dispose();

    private sealed class NullWindowService : IWindowService
    {
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(true);
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

    private async Task<ImageTabViewModel> NewVmAsync()
    {
        var col = new SyncFolder { Id = IdGenerator.NewId(), Name = "C", Path = @"C:\col" };
        await _db.Folders.AddAsync(col);
        foreach (var name in new[] { "a.jpg", "b.jpg" })
        {
            await _db.Images.AddAsync(new ImageRecord
            {
                Id = IdGenerator.NewId(),
                SyncFolderId = col.Id,
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
            new NullWindowService(), new AppSettings(), new WorkspaceService(_db.Workspaces, _db.Clock), TestLoc.Ja());
        await vm.InitializeAsync(col.Id);
        return vm;
    }

    [Fact]
    public async Task シード前は広い扱いで畳まない()
    {
        var vm = await NewVmAsync();
        Assert.False(vm.CollapseEntryLabels);
        Assert.False(vm.StowOrganizeToMenu);
        Assert.True(vm.ShowOrganizeEntryButton); // 通常閲覧=整理はツールバー上
    }

    [Fact]
    public async Task 広い幅ではすべてラベル表示_整理はツールバー上()
    {
        var vm = await NewVmAsync();
        vm.ReportToolbarWidth(1000);
        Assert.False(vm.CollapseEntryLabels);
        Assert.False(vm.StowOrganizeToMenu);
        Assert.True(vm.ShowOrganizeEntryButton);
    }

    [Fact]
    public async Task tier1_820px未満で入口ラベルを畳む_整理はまだツールバー上()
    {
        var vm = await NewVmAsync();
        vm.ReportToolbarWidth(800); // < 820, >= 640
        Assert.True(vm.CollapseEntryLabels);       // ラベル畳み
        Assert.False(vm.StowOrganizeToMenu);       // まだ退避しない(順序=ラベルが先)
        Assert.True(vm.ShowOrganizeEntryButton);
    }

    [Fact]
    public async Task tier2_640px未満で整理を退避_ラベルも畳んだまま()
    {
        var vm = await NewVmAsync();
        vm.ReportToolbarWidth(600); // < 640
        Assert.True(vm.CollapseEntryLabels);
        Assert.True(vm.StowOrganizeToMenu);        // ⋯ へ退避
        Assert.False(vm.ShowOrganizeEntryButton);  // ツールバー上の整理入口は消える
    }

    [Fact]
    public async Task 畳む順序はラベルが先_退避が後()
    {
        var vm = await NewVmAsync();
        // 820 と 640 の中間: ラベルは畳むが退避はしない(いきなり退避/折り返しに逃げない=契約②)
        vm.ReportToolbarWidth(700);
        Assert.True(vm.CollapseEntryLabels);
        Assert.False(vm.StowOrganizeToMenu);
    }

    [Fact]
    public async Task ヒステリシスでしきい値近傍のばたつきを抑える()
    {
        var vm = await NewVmAsync();
        vm.ReportToolbarWidth(800);           // 畳む
        Assert.True(vm.CollapseEntryLabels);

        vm.ReportToolbarWidth(830);           // 820 を少し超えただけ(< 820+band) → 畳んだまま
        Assert.True(vm.CollapseEntryLabels);

        vm.ReportToolbarWidth(900);           // band を十分超える → 戻る
        Assert.False(vm.CollapseEntryLabels);
    }

    [Fact]
    public async Task モード中は狭くても入口ラベルを畳まない_退避もしない()
    {
        var vm = await NewVmAsync();
        vm.ReportToolbarWidth(600);           // 極狭
        vm.ToggleEditCommand.Execute(null);   // タグ編集モードへ(離脱導線=「タグ編集を終了」)

        // 契約③: モード中の可視ボタンは離脱/実行導線なのでラベル維持
        Assert.False(vm.CollapseEntryLabels);
        Assert.False(vm.StowOrganizeToMenu);
    }

    [Fact]
    public async Task 整理モード中は狭くても整理を終了をツールバー上に維持()
    {
        var vm = await NewVmAsync();
        vm.ReportToolbarWidth(600);            // 退避しきい値未満
        vm.ToggleOrganizeCommand.Execute(null); // 整理モードへ(ボタンは「整理を終了」)

        // 整理モード中の「整理を終了」は離脱導線=退避しない(ツールバー上に残す)
        Assert.False(vm.StowOrganizeToMenu);
        Assert.True(vm.ShowOrganizeEntryButton);
        Assert.False(vm.CollapseEntryLabels);
    }

    [Fact]
    public async Task 不正な幅は無視する()
    {
        var vm = await NewVmAsync();
        vm.ReportToolbarWidth(800);
        Assert.True(vm.CollapseEntryLabels);

        vm.ReportToolbarWidth(0);       // 未レイアウト等
        vm.ReportToolbarWidth(-5);
        vm.ReportToolbarWidth(double.NaN);
        Assert.True(vm.CollapseEntryLabels); // 直前の段階を維持
    }

    // ---- tier3 回り込み(Grid リフロー)の配置マッピング ----

    [Fact]
    public async Task 既定は非回り込み_右クラスタは同段col1右寄せ()
    {
        var vm = await NewVmAsync();
        Assert.False(vm.ToolbarWrapped);
        Assert.Equal(1, vm.LeftClusterColumnSpan);
        Assert.Equal(0, vm.RightClusterRow);
        Assert.Equal(1, vm.RightClusterColumn);
        Assert.Equal(1, vm.RightClusterColumnSpan);
    }

    [Fact]
    public async Task 回り込み時は右クラスタを下段全幅へ_左は全幅()
    {
        var vm = await NewVmAsync();
        vm.SetToolbarWrapped(true);
        Assert.True(vm.ToolbarWrapped);
        Assert.Equal(2, vm.LeftClusterColumnSpan);  // 左は row0 全幅
        Assert.Equal(1, vm.RightClusterRow);        // 右は下段
        Assert.Equal(0, vm.RightClusterColumn);
        Assert.Equal(2, vm.RightClusterColumnSpan); // 右も全幅(内部で右寄せ)

        vm.SetToolbarWrapped(false); // 戻せば元の同段配置
        Assert.False(vm.ToolbarWrapped);
        Assert.Equal(1, vm.LeftClusterColumnSpan);
        Assert.Equal(0, vm.RightClusterRow);
        Assert.Equal(1, vm.RightClusterColumn);
    }

    // ---- 表示列はモード中隠す(CAD: 残すのは表示軸・ソート・グリッド/リスト・終了だけ) ----

    [Fact]
    public async Task FS軸では表示列入口は出さない()
    {
        var vm = await NewVmAsync(); // 既定=FS 軸(書き戻し先が無い)
        Assert.False(vm.CanEditColumns);
        Assert.False(vm.ShowColumnsEntry);
    }

    [Fact]
    public async Task 表示列入口はモード中は隠れる_ShowColumnsEntryはCanEditColumnsとモードのAND()
    {
        // CanEditColumns=false(FS 軸)でも、ShowColumnsEntry がモードで false になる不変を確認。
        // (view 軸+ビュー選択で CanEditColumns=true になるケースの視覚は golden で担保)
        var vm = await NewVmAsync();
        vm.ToggleEditCommand.Execute(null);
        Assert.True(vm.InAnyMode);
        Assert.False(vm.ShowColumnsEntry); // モード中は常に隠す
        vm.ToggleEditCommand.Execute(null);
        Assert.False(vm.InAnyMode);
        Assert.Equal(vm.CanEditColumns, vm.ShowColumnsEntry); // 非モードでは CanEditColumns に一致
    }
}
