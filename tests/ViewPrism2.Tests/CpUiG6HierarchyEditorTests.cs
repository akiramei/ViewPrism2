using CommunityToolkit.Mvvm.Input;
using ViewPrism2.App.Services;
using ViewPrism2.App.ViewModels;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-UI-G6(unit 部): 階層構造エディタのダーティ追跡とバッチ保存(M-UI-013 v1.2、仕様 §2.6)。
/// 編集はメモリ内 → 「保存」で一括コミット(REQ-032 の modified_at 更新は保存時に 1 回)、
/// 「キャンセル」は確認後に破棄。未保存変更がある間のみ保存/キャンセルが活性。
/// 描画(3 ペイン構成・D&D)は golden(承認者 maintainer)。
/// </summary>
[Trait("cp", "CP-UI-G6")]
public sealed class CpUiG6HierarchyEditorTests : IDisposable
{
    private readonly FakeClock _clock = new(new DateTime(2026, 6, 11, 0, 0, 0));
    private readonly TempDb _db;
    private readonly ViewService _views;
    private readonly TagService _tagService;
    private readonly LocalizationService _localization;
    private readonly StubWindows _windows = new();

    public CpUiG6HierarchyEditorTests()
    {
        _db = new TempDb(_clock);
        _views = new ViewService(_db.Views, _clock);
        _tagService = new TagService(_db.Tags);
        _localization = new LocalizationService(
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["ja"] = new Dictionary<string, string>
                {
                    ["success.saved"] = "保存しました",
                    ["modals.confirmDiscard.title"] = "変更の破棄",
                    ["modals.confirmDiscard.message"] = "変更があります。破棄してよろしいですか？",
                },
            });
    }

    public void Dispose() => _db.Dispose();

    // ---- 未保存編集に載っているタグの削除拒否(REQ-083 / ECO-046・TAG-008 U-a 裁定) ----

    [Fact]
    public async Task 未保存の階層編集に載っているタグの削除は拒否される()
    {
        // ECO-046(U-a): DB 参照ガード(ECO-045)は未コミットの編集状態を関知できない谷間の掃射。
        // 是正前は削除が成功し、保存時に FK 違反の未処理例外になっていた(maintainer 実機 2026-07-05)
        var tag = (await _tagService.CreateAsync("placed-unsaved", TagType.Simple)).Value!;
        var view = (await _views.CreateAsync("V")).Value!;

        var tab = new TagsTabViewModel(_views, _tagService, _db.Tags, _localization, _windows);
        await tab.Editor.LoadAsync(view, new Dictionary<string, Tag>(StringComparer.Ordinal) { [tag.Id] = tag });
        tab.Editor.AddNode(tag, null);
        Assert.True(tab.Editor.IsDirty);

        await tab.Palette.LoadAsync();
        var row = Assert.Single(tab.Palette.Tags, r => r.Tag.Id == tag.Id);
        await tab.Palette.DeleteCommand.ExecuteAsync(row);

        Assert.NotNull(await _db.Tags.GetByIdAsync(tag.Id));   // 定義無傷(削除されない)
        Assert.Equal(0, _windows.ConfirmCount);                // 確認ダイアログの前に拒否
        Assert.NotNull(tab.Palette.StatusMessage);             // 理由提示
        Assert.True(tab.Editor.IsDirty);                       // 編集ツリー無傷
    }

    [Fact]
    public async Task 削除拒否メッセージはロケール切替へ追随する()
    {
        // ECO-106: StatusMessage が Resolve 済み文字列だと言語切替に追随しない(ECO-104 1.2 の同族・
        // maintainer 実機所見 2026-07-17)。実アセット ja/en で往復を検査する(表示時解決)。
        var loc = TestLoc.Ja();
        var tag = (await _tagService.CreateAsync("placed-unsaved-i18n", TagType.Simple)).Value!;
        var view = (await _views.CreateAsync("V6")).Value!;

        var tab = new TagsTabViewModel(_views, _tagService, _db.Tags, loc, _windows);
        await tab.Editor.LoadAsync(view, new Dictionary<string, Tag>(StringComparer.Ordinal) { [tag.Id] = tag });
        tab.Editor.AddNode(tag, null);
        await tab.Palette.LoadAsync();
        var row = Assert.Single(tab.Palette.Tags, r => r.Tag.Id == tag.Id);
        await tab.Palette.DeleteCommand.ExecuteAsync(row);

        Assert.Equal(
            "編集中のビュー階層に配置されているタグは削除できません。配置を外すか、編集をキャンセルしてください",
            tab.Palette.StatusMessage);

        loc.SetLocale("en");
        Assert.Equal(
            "This tag is placed in the view hierarchy being edited and cannot be deleted. Remove the placement or cancel the edit first",
            tab.Palette.StatusMessage);

        loc.SetLocale("ja");                                   // 往復も追随
        Assert.Equal(
            "編集中のビュー階層に配置されているタグは削除できません。配置を外すか、編集をキャンセルしてください",
            tab.Palette.StatusMessage);
    }

    [Fact]
    public async Task 保存済みで編集がダーティでない配置タグの削除はDBガードに委ねる()
    {
        // U-a の判定は「dirty な編集ツリー」限定 — 保存済み配置は ECO-045 の DB ガード(TagInUse)が拒否する
        var tag = (await _tagService.CreateAsync("placed-saved", TagType.Simple)).Value!;
        var view = (await _views.CreateAsync("V")).Value!;
        Assert.True((await _views.AddNodeAsync(view.Id, tag.Id, null, 0)).IsSuccess);

        var tab = new TagsTabViewModel(_views, _tagService, _db.Tags, _localization, _windows);
        await tab.Editor.LoadAsync(view, new Dictionary<string, Tag>(StringComparer.Ordinal) { [tag.Id] = tag });
        Assert.False(tab.Editor.IsDirty);

        await tab.Palette.LoadAsync();
        var row = Assert.Single(tab.Palette.Tags, r => r.Tag.Id == tag.Id);
        await tab.Palette.DeleteCommand.ExecuteAsync(row);

        Assert.NotNull(await _db.Tags.GetByIdAsync(tag.Id));   // TagInUse(DB ガード)で無傷
        Assert.Equal(1, _windows.ConfirmCount);                // 確認は出る(拒否は Core から)
    }

    private sealed class StubWindows : IWindowService
    {
        public bool ConfirmResult { get; set; } = true;

        public int ConfirmCount { get; private set; }

        public NodeConditionResult? ConditionDialogResult { get; set; }

        public Task<bool> ConfirmAsync(string title, string message)
        {
            ConfirmCount++;
            return Task.FromResult(ConfirmResult);
        }

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
            => Task.FromResult(ConditionDialogResult);

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

    private async Task<(View View, Tag TagA, Tag TagB, HierarchyEditorViewModel Editor, Dictionary<string, Tag> TagById)> SetupAsync()
    {
        var view = (await _views.CreateAsync("V")).Value!;
        var tagA = (await _tagService.CreateAsync("色", TagType.Textual)).Value!;
        var tagB = (await _tagService.CreateAsync("印", TagType.Simple)).Value!;
        var tagById = new Dictionary<string, Tag>(StringComparer.Ordinal)
        {
            [tagA.Id] = tagA,
            [tagB.Id] = tagB,
        };
        var editor = new HierarchyEditorViewModel(_views, _localization, _windows);
        await editor.LoadAsync(view, tagById);
        return (view, tagA, tagB, editor, tagById);
    }

    [Fact]
    public async Task 編集操作はダーティを立て保存とキャンセルが活性化する()
    {
        var (_, tagA, _, editor, _) = await SetupAsync();

        Assert.True(editor.HasView);
        Assert.True(editor.IsTreeEmpty); // 階層ノード 0 件(仕様 §2.6 空状態)
        Assert.False(editor.IsDirty);
        Assert.False(editor.SaveCommand.CanExecute(null));
        Assert.False(editor.CancelCommand.CanExecute(null));

        editor.AddNode(tagA, null);

        Assert.True(editor.IsDirty);
        Assert.True(editor.SaveCommand.CanExecute(null));
        Assert.True(editor.CancelCommand.CanExecute(null));
        Assert.False(editor.IsTreeEmpty);
    }

    [Fact]
    public async Task 編集はメモリ内に留まり保存で一括コミットされmodified_atは保存時に1回だけ更新()
    {
        var (view, tagA, tagB, editor, _) = await SetupAsync();
        var before = (await _views.GetAsync(view.Id))!.ModifiedAt;

        // メモリ内編集(複数操作): ルート追加→別名→ホーム→条件→子追加
        _clock.Advance(TimeSpan.FromMinutes(1));
        var root = editor.AddNode(tagA, null)!;
        editor.BeginAliasEditCommand.Execute(root);
        root.AliasEditText = "いろ";
        editor.CommitAliasCommand.Execute(root);
        editor.ToggleHomeCommand.Execute(root);
        editor.SetCondition(root, HierarchyConditionType.Values, """{"values":["赤","青"]}""");
        var child = editor.AddNode(tagB, root)!;

        // 保存前: DB は不変(階層 0 件・modified_at 不変)= 編集はメモリ内(仕様 §2.6)
        Assert.Empty(await _views.GetHierarchyAsync(view.Id));
        Assert.Equal(before, (await _views.GetAsync(view.Id))!.ModifiedAt);

        // 保存: 一括コミット+modified_at は保存時刻で 1 回(REQ-032)
        _clock.Advance(TimeSpan.FromMinutes(1));
        var saveTime = _clock.UtcNowIso();
        await ((IAsyncRelayCommand)editor.SaveCommand).ExecuteAsync(null);

        Assert.False(editor.IsDirty);
        var nodes = await _views.GetHierarchyAsync(view.Id);
        Assert.Equal(2, nodes.Count);
        var rootNode = nodes.Single(n => n.ParentId is null);
        var childNode = nodes.Single(n => n.ParentId is not null);
        Assert.Equal(root.Id, rootNode.Id);
        Assert.Equal("いろ", rootNode.Alias);
        Assert.Equal(HierarchyConditionType.Values, rootNode.ConditionType);
        Assert.Equal("""{"values":["赤","青"]}""", rootNode.ConditionValue);
        Assert.Equal(0, rootNode.Position);
        Assert.Equal(child.Id, childNode.Id);
        Assert.Equal(root.Id, childNode.ParentId);
        Assert.Equal(0, childNode.Position);

        var saved = (await _views.GetAsync(view.Id))!;
        Assert.Equal(root.Id, saved.HomeTagId); // ホームタグ=階層ノード id(REQ-037)
        Assert.Equal(saveTime, saved.ModifiedAt); // 保存時に 1 回
    }

    [Fact]
    public async Task 破棄は確認なしでメモリ内編集を破棄してDB状態へ戻す()
    {
        // ECO-103(mock v4): 旧・確認ダイアログ方式(modals.confirmDiscard)は撤去 — 破棄=即復元
        var (view, tagA, tagB, editor, tagById) = await SetupAsync();

        // 既存 1 ノードを保存しておく
        editor.AddNode(tagA, null);
        await ((IAsyncRelayCommand)editor.SaveCommand).ExecuteAsync(null);
        Assert.Single(editor.Roots);

        // 編集(追加+別名)して破棄
        var extra = editor.AddNode(tagB, null)!;
        editor.BeginAliasEditCommand.Execute(extra);
        extra.AliasEditText = "x";
        editor.CommitAliasCommand.Execute(extra);
        Assert.True(editor.IsDirty);
        Assert.Equal(2, editor.Roots.Count);

        await ((IAsyncRelayCommand)editor.CancelCommand).ExecuteAsync(null);

        Assert.Equal(0, _windows.ConfirmCount); // 確認ダイアログを出さない(v4 契約)
        Assert.False(editor.IsDirty);
        Assert.Single(editor.Roots); // DB 状態(保存済み 1 ノード)へ復帰
        Assert.Single(await _views.GetHierarchyAsync(view.Id));
        _ = tagById;
    }

    [Fact]
    public async Task ノード削除は配下の枝ごとメモリ内で消え保存で反映される()
    {
        var (view, tagA, tagB, editor, _) = await SetupAsync();
        var root = editor.AddNode(tagA, null)!;
        editor.AddNode(tagB, root);
        await ((IAsyncRelayCommand)editor.SaveCommand).ExecuteAsync(null);
        Assert.Equal(2, (await _views.GetHierarchyAsync(view.Id)).Count);

        editor.DeleteNodeCommand.Execute(root);
        Assert.True(editor.IsDirty);
        Assert.Empty(editor.Roots);

        await ((IAsyncRelayCommand)editor.SaveCommand).ExecuteAsync(null);
        Assert.Empty(await _views.GetHierarchyAsync(view.Id));
        Assert.Null((await _views.GetAsync(view.Id))!.HomeTagId);
    }

    [Fact]
    public async Task ホームタグの設定解除は単一でトグルされる()
    {
        var (_, tagA, tagB, editor, _) = await SetupAsync();
        var a = editor.AddNode(tagA, null)!;
        var b = editor.AddNode(tagB, null)!;

        editor.ToggleHomeCommand.Execute(a);
        Assert.True(a.IsHome);

        editor.ToggleHomeCommand.Execute(b); // 付け替え(単一)
        Assert.False(a.IsHome);
        Assert.True(b.IsHome);

        editor.ToggleHomeCommand.Execute(b); // 解除
        Assert.False(b.IsHome);
    }

    [Fact]
    public async Task 条件設定ダイアログの結果がノードへ反映されダーティになる()
    {
        var (_, tagA, _, editor, _) = await SetupAsync();
        var node = editor.AddNode(tagA, null)!;
        await ((IAsyncRelayCommand)editor.SaveCommand).ExecuteAsync(null);
        Assert.False(editor.IsDirty);

        _windows.ConditionDialogResult = new NodeConditionResult(
            HierarchyConditionType.Equals, """{"value":"赤"}""");
        await ((IAsyncRelayCommand)editor.EditConditionCommand).ExecuteAsync(node);

        Assert.True(editor.IsDirty);
        Assert.Equal(HierarchyConditionType.Equals, node.ConditionType);
        Assert.Equal("""{"value":"赤"}""", node.ConditionValue);
        Assert.True(node.HasCondition);
    }

    [Fact]
    public async Task ダーティ中の遷移はGuardNavigationが拒否する()
    {
        // ECO-103(mock v4): 旧 ConfirmDiscardIfDirtyAsync(確認ダイアログ)は撤去 — ガード=ブロック+attention
        var (_, tagA, _, editor, _) = await SetupAsync();
        Assert.True(editor.GuardNavigation()); // クリーン → 通過
        Assert.Equal(0, _windows.ConfirmCount);

        editor.AddNode(tagA, null);
        Assert.False(editor.GuardNavigation()); // dirty → 拒否(ダイアログなし)
        Assert.Equal(0, _windows.ConfirmCount);
        Assert.True(editor.IsGuardAttention);
    }

    [Fact]
    public async Task ビュー未選択は空状態フラグで表現される()
    {
        var editor = new HierarchyEditorViewModel(_views, _localization, _windows);
        await editor.LoadAsync(null, new Dictionary<string, Tag>(StringComparer.Ordinal));

        Assert.False(editor.HasView); // 「ビューを選択してください」(仕様 §2.6 空状態)
        Assert.False(editor.IsTreeEmpty);
        Assert.False(editor.SaveCommand.CanExecute(null));
    }
}
