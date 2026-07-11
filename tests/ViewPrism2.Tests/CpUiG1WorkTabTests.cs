using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ViewPrism2.App.Services;
using ViewPrism2.App.ViewModels;
using ViewPrism2.App.Views;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using ViewPrism2.Core.Services.Repair;
using ViewPrism2.Core.Services.Similarity;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-UI-G1(ECO-021/ECO-β-1 / WorkTabViewModel): 作業タブ surface のオーケストレーション。
/// チップ=現スペースのタグから算出・絞り込みで Items が狭まる / 作業モードで選択し別スペースへ移動(MoveImages)。
/// Core 意味論(WorkspaceService.MoveImages 等)は CP-WORKSPACE-028 で検証済。本書は VM glue を固定する。
/// </summary>
[Trait("cp", "CP-UI-G1")]
public sealed class CpUiG1WorkTabTests : IDisposable
{
    private readonly TempDb _db = new();
    private const string Folder = "folder-1";

    public void Dispose() => _db.Dispose();

    private async Task SeedAsync()
    {
        await _db.Folders.AddAsync(new SyncFolder { Id = Folder, Name = "F", Path = @"C:\f" });
        foreach (var id in new[] { "a", "b", "c" })
            await _db.Images.AddAsync(new ImageRecord
            {
                Id = id, SyncFolderId = Folder, RelativePath = $"{id}.jpg", FileName = $"{id}.jpg",
                FileSize = 100, Hash = "h", Status = ImageStatus.Normal,
                CreatedDate = "2026-01-01T00:00:00.000Z", ModifiedDate = "2026-01-01T00:00:00.000Z",
            });
        await _db.Tags.AddAsync(new Tag { Id = "t-fav", Name = "お気に入り", Type = TagType.Simple, Color = "#e5484d" });
        await _db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = "a", TagId = "t-fav" });
        await _db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = "b", TagId = "t-fav" });
    }

    private sealed class TruePresenceProbe : IFilePresenceProbe
    {
        public bool Exists(string absoluteImagePath) => true; // 復元=normal へ
    }

    private sealed class StubWindowService : IWindowService
    {
        public List<(IReadOnlyList<ImageEntry> Ordered, int StartIndex)> ViewerCalls { get; } = new();

        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(true);
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
        public void ShowViewer(IReadOnlyList<ImageEntry> ordered, int startIndex)
            => ViewerCalls.Add((ordered, startIndex));
        public Task ShowSimilarSearchAsync(ImageEntry baseImage, IReadOnlyList<ImageEntry> collectionEntries) => Task.CompletedTask;
        public Task<bool> ShowMergeAsync(ImageEntry target, IReadOnlyList<ImageEntry> sources) => Task.FromResult(false);
        public Task ShowTrashAsync(string collectionId) => Task.CompletedTask;
    }

    private WorkTabViewModel NewVm(AppSettings? settings = null, StubWindowService? windows = null) =>
        new(new WorkspaceService(_db.Workspaces, _db.Clock), _db.Folders, _db.Tags,
            new SimilaritySearchService(_db.Folders, _db.Images, _db.Features, _db.Similarities, new FakePHashImageReader(), _db.Clock),
            new MergeService(_db.Images, _db.Tags, _db.Merges),
            new TrashService(_db.Images, _db.Folders, new TruePresenceProbe()),
            windows ?? new StubWindowService(), new ImageSorter(), settings ?? new AppSettings());

    /// <summary>ECO-068: 作業タブ閲覧モードのダブルクリックは、現在の可視順でビューアーを起動する。</summary>
    [Fact]
    public async Task 閲覧モードのダブルクリックは絞り込みソート後の表示順と開始位置でビューアーを開く()
    {
        await SeedAsync();
        var ws = new WorkspaceService(_db.Workspaces, _db.Clock);
        await ws.AddImagesToDefaultAsync(new[] { "a", "b", "c" });
        var windows = new StubWindowService();
        var vm = NewVm(windows: windows);
        await vm.InitializeAsync();

        vm.HandleItemClick(vm.Items.Single(i => i.Id == "a"), ctrl: false, shift: false, isDoubleClick: false);
        Assert.Empty(windows.ViewerCalls); // 閲覧時single clickは無操作

        vm.ClickChip(vm.Chips.Single(c => c.Id == "t-fav")); // 可視集合=a,b
        vm.ToggleSortDirCommand.Execute(null);                // 名前降順=b,a
        vm.HandleItemClick(vm.Items.Single(i => i.Id == "a"), ctrl: false, shift: false, isDoubleClick: true);

        var call = Assert.Single(windows.ViewerCalls);
        Assert.Equal(new[] { "b", "a" }, call.Ordered.Select(e => e.Record.Id).ToArray());
        Assert.Equal(1, call.StartIndex);
    }

    /// <summary>ECO-068/REQ-041: 文脈モード中のダブルクリックは既存の選択意味論を優先する。</summary>
    [Fact]
    public async Task 文脈モード中のダブルクリックはビューアーを開かない()
    {
        await SeedAsync();
        var ws = new WorkspaceService(_db.Workspaces, _db.Clock);
        await ws.AddImagesToDefaultAsync(new[] { "a", "b", "c" });
        var windows = new StubWindowService();
        var vm = NewVm(windows: windows);
        await vm.InitializeAsync();

        vm.ToggleEditCommand.Execute(null);
        vm.HandleItemClick(vm.Items.Single(i => i.Id == "a"), false, false, isDoubleClick: true);
        vm.ToggleEditCommand.Execute(null);

        vm.ToggleWorkCommand.Execute(null);
        vm.HandleItemClick(vm.Items.Single(i => i.Id == "a"), false, false, isDoubleClick: true);
        vm.ToggleWorkCommand.Execute(null);

        vm.ToggleOrganizeCommand.Execute(null);
        vm.HandleItemClick(vm.Items.Single(i => i.Id == "a"), false, false, isDoubleClick: true);
        vm.ToggleOrganizeCommand.Execute(null);

        vm.EnterDeleteCommand.Execute(null);
        vm.HandleItemClick(vm.Items.Single(i => i.Id == "a"), false, false, isDoubleClick: true);

        Assert.Empty(windows.ViewerCalls);
    }

    [Fact]
    public async Task チップは現スペースのタグから算出_絞り込みでItemsが狭まる()
    {
        await SeedAsync();
        var ws = new WorkspaceService(_db.Workspaces, _db.Clock);
        await ws.AddImagesToDefaultAsync(new[] { "a", "b", "c" });

        var vm = NewVm();
        await vm.InitializeAsync();

        Assert.Equal(3, vm.Items.Count);
        Assert.True(vm.ShowChips);
        var favChip = vm.Chips.Single(c => c.Id == "t-fav");
        Assert.Equal("3", vm.CountLabel.Split(' ')[0]); // 3 項目

        vm.ClickChip(favChip);                 // お気に入りで絞り込み
        Assert.Equal(new[] { "a", "b" }, vm.Items.Select(i => i.Id).ToArray());

        vm.ClickChip(vm.Chips.Single(c => c.Id == "__clear")); // クリア
        Assert.Equal(3, vm.Items.Count);
    }

    [Fact]
    public async Task 作業モードで選択し別スペースへ移動()
    {
        await SeedAsync();
        var ws = new WorkspaceService(_db.Workspaces, _db.Clock);
        await ws.AddImagesToDefaultAsync(new[] { "a", "b", "c" }); // default(d1) に a,b,c

        var vm = NewVm();
        await vm.InitializeAsync();
        await vm.AddWorkspaceCommand.ExecuteAsync(null); // d2=新デフォルト(空・current)、d1=保存済み(a,b,c)
        Assert.Empty(vm.Items);

        // d1(a,b,c)を選択
        var d1 = vm.Workspaces.Single(w => !w.IsDefault);
        var d2Id = vm.Workspaces.Single(w => w.IsDefault).Id;
        await vm.SelectWorkspaceCommand.ExecuteAsync(d1.Id);
        Assert.Equal(3, vm.Items.Count);

        // 作業モード→a を選択→d2 へ移動
        vm.ToggleWorkCommand.Execute(null);
        Assert.True(vm.WorkMode);
        vm.HandleItemClick(vm.Items.Single(i => i.Id == "a"), ctrl: false, shift: false);
        Assert.True(vm.HasMoveSelection);
        Assert.Equal(1, vm.MoveSelCount);

        await vm.MoveSelectedToCommand.ExecuteAsync(d2Id);

        // d1 は b,c に、d2 は a に
        Assert.Equal(new[] { "b", "c" }, vm.Items.Select(i => i.Id).OrderBy(x => x).ToArray());
        await vm.SelectWorkspaceCommand.ExecuteAsync(d2Id);
        Assert.Equal(new[] { "a" }, vm.Items.Select(i => i.Id).ToArray());
    }

    [Fact]
    public async Task タグ編集モードでシンプルタグを付与し現在のタグに反映_削除で消える()
    {
        await SeedAsync(); // a,b に t-fav 付与済み・c はタグなし
        await _db.Tags.AddAsync(new Tag { Id = "t-featured", Name = "おすすめ", Type = TagType.Simple, Color = "#8b5cf6" });
        var ws = new WorkspaceService(_db.Workspaces, _db.Clock);
        await ws.AddImagesToDefaultAsync(new[] { "a", "b", "c" });

        var vm = NewVm();
        await vm.InitializeAsync();

        vm.ToggleEditCommand.Execute(null);
        Assert.True(vm.EditMode);
        Assert.False(vm.WorkMode);                // 作業と排他
        vm.HandleItemClick(vm.Items.Single(i => i.Id == "c"), ctrl: false, shift: false);
        Assert.True(vm.PanelActive);

        // タグ追加タブ → おすすめ(シンプル)を付与
        vm.TabAddCommand.Execute(null);
        var featuredRow = vm.AddGroups.Single(g => g.Label == "シンプル").Tags.Single(r => r.Id == "t-featured");
        Assert.False(featuredRow.Added);
        await vm.ClickAddRowCommand.ExecuteAsync(featuredRow);

        Assert.Contains(vm.CurrentTags, t => t.Id == "t-featured"); // 現在のタグに反映

        // 削除で消える
        await vm.RemoveCurrentTagCommand.ExecuteAsync(vm.CurrentTags.Single(t => t.Id == "t-featured"));
        Assert.DoesNotContain(vm.CurrentTags, t => t.Id == "t-featured");
    }

    [Fact]
    public async Task 整理モードでマージ先を選び条件検索でまとめてマージ()
    {
        await _db.Folders.AddAsync(new SyncFolder { Id = Folder, Name = "F", Path = @"C:\f" });
        // ECO-055: 条件検索= マージ先との属性一致。dup_a/dup_b は同一ハッシュ(=完全重複)・other は別ハッシュ
        foreach (var (id, hash) in new[] { ("dup_a", "h"), ("dup_b", "h"), ("other", "h2") })
            await _db.Images.AddAsync(new ImageRecord
            {
                Id = id, SyncFolderId = Folder, RelativePath = $"{id}.jpg", FileName = $"{id}.jpg",
                FileSize = 100, Hash = hash, Status = ImageStatus.Normal,
                CreatedDate = "2026-01-01T00:00:00.000Z", ModifiedDate = "2026-01-01T00:00:00.000Z",
            });
        var ws = new WorkspaceService(_db.Workspaces, _db.Clock);
        await ws.AddImagesToDefaultAsync(new[] { "dup_a", "dup_b", "other" });

        var vm = NewVm();
        await vm.InitializeAsync();

        vm.ToggleOrganizeCommand.Execute(null);
        Assert.True(vm.OrganizeMode);
        Assert.True(vm.ShowMergeTargetPrompt);

        // dup_a をクリック → マージ先(残す1枚)
        vm.HandleItemClick(vm.Items.Single(i => i.Id == "dup_a"), ctrl: false, shift: false);
        Assert.True(vm.HasMergeTarget);
        Assert.Equal("dup_a.jpg", vm.MergeTarget!.Name);

        // 条件検索(ECO-055: マージ先とハッシュ一致)→ dup_b(マージ先 dup_a は除外・other は別ハッシュで不一致)
        vm.SetSearchMethodCommand.Execute("criteria");
        vm.CondHash = true;
        await vm.RunSearchCommand.ExecuteAsync(null);
        Assert.True(vm.ShowSearchResults);
        Assert.Equal(new[] { "dup_b" }, vm.SearchResults.Select(r => r.Id).ToArray());

        // 整理対象へ追加 → マージ実行
        vm.AddCandidateToTargetsCommand.Execute("dup_b");
        Assert.True(vm.CanExecuteMerge);
        await vm.ExecuteMergeCommand.ExecuteAsync(null);

        Assert.True(vm.OrganizeDone);
        // dup_b は deleted=現スペースから外れ、dup_a/other が残る(E-MERGE-034・物理非破壊 INV-009)
        Assert.Equal(new[] { "dup_a", "other" }, vm.Items.Select(i => i.Id).OrderBy(x => x).ToArray());
        Assert.Equal(ImageStatus.Deleted, (await _db.Images.GetByIdAsync("dup_b"))!.Status);
    }

    [Fact]
    public async Task 削除モードでゴミ箱へ移動_ゴミ箱popupで復元して現スペースへ戻る()
    {
        await SeedAsync(); // a,b,c
        var ws = new WorkspaceService(_db.Workspaces, _db.Clock);
        await ws.AddImagesToDefaultAsync(new[] { "a", "b", "c" });

        var vm = NewVm();
        await vm.InitializeAsync();

        // ⋯「削除」→ 削除モード → a を選択 → ゴミ箱へ移動(normal→deleted ソフト削除)
        vm.EnterDeleteCommand.Execute(null);
        Assert.True(vm.DeleteMode);
        Assert.False(vm.ShowEditEntry); // 他モード入口は隠れる(排他)
        vm.HandleItemClick(vm.Items.Single(i => i.Id == "a"), ctrl: false, shift: false);
        Assert.True(vm.CanDeleteToTrash);
        await vm.DeleteToTrashCommand.ExecuteAsync(null);

        Assert.Equal(ImageStatus.Deleted, (await _db.Images.GetByIdAsync("a"))!.Status);
        Assert.DoesNotContain(vm.Items, i => i.Id == "a"); // normal 一覧から外れる
        Assert.Equal(1, vm.TrashCount);                    // ⋯ゴミ箱バッジ=現スペースの deleted 件数

        // ⋯「ゴミ箱」→ popup で a を選択 → 復元(物理存在→normal)
        await vm.OpenTrashCommand.ExecuteAsync(null);
        Assert.True(vm.TrashOpen);
        Assert.Equal(new[] { "a" }, vm.TrashPopupItems.Select(i => i.Id).ToArray());
        vm.ToggleTrashSelectAllCommand.Execute(null);
        Assert.True(vm.CanRestoreTrash);
        await vm.RestoreSelectedTrashCommand.ExecuteAsync(null);

        Assert.Equal(ImageStatus.Normal, (await _db.Images.GetByIdAsync("a"))!.Status);
        Assert.Empty(vm.TrashPopupItems);                  // ゴミ箱から消える
    }

    [Fact]
    public async Task ゴミ箱popupで完全削除_確認後にDB行除去_所属もCASCADEで消える()
    {
        await SeedAsync();
        var ws = new WorkspaceService(_db.Workspaces, _db.Clock);
        await ws.AddImagesToDefaultAsync(new[] { "a", "b" });

        var vm = NewVm();
        await vm.InitializeAsync();
        vm.EnterDeleteCommand.Execute(null);
        vm.HandleItemClick(vm.Items.Single(i => i.Id == "a"), false, false);
        await vm.DeleteToTrashCommand.ExecuteAsync(null);
        await vm.OpenTrashCommand.ExecuteAsync(null);
        vm.ToggleTrashSelectAllCommand.Execute(null);

        await vm.PurgeSelectedTrashCommand.ExecuteAsync(null); // StubWindowService.ConfirmAsync=true

        Assert.Null(await _db.Images.GetByIdAsync("a")); // DB 行除去(INV-009: 物理ファイルは不変)
        Assert.Empty(vm.TrashPopupItems);
        Assert.Equal(0, vm.TrashCount);
    }

    // ---- レビュー指摘の回帰(2026-06-29) ----

    [Fact]
    public async Task 絞り込みで非表示になった選択は落ちる_見えない画像を操作しない()
    {
        await SeedAsync(); // a,b=t-fav / c=タグなし
        var ws = new WorkspaceService(_db.Workspaces, _db.Clock);
        await ws.AddImagesToDefaultAsync(new[] { "a", "b", "c" });
        var vm = NewVm();
        await vm.InitializeAsync();

        vm.ToggleWorkCommand.Execute(null);
        vm.HandleItemClick(vm.Items.Single(i => i.Id == "a"), ctrl: false, shift: false);
        vm.HandleItemClick(vm.Items.Single(i => i.Id == "c"), ctrl: true, shift: false); // a(t-fav)+c(タグなし)
        Assert.Equal(2, vm.MoveSelCount);

        // t-fav で絞り込み → c が非表示 → 選択から落ちる(別スペース移動/削除の対象にならない)
        vm.ClickChip(vm.Chips.Single(ch => ch.Id == "t-fav"));
        Assert.Equal(new[] { "a", "b" }, vm.Items.Select(i => i.Id).ToArray()); // c 非表示
        Assert.Equal(1, vm.MoveSelCount);                                       // c は選択から落ちた
        Assert.True(vm.HasMoveSelection);
    }

    [Fact]
    public async Task RefreshAsyncはフォルダマップを再読込し新フォルダ画像のサムネが解決する()
    {
        await SeedAsync(); // folder-1
        var ws = new WorkspaceService(_db.Workspaces, _db.Clock);
        await ws.AddImagesToDefaultAsync(new[] { "a" });
        var vm = NewVm();
        await vm.InitializeAsync(); // この時点の folderPath は folder-1 のみ

        // 起動後に新フォルダ folder-2 + 画像 x を追加し作業スペースへ(受け渡し相当)
        await _db.Folders.AddAsync(new SyncFolder { Id = "folder-2", Name = "F2", Path = @"C:\f2" });
        await _db.Images.AddAsync(new ImageRecord
        {
            Id = "x", SyncFolderId = "folder-2", RelativePath = "x.jpg", FileName = "x.jpg",
            FileSize = 100, Hash = "h", Status = ImageStatus.Normal,
            CreatedDate = "2026-01-01T00:00:00.000Z", ModifiedDate = "2026-01-01T00:00:00.000Z",
        });
        await ws.AddImagesToDefaultAsync(new[] { "x" });

        await vm.RefreshAsync(); // folderPath を再読込(folder-2 を含む)
        Assert.True(vm.Items.Single(i => i.Id == "x").HasRealThumb); // AbsolutePath が解決(修正前は null)
    }

    // ---- ECO-039(FL-004=D-b) 受入: 表示形式のタブ独立永続 ----

    [Fact]
    public async Task 表示形式は作業タブ専用キーへ保存され共通キーを汚さず再構築で復元される()
    {
        await SeedAsync();
        var ws = new WorkspaceService(_db.Workspaces, _db.Clock);
        await ws.AddImagesToDefaultAsync(new[] { "a" });
        var settings = new AppSettings(); // DisplayMode 既定 grid・WorkTabDisplayMode 未保存

        var vm = NewVm(settings);
        await vm.InitializeAsync();
        Assert.True(vm.IsGrid); // 初回は共通キー(grid)を初期値に読む

        vm.SetListCommand.Execute(null);
        Assert.Equal("list", settings.WorkTabDisplayMode); // 専用キーへ保存
        Assert.Equal("grid", settings.DisplayMode);        // 共通キー(画像タブ)は汚さない=独立

        var vm2 = NewVm(settings); // 再起動相当(同一 settings で再構築)
        await vm2.InitializeAsync();
        Assert.True(vm2.IsList);   // 専用キーから復元
    }

    [Fact]
    public async Task 表示形式の初回は画像タブ共通設定を初期値に読み専用キーが優先される()
    {
        await SeedAsync();
        var ws = new WorkspaceService(_db.Workspaces, _db.Clock);
        await ws.AddImagesToDefaultAsync(new[] { "a" });

        // 専用キー未保存 → 共通キー(list)へフォールバック(FL-004=D-b: 初回挙動不変)
        var vm = NewVm(new AppSettings { DisplayMode = "list" });
        await vm.InitializeAsync();
        Assert.True(vm.IsList);

        // 専用キーが共通キーに優先(以後は連動しない)
        var vm2 = NewVm(new AppSettings { DisplayMode = "list", WorkTabDisplayMode = "grid" });
        await vm2.InitializeAsync();
        Assert.True(vm2.IsGrid);
    }

    // ---- ECO-038 回帰: グリッド/リスト切替の即時反映 ----

    [Fact]
    public async Task グリッドリスト切替は本体表示プロパティShowBrowseへ即時通知される()
    {
        await SeedAsync();
        var ws = new WorkspaceService(_db.Workspaces, _db.Clock);
        await ws.AddImagesToDefaultAsync(new[] { "a", "b", "c" });
        var vm = NewVm();
        await vm.InitializeAsync();

        Assert.True(vm.ShowBrowseGrid);
        Assert.False(vm.ShowBrowseList);

        var notified = new List<string?>();
        vm.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        // XAML 本体(WorkTabView.axaml 657/706)は派生の ShowBrowse* にバインド — 切替コマンドが
        // これを通知しないとボタン active だけ替わり本体が不変になる(ECO-038 の症状)
        vm.SetListCommand.Execute(null);
        Assert.True(vm.ShowBrowseList);
        Assert.False(vm.ShowBrowseGrid);
        Assert.Contains(notified, p => string.IsNullOrEmpty(p) || p == nameof(vm.ShowBrowseList));

        notified.Clear();
        vm.SetGridCommand.Execute(null);
        Assert.True(vm.ShowBrowseGrid);
        Assert.Contains(notified, p => string.IsNullOrEmpty(p) || p == nameof(vm.ShowBrowseGrid));
    }

    /// <summary>
    /// ECO-058 / CP-NFR-026: 画面外セルまで全件生成する非仮想 ItemsControl の再混入を、
    /// 実レイアウト後の visual tree に残るセル数で検出する。
    /// </summary>
    [Fact]
    [Trait("cp", "CP-NFR-026")]
    public async Task 中央ブラウズはグリッドとリストで画面外セルを全件実体化しない()
    {
        const int itemCount = 256;
        var vm = NewVm();
        for (var i = 0; i < itemCount; i++)
        {
            vm.Items.Add(new ImageItemVM(
                $"nfr-{i}", $"nfr-{i:D3}.jpg",
                isFolder: false, isPlaceholder: false, hasThumb: false,
                thumbBrush: null, selectable: false, isSelected: false, hasTagDots: false,
                tagDots: [], sizeLabel: "", dateLabel: "", target: null));
        }
        vm.WsEmpty = false;

        await HeadlessApp.Session.Dispatch(() =>
        {
            var window = new Window
            {
                Content = new WorkTabView { DataContext = vm },
                Width = 1366,
                Height = 900,
            };

            try
            {
                window.Show();
                RunLayoutJobs();
                var gridCells = window.GetVisualDescendants().OfType<Border>()
                    .Count(b => b.Classes.Contains("thumbCellWrap") && b.IsEffectivelyVisible);

                vm.SetListCommand.Execute(null);
                RunLayoutJobs();
                var listRows = window.GetVisualDescendants().OfType<Border>()
                    .Count(b => b.Classes.Contains("imageListRow") && b.IsEffectivelyVisible);

                Assert.True(
                    gridCells is > 0 and < itemCount / 2 && listRows is > 0 and < itemCount / 2,
                    $"画面外セルを全件実体化: Items={itemCount}, grid={gridCells}, list={listRows}");
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static void RunLayoutJobs()
    {
        for (var i = 0; i < 8; i++)
            Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public async Task CloseMenusFromDismissはその他メニューも閉じる()
    {
        await SeedAsync();
        var ws = new WorkspaceService(_db.Workspaces, _db.Clock);
        await ws.AddImagesToDefaultAsync(new[] { "a" });
        var vm = NewVm();
        await vm.InitializeAsync();

        vm.ToggleMoreMenuCommand.Execute(null);
        Assert.True(vm.MoreMenuOpen);
        vm.CloseMenusFromDismiss(); // light-dismiss 経路
        Assert.False(vm.MoreMenuOpen);
    }

    /// <summary>ECO-044(IMG-011 裁定③): 作業タブでも取り消す= 補償 Undo が画像タブと同一意味論。</summary>
    [Fact]
    public async Task 整理マージの取り消しで整理対象が現スペースへ戻る()
    {
        await _db.Folders.AddAsync(new SyncFolder { Id = Folder, Name = "F", Path = @"C:\f" });
        foreach (var id in new[] { "dup_a", "dup_b" })
            await _db.Images.AddAsync(new ImageRecord
            {
                Id = id, SyncFolderId = Folder, RelativePath = $"{id}.jpg", FileName = $"{id}.jpg",
                FileSize = 100, Hash = "h", Status = ImageStatus.Normal,
                CreatedDate = "2026-01-01T00:00:00.000Z", ModifiedDate = "2026-01-01T00:00:00.000Z",
            });
        var ws = new WorkspaceService(_db.Workspaces, _db.Clock);
        await ws.AddImagesToDefaultAsync(new[] { "dup_a", "dup_b" });
        var vm = NewVm();
        await vm.InitializeAsync();

        vm.ToggleOrganizeCommand.Execute(null);
        vm.HandleItemClick(vm.Items.Single(i => i.Id == "dup_a"), ctrl: false, shift: false); // マージ先
        vm.HandleItemClick(vm.Items.Single(i => i.Id == "dup_b"), ctrl: false, shift: false); // 整理対象
        await vm.ExecuteMergeCommand.ExecuteAsync(null);
        Assert.True(vm.OrganizeDone);
        Assert.True(vm.CanUndo); // マージ直後は取り消し可能

        await vm.UndoMergeCommand.ExecuteAsync(null);

        Assert.False(vm.OrganizeDone);   // 完了状態を畳んでトレイへ戻る
        Assert.True(vm.OrganizeMode);    // 整理モードは維持
        Assert.Equal(ImageStatus.Normal, (await _db.Images.GetByIdAsync("dup_b"))!.Status); // source 復元
        Assert.Contains(vm.Items, i => i.Id == "dup_b"); // 現スペースの一覧へ戻る
    }
}
