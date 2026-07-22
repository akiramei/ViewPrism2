using CommunityToolkit.Mvvm.Input;
using ViewPrism2.App.Services;
using ViewPrism2.App.ViewModels;
using ViewPrism2.Core.Models;
using ViewPrism2.Infrastructure.Scanning;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-UI-G1(ECO-133・Codex P2): 二段階スキャン適用の失敗回復。ApplyCoreAsync は 512 件独立
/// トランザクションのため部分適用があり得る(残余は次回スキャンが収束=REQ-100)。適用が失敗したら
/// 同一 stale staging の再試行を禁止し(既 INSERT 行への再 INSERT が UNIQUE 衝突で残余へ到達不能)、
/// 再スキャンへ導くことを固定する。
/// </summary>
[Trait("cp", "CP-UI-G1")]
public sealed class CpScanApplyRetryGuardTests : IDisposable
{
    private readonly TempDb _db = new();

    public void Dispose() => _db.Dispose();

    // AddedPending=1 で TotalChanges>0(CanApply=true)。Adds=[dup] は既存 dup.jpg と同一パスのため
    // INSERT が UNIQUE(sync_folder_id, relative_path)で衝突=適用失敗を実 DB で再現する。
    private static ScanStaging FailingAddStaging(string folderId, ImageRecord dupAdd) => new()
    {
        FolderId = folderId,
        ManagedTotal = 1,
        ScannedFiles = 1,
        Unchanged = 0,
        ContentChanged = 0,
        AddedPending = 1,
        Reappeared = 0,
        MissingFromNormal = 0,
        MissingFromPending = 0,
        PreexistingMissing = 0,
        DeletedUnchanged = 0,
        DeletedMetaRefreshed = 0,
        PendedWithoutMeta = 0,
        ReadFailures = 0,
        Adds = [dupAdd],
        MetaUpdates = [],
        StatusUpdates = [],
        Deletes = [],
        Examples = [],
    };

    private static ImageRecord Img(string id, string folderId, string path, ImageStatus status) => new()
    {
        Id = id,
        SyncFolderId = folderId,
        RelativePath = path,
        FileName = path,
        FileSize = 10,
        Hash = new string('0', 64),
        Status = status,
        CreatedDate = "2026-01-01T00:00:00.000Z",
        ModifiedDate = "2026-01-01T00:00:00.000Z",
    };

    [Fact]
    public async Task 適用が失敗すると同一stagingの再適用を禁止し再スキャンへ導く()
    {
        var folder = new SyncFolder { Id = "folder-1", Name = "f", Path = @"C:\p" };
        Assert.True((await _db.Folders.AddAsync(folder)).IsSuccess);
        await _db.Images.AddAsync(Img("existing", folder.Id, "dup.jpg", ImageStatus.Normal)); // 衝突の種

        var dupAdd = Img("new-id", folder.Id, "dup.jpg", ImageStatus.Pending); // 同一パス= INSERT で UNIQUE 違反
        var coordinator = new ScanCoordinator(new ScanService(_db.Folders, _db.Images, _db.Clock));
        var vm = new ScanSummaryViewModel(coordinator, TestLoc.Ja(), new NullWindows(), folder);
        vm.PresentSummary(FailingAddStaging(folder.Id, dupAdd));
        Assert.True(vm.CanApply); // 初期は適用可

        await ((IAsyncRelayCommand)vm.ApplyCommand).ExecuteAsync(null); // 適用= UNIQUE で失敗

        // 失敗後、同一 staging の再試行を禁止(CanApply=false)+失敗/誘導メッセージ
        Assert.False(vm.CanApply);
        Assert.NotNull(vm.StatusMessage);

        // 再度 Apply を叩いても走らない(残余未到達の行き詰まりを作らない)
        await ((IAsyncRelayCommand)vm.ApplyCommand).ExecuteAsync(null);
        Assert.False(vm.CanApply);
    }

    private sealed class NullWindows : IWindowService
    {
        public Task<bool> ConfirmAsync(string title, string message, string confirmLabel,
            bool destructive = false, string? cancelLabel = null) => Task.FromResult(true);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task ShowFolderManagementAsync() => Task.CompletedTask;
        public Task ShowSettingsAsync() => Task.CompletedTask;
        public Task ShowSnapshotsAsync() => Task.CompletedTask;
        public Task<bool> ShowTagEditorAsync(Tag? existing) => Task.FromResult(false);
        public Task<bool> ShowViewEditDialogAsync(View? existing) => Task.FromResult(false);
        public Task<IReadOnlyList<string>?> ShowNumericValueDialogAsync(
            Tag tag, NumericTagSettings? settings, int imageCount)
            => Task.FromResult<IReadOnlyList<string>?>(null);
        public Task<NodeConditionResult?> ShowNodeConditionDialogAsync(
            Tag tag, HierarchyConditionType? conditionType, string? conditionValueJson)
            => Task.FromResult<NodeConditionResult?>(null);
        public Task ShowRelinkAsync(string folderId) => Task.CompletedTask;
        public void ShowViewer(IReadOnlyList<ImageEntry> ordered, int startIndex) { }
    }
}
