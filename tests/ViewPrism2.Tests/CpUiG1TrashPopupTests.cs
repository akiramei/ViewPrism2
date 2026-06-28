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
/// CP-UI-G1(unit 部分・ECO-019): ゴミ箱ポップアップ(トラッシュモーダルを画像タブ内オーバーレイへ作り直す)。
/// モック(ViewPrismUI:資料/画像タブ/ViewPrism2 画像タブゴミ箱ポップアップ.html)の挙動を回帰固定:
/// ⋯「ゴミ箱」で deleted 一覧を読み popup を開く / 複数選択・すべて選択 / 復元・完全削除・ゴミ箱を空。
/// 完全削除/空は確認(ConfirmAsync)を要し、INV-009 で物理ファイルは不変(DB 行のみ除去)。
/// </summary>
[Trait("cp", "CP-UI-G1")]
public sealed class CpUiG1TrashPopupTests : IDisposable
{
    private readonly TempDb _db = new();
    private SyncFolder _col = null!;

    public void Dispose() => _db.Dispose();

    private sealed class StubWindowService : IWindowService
    {
        public bool ConfirmResult { get; set; } = true;
        public int ConfirmCalls { get; private set; }
        public Task<bool> ConfirmAsync(string title, string message) { ConfirmCalls++; return Task.FromResult(ConfirmResult); }
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

    /// <summary>物理存在を固定で返すフェイク(復元の存在分岐 T6/T7 制御用)。</summary>
    private sealed class FakeProbe(bool exists) : IFilePresenceProbe
    {
        public bool Exists(string absoluteImagePath) => exists;
    }

    private StubWindowService _win = null!;

    /// <summary>normal/deleted を混在で投入(deleted は引数 deletedNames で指定)。probe で復元の存在分岐を制御。</summary>
    private async Task<ImageTabViewModel> NewAsync(string[] normalNames, string[] deletedNames, IFilePresenceProbe? probe = null)
    {
        _col = new SyncFolder { Id = IdGenerator.NewId(), Name = "C", Path = @"C:\col" };
        await _db.Folders.AddAsync(_col);
        async Task Add(string name, ImageStatus st) => await _db.Images.AddAsync(new ImageRecord
        {
            Id = name, // テスト可読性のため id=name
            SyncFolderId = _col.Id,
            RelativePath = name, FileName = name, FileSize = 10, Hash = new string('0', 64),
            Status = st, CreatedDate = "2026-06-11T00:00:00.000Z", ModifiedDate = "2026-06-11T00:00:00.000Z",
        });
        foreach (var n in normalNames) await Add(n, ImageStatus.Normal);
        foreach (var n in deletedNames) await Add(n, ImageStatus.Deleted);

        _win = new StubWindowService();
        var vm = new ImageTabViewModel(
            _db.Folders, _db.Images, _db.Tags, new ImageSorter(),
            new ViewService(_db.Views, _db.Clock), new NodeGraphBuilder(),
            new PathConditionConverter(), new ConditionEvaluator(),
            new SimilaritySearchService(_db.Folders, _db.Images, _db.Features, _db.Similarities, new FakePHashImageReader(), _db.Clock),
            new MergeService(_db.Images, _db.Tags, _db.Merges),
            new TrashService(_db.Images, _db.Folders, probe ?? new FilePresenceProbe()),
            _win, new AppSettings());
        await vm.InitializeAsync(_col.Id);
        return vm;
    }

    [Fact]
    public async Task ゴミ箱を開くとdeleted一覧を読み込みポップアップを表示する()
    {
        var vm = await NewAsync(["a.jpg"], ["x.jpg", "y.jpg"]);

        await vm.OpenTrashCommand.ExecuteAsync(null);

        Assert.True(vm.TrashOpen);
        Assert.True(vm.HasTrashItems);
        Assert.Equal(2, vm.TrashPopupCount);
        Assert.Equal(["x.jpg", "y.jpg"], vm.TrashPopupItems.Select(i => i.Name)); // 名前昇順
        Assert.False(vm.HasTrashSel);
        Assert.Equal("画像を選択して操作", vm.TrashSelCountLabel);
    }

