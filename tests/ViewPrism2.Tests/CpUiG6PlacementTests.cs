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
/// CP-UI-G6(ECO-099): 配置モデル統一(クリック配置)+行操作「⋯」メニュー+ホーム1クリック。
/// CAD 契約= tag_tab.md「配置モデル統一」「配置タグの行操作」+ VC-TAG-12/13/14(2026-07-16 mock v3)。
/// プローブ先行(R5): 是正前は配置モード面が存在せず全プローブ不合格 → 是正で緑転。
/// </summary>
[Trait("cp", "CP-UI-G6")]
public sealed class CpUiG6PlacementTests : IDisposable
{
    private readonly TempDb _db = new();
    private readonly ViewService _views;
    private readonly TagService _tagService;
    private readonly StubWindows _windows = new();

    public CpUiG6PlacementTests()
    {
        _views = new ViewService(_db.Views, _db.Clock);
        _tagService = new TagService(_db.Tags);
    }

    public void Dispose() => _db.Dispose();

    private async Task<(TagsTabViewModel Tab, Tag TagA, Tag TagB, View View)> SetupAsync()
    {
        var view = (await _views.CreateAsync("V")).Value!;
        var tagA = (await _tagService.CreateAsync("地域", TagType.Textual)).Value!;
        var tagB = (await _tagService.CreateAsync("風景", TagType.Simple)).Value!;
        var tab = new TagsTabViewModel(_views, _tagService, _db.Tags, TestLoc.Ja(), _windows);
        await tab.EnsureLoadedAsync();
        await tab.Editor.LoadAsync(view, new Dictionary<string, Tag>(StringComparer.Ordinal)
        {
            [tagA.Id] = tagA,
            [tagB.Id] = tagB,
        });
        return (tab, tagA, tagB, view);
    }

    private static TagPaletteRowViewModel Row(TagsTabViewModel tab, Tag tag)
        => tab.Palette.Tags.Single(r => r.Tag.Id == tag.Id);

    // ---- 配置モードの状態遷移(VC-TAG-12①②・インタラクション表) ----

    [Fact]
    public async Task パレットクリックで配置モードへ入り同カード再クリックで解除される()
    {
        var (tab, tagA, tagB, _) = await SetupAsync();

        tab.TogglePlacing(Row(tab, tagA));
        Assert.Equal(tagA.Id, tab.Editor.PlacingTag?.Id);
        Assert.True(tab.Editor.IsPlacing);
        Assert.True(Row(tab, tagA).IsPlacing);          // 配置中カードの強調(VC-TAG-12①)
        Assert.Contains("地域", tab.Editor.PlacingBannerText, StringComparison.Ordinal);
        Assert.Contains("を配置中", tab.Editor.PlacingBannerText, StringComparison.Ordinal);

        // 別カードのクリック=配置対象の切替(mock: placing !== id → 置換)
        tab.TogglePlacing(Row(tab, tagB));
        Assert.Equal(tagB.Id, tab.Editor.PlacingTag?.Id);
        Assert.False(Row(tab, tagA).IsPlacing);
        Assert.True(Row(tab, tagB).IsPlacing);

        // 同カード再クリック=トグル解除
        tab.TogglePlacing(Row(tab, tagB));
        Assert.Null(tab.Editor.PlacingTag);
        Assert.False(Row(tab, tagB).IsPlacing);
    }

