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
    public async Task キャンセルは確認後にメモリ内編集を破棄してDB状態へ戻す()
    {
        var (view, tagA, tagB, editor, tagById) = await SetupAsync();

        // 既存 1 ノードを保存しておく
        editor.AddNode(tagA, null);
        await ((IAsyncRelayCommand)editor.SaveCommand).ExecuteAsync(null);
        Assert.Single(editor.Roots);

        // 編集(追加+別名)してキャンセル(確認=破棄する)
        var extra = editor.AddNode(tagB, null)!;
        editor.BeginAliasEditCommand.Execute(extra);
        extra.AliasEditText = "x";
        editor.CommitAliasCommand.Execute(extra);
        Assert.True(editor.IsDirty);
        Assert.Equal(2, editor.Roots.Count);

        _windows.ConfirmResult = true;
        await ((IAsyncRelayCommand)editor.CancelCommand).ExecuteAsync(null);

        Assert.False(editor.IsDirty);
        Assert.Single(editor.Roots); // DB 状態(保存済み 1 ノード)へ復帰
        Assert.Single(await _views.GetHierarchyAsync(view.Id));
        _ = tagById;
    }

    [Fact]
    public async Task キャンセル確認でいいえなら編集は保持される()
    {
        var (_, tagA, _, editor, _) = await SetupAsync();
        editor.AddNode(tagA, null);

        _windows.ConfirmResult = false;
        await ((IAsyncRelayCommand)editor.CancelCommand).ExecuteAsync(null);

        Assert.True(editor.IsDirty); // 破棄しない
        Assert.Single(editor.Roots);
        Assert.True(_windows.ConfirmCount > 0);
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
    public async Task ダーティ中の切替確認はConfirmDiscardIfDirtyAsyncで行う()
    {
        var (_, tagA, _, editor, _) = await SetupAsync();
        Assert.True(await editor.ConfirmDiscardIfDirtyAsync()); // クリーン → 確認なしで続行
        Assert.Equal(0, _windows.ConfirmCount);

        editor.AddNode(tagA, null);
        _windows.ConfirmResult = false;
        Assert.False(await editor.ConfirmDiscardIfDirtyAsync());
        _windows.ConfirmResult = true;
        Assert.True(await editor.ConfirmDiscardIfDirtyAsync());
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
