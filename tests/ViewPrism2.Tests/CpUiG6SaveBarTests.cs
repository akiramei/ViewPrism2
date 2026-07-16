using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using ViewPrism2.App.Services;
using ViewPrism2.App.ViewModels;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.App.Views;
using ViewPrism2.Core.Services;
using ViewPrism2.Core.Services.Repair;
using ViewPrism2.Core.Services.Similarity;
using ViewPrism2.Infrastructure.I18n;
using ViewPrism2.Infrastructure.Imaging;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-UI-G6(ECO-103): 保存モデル(mock v4)— フローティング保存バー・未保存 3 表示・遷移ガード・
/// 保存トースト+TAG-016 裁定(i)(iv)。契約= tag_tab.md「保存モデルと保存バー」+VC-TAG-16。
/// プローブ先行(R5): 是正前は保存バー面が存在せず不合格 → 是正で緑転。
/// </summary>
[Trait("cp", "CP-UI-G6")]
public sealed class CpUiG6SaveBarTests : IDisposable
{
    private readonly TempDb _db = new();
    private readonly ViewService _views;
    private readonly TagService _tagService;
    private readonly StubWindows _windows = new();

    public CpUiG6SaveBarTests()
    {
        // REQ-083 の参照切れ検証は ITagRepository 注入時のみ有効(production DI 必須・(iv)プローブが依存)
        _views = new ViewService(_db.Views, _db.Clock, _db.Tags);
        _tagService = new TagService(_db.Tags);
    }

    public void Dispose() => _db.Dispose();

    private async Task<(TagsTabViewModel Tab, Tag Tag, View View)> SetupAsync()
    {
        var view = (await _views.CreateAsync("V")).Value!;
        var tag = (await _tagService.CreateAsync("タグA", TagType.Simple)).Value!;
        var tab = new TagsTabViewModel(_views, _tagService, _db.Tags, TestLoc.Ja(), _windows);
        await tab.EnsureLoadedAsync();
        var row = tab.Views.Single();
        await ((IAsyncRelayCommand)tab.SelectViewCommand).ExecuteAsync(row);
        return (tab, tag, view);
    }

    // ---- 遷移ガード(VC-TAG-16③⑥) ----

    [Fact]
    public async Task GuardNavigationはdirty中に遷移を拒否しattentionが復帰する()
    {
        var (tab, tag, _) = await SetupAsync();
        tab.Editor.GuardAttentionRevertDelay = TimeSpan.FromMilliseconds(60);

        Assert.True(tab.Editor.GuardNavigation()); // クリーン=通過
        Assert.False(tab.Editor.IsGuardAttention);

        tab.Editor.AddNode(tag, null);
        Assert.False(tab.Editor.GuardNavigation()); // dirty=拒否
        Assert.True(tab.Editor.IsGuardAttention);
        Assert.Equal("移動する前に、保存するか破棄してください", tab.Editor.SaveBarMessage);

        await Task.Delay(250, TestContext.Current.CancellationToken); // 700ms 相当(短縮)後に通常文言へ復帰
        Assert.False(tab.Editor.IsGuardAttention);
        Assert.Equal("未保存の変更があります", tab.Editor.SaveBarMessage);
    }

    [Fact]
    public async Task dirty中の別ビュー選択はブロックされ確認ダイアログを出さない()
    {
        var (tab, tag, view) = await SetupAsync();
        var second = (await _views.CreateAsync("W")).Value!;
        await tab.ReloadViewsAsync();
        var rowW = tab.Views.Single(r => r.View.Id == second.Id);

        tab.Editor.AddNode(tag, null);
        await ((IAsyncRelayCommand)tab.SelectViewCommand).ExecuteAsync(rowW);

        Assert.Equal(view.Id, tab.SelectedViewRow?.View.Id); // 遷移していない(VC-TAG-16⑥)
        Assert.Equal(view.Id, tab.Editor.View?.Id);
        Assert.Equal(0, _windows.ConfirmCount);              // 旧・確認ダイアログ方式は撤去
        Assert.True(tab.Editor.IsGuardAttention);
        Assert.True(tab.Editor.IsDirty);                     // 編集は無傷
    }