    [Fact]
    public async Task 解除は配置モードだけを解除しツリーは不変でダーティにならない()
    {
        var (tab, tagA, _, _) = await SetupAsync();
        tab.Editor.AddNode(tagA, null);
        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)tab.Editor.SaveCommand).ExecuteAsync(null);
        Assert.False(tab.Editor.IsDirty);

        tab.TogglePlacing(Row(tab, tagA));
        Assert.True(tab.Editor.IsPlacing);

        tab.Editor.CancelPlacingCommand.Execute(null);  // Esc/ヘッダ帯の解除と同経路
        Assert.False(tab.Editor.IsPlacing);
        Assert.Single(tab.Editor.Roots);                // ツリー不変
        Assert.False(tab.Editor.IsDirty);               // 配置モード自体は編集ではない
    }

    [Fact]
    public async Task ビュー再読込で配置モードは解除される()
    {
        var (tab, tagA, _, view) = await SetupAsync();
        tab.TogglePlacing(Row(tab, tagA));
        Assert.True(tab.Editor.IsPlacing);

        await tab.Editor.LoadAsync(view, new Dictionary<string, Tag>(StringComparer.Ordinal) { [tagA.Id] = tagA });
        Assert.False(tab.Editor.IsPlacing); // ビュー切替・保存後再読込で配置は持ち越さない
    }

    [Fact]
    public async Task ビュー未選択では配置モードに入らない()
    {
        var (tab, tagA, _, _) = await SetupAsync();
        await tab.Editor.LoadAsync(null, new Dictionary<string, Tag>(StringComparer.Ordinal));

        tab.TogglePlacing(Row(tab, tagA));
        Assert.False(tab.Editor.IsPlacing); // 挿入先が存在しない(実装判断=ECO-099 §4)
    }

    // ---- 挿入セマンティクス(VC-TAG-12③④・インタラクション表) ----

    [Fact]
    public async Task 行間の挿入ポイントはその位置へ兄弟として挿入し配置解除と選択が起きる()
    {
        var (tab, tagA, tagB, _) = await SetupAsync();
        var first = tab.Editor.AddNode(tagA, null)!;

        tab.TogglePlacing(Row(tab, tagB));
        tab.Editor.InsertBeforeCommand.Execute(first);

        Assert.Equal(2, tab.Editor.Roots.Count);
        Assert.Equal(tagB.Id, tab.Editor.Roots[0].TagId);   // first の前=兄弟挿入
        Assert.Equal(tagA.Id, tab.Editor.Roots[1].TagId);
        Assert.False(tab.Editor.IsPlacing);                  // 配置実行で解除
        Assert.Same(tab.Editor.Roots[0], tab.Editor.SelectedNode); // 配置タグが選択状態
        Assert.True(tab.Editor.IsDirty);
    }

    [Fact]
    public async Task 子にするは行の子として末尾挿入し親を自動展開する()
    {
        var (tab, tagA, tagB, _) = await SetupAsync();
        var parent = tab.Editor.AddNode(tagA, null)!;
        var existingChild = tab.Editor.AddNode(tagB, parent)!;
        parent.IsExpanded = false;

        tab.TogglePlacing(Row(tab, tagB));
        tab.Editor.PlaceAsChildCommand.Execute(parent);

        Assert.Equal(2, parent.Children.Count);
        Assert.Same(existingChild, parent.Children[0]);
        Assert.Equal(tagB.Id, parent.Children[1].TagId);    // 末尾挿入
        Assert.True(parent.IsExpanded);                     // 自動展開
        Assert.False(tab.Editor.IsPlacing);
        Assert.Same(parent.Children[1], tab.Editor.SelectedNode);
    }

    [Fact]
    public async Task 子リスト末尾とルート末尾の挿入ポイントは末尾挿入になる()
    {
        var (tab, tagA, tagB, _) = await SetupAsync();
        var parent = tab.Editor.AddNode(tagA, null)!;
        tab.Editor.AddNode(tagB, parent);

        tab.TogglePlacing(Row(tab, tagB));
        tab.Editor.InsertChildEndCommand.Execute(parent);
        Assert.Equal(2, parent.Children.Count);
        Assert.Equal(tagB.Id, parent.Children[1].TagId);

        tab.TogglePlacing(Row(tab, tagB));
        tab.Editor.InsertRootEndCommand.Execute(null);
        Assert.Equal(2, tab.Editor.Roots.Count);
        Assert.Equal(tagB.Id, tab.Editor.Roots[1].TagId);
        Assert.False(tab.Editor.IsPlacing);
    }

    [Fact]
    public async Task 配置モードでない時の挿入コマンドは何もしない()
    {
        var (tab, tagA, _, _) = await SetupAsync();
        var first = tab.Editor.AddNode(tagA, null)!;

        tab.Editor.InsertBeforeCommand.Execute(first);
        tab.Editor.InsertRootEndCommand.Execute(null);
        tab.Editor.PlaceAsChildCommand.Execute(first);
        Assert.Single(tab.Editor.Roots);
        Assert.Empty(first.Children);
    }

    // ---- ホーム設定(VC-TAG-14④⑤⑥) ----

    [Fact]
    public async Task メニューのホームに設定は排他で移動し現ホーム行でも解除しない()
    {
        var (tab, tagA, tagB, _) = await SetupAsync();
        var a = tab.Editor.AddNode(tagA, null)!;
        var b = tab.Editor.AddNode(tagB, null)!;

        tab.Editor.SetHomeFromMenuCommand.Execute(a);
        Assert.True(a.IsHome);

        tab.Editor.SetHomeFromMenuCommand.Execute(b);   // 排他移動
        Assert.False(a.IsHome);
        Assert.True(b.IsHome);

        tab.Editor.SetHomeFromMenuCommand.Execute(b);   // メニュー経路は設定のみ(mock setHomeClose)
        Assert.True(b.IsHome);
    }

    [Fact]
    public async Task 家アイコンのtitleは現ホーム行で解除文言になる()
    {
        var (tab, tagA, tagB, _) = await SetupAsync();
        var a = tab.Editor.AddNode(tagA, null)!;
        var b = tab.Editor.AddNode(tagB, null)!;

        Assert.Equal("ホームに設定（他の行から移動）", a.HomeButtonTitle);
        tab.Editor.ToggleHomeCommand.Execute(a);        // ゴーストボタン経路=トグル(既存契約)
        Assert.Equal("ホームを解除", a.HomeButtonTitle);
        Assert.Equal("ホームに設定（他の行から移動）", b.HomeButtonTitle);
    }

    // ---- 実描画(headless): 配置中の状態切替一式(VC-TAG-12⑤⑦)+条件チップ(VC-TAG-13⑤・TAG-015③) ----

    [Fact]
    public async Task 配置中は挿入ポイントと子にするが現れ行操作は一時停止し解除後は一切残らない()
    {
        var (tab, tagA, tagB, _) = await SetupAsync();
        var parent = tab.Editor.AddNode(tagA, null)!;
        tab.Editor.AddNode(tagB, parent);
        tab.Editor.AddNode(tagB, null);

        await HeadlessApp.Session.Dispatch(() =>
        {
            var window = new Window { Content = new TagsTabView { DataContext = tab }, Width = 1366, Height = 900 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // 非配置時: 挿入装飾なし+全行に「⋯」(VC-TAG-13①)
            Assert.Equal(0, CountVisible(window, "insertPoint"));
            Assert.Equal(0, CountVisible(window, "makeChild"));
            Assert.Equal(3, CountVisible(window, "rowMenuBtn"));

            // 配置開始: 行間(root 2)+ルート末尾(1)+子リスト内(child 1+末尾 1)=挿入ポイント 5、
            // 「＋ 子にする」=全 3 行、「⋯」と家アイコンは非表示(VC-TAG-12⑦)
            tab.TogglePlacing(Row(tab, tagB));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(5, CountVisible(window, "insertPoint"));
            Assert.Equal(3, CountVisible(window, "makeChild"));
            Assert.Equal(0, CountVisible(window, "rowMenuBtn"));
            Assert.Equal(0, CountVisible(window, "homeGhost"));

            // ヘッダ帯が配置中チップへ切替(VC-TAG-12②)
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),
                t => t.IsVisible && t.Text is { } s && s.Contains("を配置中", StringComparison.Ordinal));

            // 解除: 装飾が一切残らない(VC-TAG-12⑤)
            tab.Editor.CancelPlacingCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, CountVisible(window, "insertPoint"));
            Assert.Equal(0, CountVisible(window, "makeChild"));
            Assert.Equal(3, CountVisible(window, "rowMenuBtn"));

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task 条件チップは琥珀様式で幅上限と全文ツールチップを持つ()
    {
        // TAG-015③裁定(ECO-099 §4.2)+VC-TAG-13⑤: 長文条件はチップ幅上限+省略+ツールチップ。非対話。
        var (tab, tagA, _, _) = await SetupAsync();
        var node = tab.Editor.AddNode(tagA, null)!;
        tab.Editor.SetCondition(node, HierarchyConditionType.Pattern,
            """{"pattern":"とても長い正規表現パターンとても長い正規表現パターンとても長い正規表現パターン"}""");

        await HeadlessApp.Session.Dispatch(() =>
        {
            var window = new Window { Content = new TagsTabView { DataContext = tab }, Width = 1366, Height = 900 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var chip = window.GetVisualDescendants().OfType<Border>()
                .Single(b => b.Classes.Contains("condChip") && b.IsVisible);
            var text = chip.GetVisualDescendants().OfType<TextBlock>().Last();
            Assert.True(text.MaxWidth <= 150, $"条件チップ文言の幅上限がない(MaxWidth={text.MaxWidth})");
            Assert.NotNull(Avalonia.Controls.ToolTip.GetTip(chip)); // 全文はツールチップ
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    private static int CountVisible(Window window, string cls)
        => window.GetVisualDescendants().OfType<Control>()
            .Count(c => c.Classes.Contains(cls) && c.IsVisible && IsEffectivelyVisible(c));

    /// <summary>祖先の IsVisible=false 配下(折畳み等)を数えない。</summary>
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
        public Task<bool> ConfirmAsync(string title, string message, string confirmLabel, bool destructive = false, string? cancelLabel = null) => Task.FromResult(true);

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