    [Fact]
    public async Task 空のゴミ箱は空状態を表示する()
    {
        var vm = await NewAsync(["a.jpg"], []);
        await vm.OpenTrashCommand.ExecuteAsync(null);

        Assert.True(vm.TrashOpen);
        Assert.True(vm.TrashPopupEmpty);
        Assert.False(vm.HasTrashItems);
    }

    [Fact]
    public async Task すべて選択と選択解除がトグルする()
    {
        var vm = await NewAsync([], ["x.jpg", "y.jpg", "z.jpg"]);
        await vm.OpenTrashCommand.ExecuteAsync(null);

        vm.ToggleTrashSelectAllCommand.Execute(null);
        Assert.Equal(3, vm.TrashSelCount);
        Assert.Equal("選択を解除", vm.TrashSelectAllLabel);
        Assert.All(vm.TrashPopupItems, i => Assert.True(i.IsSelected));

        vm.ToggleTrashSelectAllCommand.Execute(null);
        Assert.Equal(0, vm.TrashSelCount);
        Assert.Equal("すべて選択", vm.TrashSelectAllLabel);
    }

    [Fact]
    public async Task 復元は選択をnormalへ戻しグリッド母集合へ復帰させる()
    {
        var vm = await NewAsync(["a.jpg"], ["x.jpg", "y.jpg"], new FakeProbe(exists: true));
        await vm.OpenTrashCommand.ExecuteAsync(null);
        vm.ToggleTrashItemCommand.Execute(vm.TrashPopupItems.Single(i => i.Name == "x.jpg"));

        await vm.RestoreSelectedTrashCommand.ExecuteAsync(null);

        // x は復元(物理存在 → normal)、ゴミ箱からは消える
        Assert.Equal(["y.jpg"], vm.TrashPopupItems.Select(i => i.Name));
        Assert.Equal(1, vm.TrashCount);
        var x = await _db.Images.GetByIdAsync("x.jpg");
        Assert.Equal(ImageStatus.Normal, x!.Status);
    }

    [Fact]
    public async Task 完全削除は確認を経てDB行を消す_物理は不変_確認却下なら何もしない()
    {
        var vm = await NewAsync([], ["x.jpg", "y.jpg"]);
        await vm.OpenTrashCommand.ExecuteAsync(null);
        vm.ToggleTrashItemCommand.Execute(vm.TrashPopupItems.Single(i => i.Name == "x.jpg"));

        // 確認却下 → 何もしない
        _win.ConfirmResult = false;
        await vm.PurgeSelectedTrashCommand.ExecuteAsync(null);
        Assert.Equal(1, _win.ConfirmCalls);
        Assert.NotNull(await _db.Images.GetByIdAsync("x.jpg"));

        // 確認承認 → DB 行削除(物理は TrashService が触れない=INV-009)
        _win.ConfirmResult = true;
        await vm.PurgeSelectedTrashCommand.ExecuteAsync(null);
        Assert.Null(await _db.Images.GetByIdAsync("x.jpg"));
        Assert.Equal(["y.jpg"], vm.TrashPopupItems.Select(i => i.Name));
    }

    [Fact]
    public async Task ゴミ箱を空は確認を経て全deletedを完全削除する()
    {
        var vm = await NewAsync(["a.jpg"], ["x.jpg", "y.jpg"]);
        await vm.OpenTrashCommand.ExecuteAsync(null);

        await vm.EmptyTrashCommand.ExecuteAsync(null);

        Assert.Equal(1, _win.ConfirmCalls);
        Assert.True(vm.TrashPopupEmpty);
        Assert.Equal(0, vm.TrashCount);
        Assert.Null(await _db.Images.GetByIdAsync("x.jpg"));
        Assert.Null(await _db.Images.GetByIdAsync("y.jpg"));
        Assert.NotNull(await _db.Images.GetByIdAsync("a.jpg")); // normal は無傷
    }
}