    [Fact]
    public async Task dirty中のビュー操作は新規編集削除ともブロックされる()
    {
        // TAG-016 裁定(i): 特に削除は未保存編集の消失経路(是正前=確認後に削除が通ってしまう)
        var (tab, tag, view) = await SetupAsync();
        tab.Editor.AddNode(tag, null);

        await ((IAsyncRelayCommand)tab.NewViewCommand).ExecuteAsync(null);
        await ((IAsyncRelayCommand)tab.EditViewCommand).ExecuteAsync(tab.Views.Single());
        await ((IAsyncRelayCommand)tab.DeleteViewCommand).ExecuteAsync(tab.Views.Single());

        Assert.Equal(0, _windows.ViewEditDialogCount);           // ダイアログ自体を開かない
        Assert.Equal(0, _windows.ConfirmCount);                  // 削除確認にも到達しない
        Assert.NotNull(await _views.GetAsync(view.Id));          // ビューは無傷
        Assert.True(tab.Editor.IsGuardAttention);
        Assert.True(tab.Editor.IsDirty);
    }

    [Fact]
    public async Task dirty中のタブ遷移はブロックされクリーン時は通過する()
    {
        // VC-TAG-16⑥(他タブ)。MainWindowViewModel は CP-L1-SMOKE と同じ実サービス合成で構築
        var localization = new LocalizationService(
            I18nResourceLoader.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "i18n")), "ja");
        var viewService = new ViewService(_db.Views, _db.Clock);
        var tagsTab = new TagsTabViewModel(viewService, _tagService, _db.Tags, localization, _windows);
        var shell = new MainWindowViewModel(
            _db.Folders, _db.Images, _db.Tags, viewService,
            new NodeGraphBuilder(), new PathConditionConverter(), new ConditionEvaluator(),
            new SimilaritySearchService(_db.Folders, _db.Images, _db.Features, _db.Similarities, new FakePHashImageReader(), _db.Clock),
            new MergeService(_db.Images, _db.Tags, _db.Merges),
            new TrashService(_db.Images, _db.Folders, new FilePresenceProbe()),
            new ImageSorter(), localization, new AppSettings(), _windows,
            tagsTab, new WorkspaceService(_db.Workspaces, _db.Clock));

        var view = (await viewService.CreateAsync("V")).Value!;
        var tag = (await _tagService.CreateAsync("タグZ", TagType.Simple)).Value!;
        shell.ShowTagsTabCommand.Execute(null);
        Assert.Equal(0, shell.SelectedTabIndex);
        await tagsTab.EnsureLoadedAsync();
        await tagsTab.Editor.LoadAsync(view, new Dictionary<string, Tag>(StringComparer.Ordinal) { [tag.Id] = tag });

        // クリーン: 遷移可
        shell.ShowImagesTabCommand.Execute(null);
        Assert.Equal(1, shell.SelectedTabIndex);
        shell.ShowTagsTabCommand.Execute(null);

        // dirty: 画像/作業とも遷移ブロック+attention(編集は無傷)
        tagsTab.Editor.AddNode(tag, null);
        shell.ShowImagesTabCommand.Execute(null);
        Assert.Equal(0, shell.SelectedTabIndex);
        Assert.True(tagsTab.Editor.IsGuardAttention);
        shell.ShowWorkTabCommand.Execute(null);
        Assert.Equal(0, shell.SelectedTabIndex);
        Assert.True(tagsTab.Editor.IsDirty);
    }

    // ---- 破棄・保存トースト(VC-TAG-16②④⑤) ----

    [Fact]
    public async Task 破棄は確認なしで復元し配置モードもクリアする()
    {
        var (tab, tag, _) = await SetupAsync();
        tab.Editor.AddNode(tag, null);
        await ((IAsyncRelayCommand)tab.Editor.SaveCommand).ExecuteAsync(null);
        Assert.Single(tab.Editor.Roots);

        tab.Editor.AddNode(tag, null);              // 未保存の 2 個目
        tab.TogglePlacing(tab.Palette.Tags.Single());
        Assert.True(tab.Editor.IsPlacing);

        await ((IAsyncRelayCommand)tab.Editor.CancelCommand).ExecuteAsync(null);

        Assert.Equal(0, _windows.ConfirmCount);     // v4: 確認ダイアログなし(旧契約の撤去)
        Assert.Single(tab.Editor.Roots);            // 保存時点へ復元
        Assert.False(tab.Editor.IsDirty);
        Assert.False(tab.Editor.IsPlacing);         // 配置モードもクリア(mock discard)
    }

    [Fact]
    public async Task 保存成功でトーストが出て自動消滅する()
    {
        var (tab, tag, _) = await SetupAsync();
        tab.Editor.SavedToastDuration = TimeSpan.FromMilliseconds(80);
        tab.Editor.AddNode(tag, null);

        await ((IAsyncRelayCommand)tab.Editor.SaveCommand).ExecuteAsync(null);
        Assert.False(tab.Editor.IsDirty);
        Assert.True(tab.Editor.IsSavedToastVisible);   // 「✓ 変更を保存しました」

        await Task.Delay(300, TestContext.Current.CancellationToken);                          // 1.8s 相当(短縮)で自動消滅
        Assert.False(tab.Editor.IsSavedToastVisible);
    }

    [Fact]
    public async Task 保存失敗はバーをattention様式で維持し理由を提示する()
    {
        // TAG-016 裁定(iv): 失敗理由はバーのメッセージ位置・700ms 自動復帰なし(次の操作まで維持)
        var (tab, tag, _) = await SetupAsync();
        tab.Editor.GuardAttentionRevertDelay = TimeSpan.FromMilliseconds(60);
        tab.Editor.AddNode(tag, null);
        await _db.Tags.DeleteAsync(tag.Id); // 参照切れを作る(REQ-083 で保存拒否)

        await ((IAsyncRelayCommand)tab.Editor.SaveCommand).ExecuteAsync(null);

        Assert.True(tab.Editor.IsDirty);                 // 保存されていない
        Assert.False(tab.Editor.IsSavedToastVisible);
        Assert.NotNull(tab.Editor.SaveError);            // 失敗理由の提示
        Assert.True(tab.Editor.IsSaveBarAttention);
        Assert.Equal(tab.Editor.SaveError, tab.Editor.SaveBarMessage);

        await Task.Delay(250, TestContext.Current.CancellationToken);                            // 自動復帰しない(遷移ガードの 700ms とは別)
        Assert.True(tab.Editor.IsSaveBarAttention);

        await ((IAsyncRelayCommand)tab.Editor.CancelCommand).ExecuteAsync(null); // 破棄で解消
        Assert.Null(tab.Editor.SaveError);
        Assert.False(tab.Editor.IsSaveBarAttention);
    }

    // ---- 実描画(headless): 3 表示の出現とクリーン時の完全消滅(VC-TAG-16①②⑤) ----

    [Fact]
    public async Task 保存バーとチップはdirty時のみ現れクリーン時は一切残らない()
    {
        var (tab, tag, _) = await SetupAsync();
        tab.Editor.SavedToastDuration = TimeSpan.FromMilliseconds(60);

        await HeadlessApp.Session.Dispatch(async () =>
        {
            var window = new Window { Content = new TagsTabView { DataContext = tab }, Width = 1366, Height = 900 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // クリーン: 3 表示なし+旧ヘッダボタン(保存/キャンセル)が存在しない(VC-TAG-16⑤+撤去)
            Assert.Equal(0, CountVisible(window, "saveBar"));
            Assert.Equal(0, CountVisible(window, "unsavedChip"));
            Assert.Equal(0, CountVisible(window, "savedToast"));

            // dirty: バー+チップ出現(バー内=破棄/保存ボタン)
            tab.Editor.AddNode(tag, null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, CountVisible(window, "saveBar"));
            Assert.Equal(1, CountVisible(window, "unsavedChip"));
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),
                t => t.IsVisible && t.Text == "未保存の変更があります");

            // 保存: バー/チップ消滅+トースト出現→自動消滅
            await ((IAsyncRelayCommand)tab.Editor.SaveCommand).ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, CountVisible(window, "saveBar"));
            Assert.Equal(0, CountVisible(window, "unsavedChip"));
            Assert.Equal(1, CountVisible(window, "savedToast"));
            await Task.Delay(250, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, CountVisible(window, "savedToast"));

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
        public int ConfirmCount { get; private set; }

        public int ViewEditDialogCount { get; private set; }

        public Task<bool> ConfirmAsync(string title, string message)
        {
            ConfirmCount++;
            return Task.FromResult(true);
        }

        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);

        public Task ShowFolderManagementAsync() => Task.CompletedTask;

        public Task ShowSettingsAsync() => Task.CompletedTask;

        public Task ShowSnapshotsAsync() => Task.CompletedTask;

        public Task ShowCollectionExportAsync(string collectionId) => Task.CompletedTask;

        public Task ShowCollectionImportAsync(string collectionId) => Task.CompletedTask;

        public Task<bool> ShowTagEditorAsync(Tag? existing) => Task.FromResult(false);

        public Task<bool> ShowViewEditDialogAsync(View? existing)
        {
            ViewEditDialogCount++;
            return Task.FromResult(false);
        }

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
