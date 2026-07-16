using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ViewPrism2.App.Services;
using ViewPrism2.App.ViewModels;
using ViewPrism2.App.Views;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-UI-G6(ECO-100): 既存配置行の並べ替え・付け替え D&D(TAG-014 実装確定)。
/// 契約= tag_tab.md「配置モデル統一」(クリックもドラッグも同一の挿入表示)+ECO-100 §4.2 裁定(a)〜(e)。
/// プローブ先行(R5): 是正前は移動 API・挿入表示の移動駆動が存在せず不合格 → 是正で緑転。
/// </summary>
[Trait("cp", "CP-UI-G6")]
public sealed class CpUiG6DndMoveTests : IDisposable
{
    private readonly TempDb _db = new();
    private readonly ViewService _views;
    private readonly TagService _tagService;

    public CpUiG6DndMoveTests()
    {
        _views = new ViewService(_db.Views, _db.Clock);
        _tagService = new TagService(_db.Tags);
    }

    public void Dispose() => _db.Dispose();

    /// <summary>ツリー: A{A1, A2} / B / C(A1 に別名+条件+HOME を載せ状態随伴を検査)。</summary>
    private async Task<(HierarchyEditorViewModel Editor,
        EditNodeViewModel A, EditNodeViewModel A1, EditNodeViewModel A2,
        EditNodeViewModel B, EditNodeViewModel C)> SetupAsync()
    {
        var view = (await _views.CreateAsync("V")).Value!;
        var tagA = (await _tagService.CreateAsync("親A", TagType.Textual)).Value!;
        var tagB = (await _tagService.CreateAsync("葉B", TagType.Simple)).Value!;
        var editor = new HierarchyEditorViewModel(_views, TestLoc.Ja(), new StubWindows());
        await editor.LoadAsync(view, new Dictionary<string, Tag>(StringComparer.Ordinal)
        {
            [tagA.Id] = tagA,
            [tagB.Id] = tagB,
        });
        var a = editor.AddNode(tagA, null)!;
        var a1 = editor.AddNode(tagB, a)!;
        var a2 = editor.AddNode(tagB, a)!;
        var b = editor.AddNode(tagB, null)!;
        var c = editor.AddNode(tagB, null)!;

        a1.AliasEditText = "いち";
        editor.BeginAliasEditCommand.Execute(a1);
        a1.AliasEditText = "いち";
        editor.CommitAliasCommand.Execute(a1);
        editor.SetCondition(a1, HierarchyConditionType.Equals, """{"value":"x"}""");
        editor.ToggleHomeCommand.Execute(a1);
        return (editor, a, a1, a2, b, c);
    }

    // ---- 移動の意味論(裁定 (b)(d)) ----

