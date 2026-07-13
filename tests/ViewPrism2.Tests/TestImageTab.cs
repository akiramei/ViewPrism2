using ViewPrism2.App.Services;
using ViewPrism2.App.ViewModels;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using ViewPrism2.Core.Services.Repair;
using ViewPrism2.Core.Services.Similarity;
using ViewPrism2.Infrastructure.Imaging;

namespace ViewPrism2.Tests;

/// <summary>
/// ImageTabView を描画して文言/構造を検査するテスト向けの ImageTabViewModel ビルダー(ECO-079)。
/// 直書き文言を Loc[key] バインドへ移行したため、View 描画には実 loc(ja)を持つ VM の DataContext が要る。
/// </summary>
internal static class TestImageTab
{
    public static ImageTabViewModel NewVm(TempDb db) =>
        new(db.Folders, db.Images, db.Tags, new ImageSorter(),
            new ViewService(db.Views, db.Clock), new NodeGraphBuilder(),
            new PathConditionConverter(), new ConditionEvaluator(),
            new SimilaritySearchService(db.Folders, db.Images, db.Features, db.Similarities, new FakePHashImageReader(), db.Clock),
            new MergeService(db.Images, db.Tags, db.Merges),
            new TrashService(db.Images, db.Folders, new FilePresenceProbe()),
            new StubWindows(), new AppSettings(), new WorkspaceService(db.Workspaces, db.Clock), TestLoc.Ja());

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