    [Fact]
    public async Task 兄弟の並べ替えは指定位置へ移動し同位置ドロップはnoopでダーティ不変()
    {
        var (editor, a, _, _, b, c) = await SetupAsync();
        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)editor.SaveCommand).ExecuteAsync(null);
        Assert.False(editor.IsDirty);

        // C を A の前へ(insertBefore A 相当)
        Assert.True(editor.MoveNode(c, null, 0));
        Assert.Equal([c, a, b], editor.Roots.ToArray());
        Assert.True(editor.IsDirty);
        Assert.Same(c, editor.SelectedNode); // 移動後は移動行が選択

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)editor.SaveCommand).ExecuteAsync(null);
        Assert.False(editor.IsDirty);

        // 同位置(自分の直前/直後の挿入ポイント)へのドロップ= no-op・ダーティ不変(裁定 b)
        Assert.False(editor.MoveNode(c, null, 0));
        Assert.False(editor.MoveNode(c, null, 1));
        Assert.Equal([c, a, b], editor.Roots.ToArray());
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public async Task 付け替えはサブツリーごと移動し配置状態が随伴し親は自動展開される()
    {
        var (editor, a, a1, a2, b, _) = await SetupAsync();
        b.IsExpanded = false;

        // A(子 A1/A2 持ち)を B の子へ = サブツリーごと(裁定 d)
        Assert.True(editor.MoveNode(a, b, 0));
        Assert.Same(b, a.Parent);
        Assert.Equal([a1, a2], a.Children.ToArray());     // サブツリー無傷
        Assert.True(b.IsExpanded);                          // 自動展開
        Assert.Equal(2, editor.Roots.Count);                // A はルートから消えた

        // per-placement 状態(別名・条件・HOME)は移動後も保持(裁定 d)
        Assert.Equal("いち", a1.Alias);
        Assert.True(a1.HasCondition);
        Assert.True(a1.IsHome);
    }

    [Fact]
    public async Task 自分自身と自分の子孫への入れ子は拒否されツリー不変()
    {
        var (editor, a, a1, _, _, _) = await SetupAsync();
        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)editor.SaveCommand).ExecuteAsync(null);

        Assert.False(editor.MoveNode(a, a, 0));    // 自分自身の子
        Assert.False(editor.MoveNode(a, a1, 0));   // 自分の子孫の子(循環)
        Assert.Equal(3, editor.Roots.Count);
        Assert.Same(a, a1.Parent);
        Assert.False(editor.IsDirty);              // 拒否はツリー不変・ダーティ不変
    }

    // ---- 移動ドラッグの状態機械(裁定 (a)(b)(c)(e)) ----

    [Fact]
    public async Task BeginMoveで挿入表示が出て禁止位置だけ非表示になりEndMoveで全解除される()
    {
        var (editor, a, a1, a2, b, _) = await SetupAsync();

        Assert.False(editor.ShowInsertTargets);
        editor.BeginMove(a);
        Assert.True(editor.ShowInsertTargets);
        Assert.Same(a, editor.DraggingNode);
        Assert.True(a.IsMoveSource);

        // 禁止(b): 自分自身・子孫は子として受けない/子孫の前への挿入(=サブツリー内部)も出ない
        Assert.False(a.ChildTargetVisible);
        Assert.False(a1.ChildTargetVisible);
        Assert.False(a1.InsertBeforeTargetVisible);
        Assert.False(a2.InsertBeforeTargetVisible);
        // 自分の直前の挿入ポイントは可視(ドロップ= no-op)・他行は通常どおり可視
        Assert.True(a.InsertBeforeTargetVisible);
        Assert.True(b.InsertBeforeTargetVisible);
        Assert.True(b.ChildTargetVisible);
        // 帯は「移動中」文言(裁定 e)
        Assert.Contains("を移動中", editor.InsertBannerText, StringComparison.Ordinal);

        // キャンセル(c): EndMove でツリー不変・表示状態が全解除
        editor.EndMove();
        Assert.Null(editor.DraggingNode);
        Assert.False(editor.ShowInsertTargets);
        Assert.False(a.IsMoveSource);
        Assert.False(b.InsertBeforeTargetVisible);
        Assert.Equal(3, editor.Roots.Count);
    }

    [Fact]
    public async Task BeginMoveは配置モードを解除し排他になる()
    {
        var (editor, a, _, _, _, _) = await SetupAsync();
        var tag = (await _tagService.CreateAsync("新タグ", TagType.Simple)).Value!;
        editor.TogglePlacing(tag);
        Assert.True(editor.IsPlacing);

        editor.BeginMove(a);
        Assert.False(editor.IsPlacing);        // 配置と移動は同時に成立しない
        Assert.Same(a, editor.DraggingNode);
        editor.EndMove();
    }

    [Fact]
    public async Task ドロップ経路のMoveBeforeとMoveToChildEndとMoveToRootEndは移動しドラッグを終える()
    {
        var (editor, a, _, _, b, c) = await SetupAsync();

        editor.BeginMove(c);
        editor.MoveBefore(a);                   // 行間ポイント= 兄弟挿入
        Assert.Equal([c, a, b], editor.Roots.ToArray());
        Assert.Null(editor.DraggingNode);       // ドロップでドラッグ終了

        editor.BeginMove(c);
        editor.MoveToChildEnd(b);               // 子リスト末尾/＋子にする相当
        Assert.Same(b, c.Parent);
        Assert.Null(editor.DraggingNode);

        editor.BeginMove(c);
        editor.MoveToRootEnd();                 // ルート末尾
        Assert.Null(c.Parent);
        Assert.Same(c, editor.Roots[^1]);
        Assert.Null(editor.DraggingNode);
    }

    // ---- 実描画(headless): 移動ドラッグ中の表示一式(VC-TAG-12⑤同型+一時停止整合) ----

    [Fact]
    public async Task 移動ドラッグ中は挿入表示が出て行操作が止まり終了後に一切残らない()
    {
        var view = (await _views.CreateAsync("V")).Value!;
        var tagA = (await _tagService.CreateAsync("親A", TagType.Textual)).Value!;
        var tagB = (await _tagService.CreateAsync("葉B", TagType.Simple)).Value!;
        var tab = new TagsTabViewModel(_views, _tagService, _db.Tags, TestLoc.Ja(), new StubWindows());
        await tab.EnsureLoadedAsync();
        await tab.Editor.LoadAsync(view, new Dictionary<string, Tag>(StringComparer.Ordinal)
        {
            [tagA.Id] = tagA,
            [tagB.Id] = tagB,
        });
        var parent = tab.Editor.AddNode(tagA, null)!;
        var child = tab.Editor.AddNode(tagB, parent)!;
        tab.Editor.AddNode(tagB, null);

        await HeadlessApp.Session.Dispatch(() =>
        {
            var window = new Window { Content = new TagsTabView { DataContext = tab }, Width = 1366, Height = 900 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(0, CountVisible(window, "insertPoint"));

            // 子行(child)の移動ドラッグ開始: 挿入表示=クリック配置と同一様式(同一クラスの同一要素)
            tab.Editor.BeginMove(child);
            Dispatcher.UIThread.RunJobs();
            // 行間(root 2)+ルート末尾(1)+子リスト内(child 自身の前=no-op 可 1+末尾 1)= 5
            Assert.Equal(5, CountVisible(window, "insertPoint"));
            // ＋子にする= parent と葉B の 2(child 自身は「自分への入れ子」=禁止で非表示・裁定 b)
            Assert.Equal(2, CountVisible(window, "makeChild"));
            Assert.Equal(0, CountVisible(window, "rowMenuBtn"));   // 一時停止と同型
            Assert.Equal(0, CountVisible(window, "homeGhost"));
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),
                t => t.IsVisible && t.Text is { } s && s.Contains("を移動中", StringComparison.Ordinal));

            // 親(parent)の移動: サブツリー内部の挿入ポイント/子ターゲットが消える(裁定 b)
            tab.Editor.EndMove();
            tab.Editor.BeginMove(parent);
            Dispatcher.UIThread.RunJobs();
            // 可視= root 行間 2+ルート末尾 1(parent 配下= child 前/子末尾は非表示)
            Assert.Equal(3, CountVisible(window, "insertPoint"));
            Assert.Equal(1, CountVisible(window, "makeChild"));    // 葉Bルート行のみ

            // 終了(キャンセル相当): 一切残らない(VC-TAG-12⑤同型)
            tab.Editor.EndMove();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, CountVisible(window, "insertPoint"));
            Assert.Equal(0, CountVisible(window, "makeChild"));
            Assert.Equal(3, CountVisible(window, "rowMenuBtn"));

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    private static int CountVisible(Window window, string cls)
        => window.GetVisualDescendants().OfType<Control>()
            .Count(c => c.Classes.Contains(cls) && c.IsVisible && IsEffectivelyVisible(c));

    private static bool IsEffectivelyVisible(Control c)
    {
        for (Avalonia.Visual? v = c; v is not null; v = v.GetVisualParent())
        {
            if (v is Control { IsVisible: false })
            {
                return false;
            }
        }

        return true;
    }

    private sealed class StubWindows : IWindowService
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
}
