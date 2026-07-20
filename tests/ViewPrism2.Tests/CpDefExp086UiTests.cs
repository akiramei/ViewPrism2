using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ViewPrism2.App.Services;
using ViewPrism2.App.ViewModels;
using ViewPrism2.App.Views;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-DEFEXP-086(UI 層): 定義値展開の設定 UI と ビュー軸チップの見え方(CAD VC-TAG-1〜3 / VC-IMG-6/7)。
/// fixture: 都道府県(textual・候補値 3 件へ簡略・closed)> 評価(numeric 1-5)。
/// hokkaido.jpg=北海道+評価5 / ezo.jpg=蝦夷(定義外) / none.jpg=タグなし。
/// </summary>
[Trait("cp", "CP-DEFEXP-086")]
public sealed class CpDefExp086UiTests : IDisposable
{
    private readonly TempDb _db = new();
    private SyncFolder _col = null!;
    private View _view = null!;
    private Tag _prefTag = null!;
    private Tag _ratingTag = null!;
    private string _prefNodeId = null!;

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task タグ編集の値の扱いが保存往復する()
    {
        // REQ-095: 閉じた値集合の保存→再読込→既定(入力補助)への戻し
        var tagService = new TagService(_db.Tags);
        var tag = (await tagService.CreateAsync("県", TagType.Textual)).Value!;

        var vm = new TagEditorViewModel(tag, tagService, _db.Tags, TestLoc.Ja());
        vm.PredefinedValues.Add("北海道");
        Assert.False(vm.IsClosedDomain); // 既定=入力補助
        vm.SelectDomainClosedCommand.Execute(null);
        await vm.SaveCommand.ExecuteAsync(null);

        var saved = await _db.Tags.GetTextualSettingsAsync(tag.Id);
        Assert.Equal(TagValueDomain.Closed, saved!.ValueDomain);
        Assert.Equal(["北海道"], saved.PredefinedValues);

        // 再編集で復元される(LoadAsync)
        var vm2 = new TagEditorViewModel(tag, tagService, _db.Tags, TestLoc.Ja());
        await vm2.LoadAsync();
        Assert.True(vm2.IsClosedDomain);

        // 説明カードは選択へ追随(CAD VC-TAG-1)
        Assert.Contains("未定義値", vm2.DomainNote, StringComparison.Ordinal);
        vm2.SelectDomainSuggestCommand.Execute(null);
        Assert.Contains("入力補助", vm2.DomainNote, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 階層エディタの展開モードがバッチ保存で永続しバッジへ出る()
    {
        // REQ-096: ノード単位の展開モード+0件隠すの永続往復と行バッジ(非既定のみ=CAD VC-TAG-3)
        await SeedAsync();
        var views = new ViewService(_db.Views, _db.Clock);
        var tagById = (await _db.Tags.GetAllAsync()).ToDictionary(t => t.Id, StringComparer.Ordinal);
        var editor = new HierarchyEditorViewModel(views, TestLoc.Ja(), new StubWindows(), _db.Tags);
        await editor.LoadAsync(await views.GetAsync(_view.Id), tagById);

        var prefNode = editor.Roots.Single();
        Assert.Equal(HierarchyExpansionMode.Defined, prefNode.ExpansionMode); // seed の保存値が復元される
        Assert.True(prefNode.HasExpansionBadge);
        Assert.Equal("定義値", prefNode.ExpansionBadgeText);

        // 0件隠す込みへ変更 → バッジ文言追随 → バッチ保存 → DB 永続
        editor.SetExpansion(prefNode, HierarchyExpansionMode.DefinedAndObserved, hideEmptyValues: true);
        Assert.True(editor.IsDirty);
        Assert.Equal("定義+観測・0件を隠す", prefNode.ExpansionBadgeText);
        await editor.SaveCommand.ExecuteAsync(null);

        var saved = (await views.GetHierarchyAsync(_view.Id)).Single(n => n.Id == _prefNodeId);
        Assert.Equal(HierarchyExpansionMode.DefinedAndObserved, saved.ExpansionMode);
        Assert.True(saved.HideEmptyValues);

        // 既定(観測値)ではバッジを出さない(ノイズ回避)
        editor.SetExpansion(prefNode, HierarchyExpansionMode.Observed, hideEmptyValues: false);
        Assert.False(prefNode.HasExpansionBadge);
    }

    [Fact]
    public async Task ビュー軸チップが定義順と0件淡色と未定義値検出を表現する()
    {
        // REQ-095/096・CAD VC-IMG-6: 定義順(青森県=0件でも表示・淡色)+末尾に未定義値チップ(蝦夷)
        var vm = await NewViewVmAsync();

        // root チップ=タグ名ノード(都道府県)。潜ると定義値の値ノードチップ群(CAD: EF→職種と同型)
        vm.ClickChipCommand.Execute(vm.Chips.Single(c => c.Label == "都道府県"));
        Assert.Equal(["北海道", "青森県", "沖縄県", "蝦夷"], vm.Chips.Select(c => c.Label));
        var hokkaido = vm.Chips[0];
        Assert.Equal("1", hokkaido.Count);
        Assert.False(hokkaido.IsUndef);

        var aomori = vm.Chips[1];
        Assert.Equal("0", aomori.Count);
        Assert.Equal(Color.Parse("#8a93a2"), ((ISolidColorBrush)aomori.LabelBrush).Color); // 淡色(ColoredZero)

        var ezo = vm.Chips[3];
        Assert.True(ezo.IsUndef, "定義外の付与値(蝦夷)が未定義値として検出されない");
        Assert.Equal("未定義", ezo.UndefLabel);
        Assert.Equal("1", ezo.Count);
    }

    [Fact]
    public async Task 多段リーフの選択で親equalsと子equalsに絞り込める()
    {
        // REQ-096・CAD VC-IMG-7: 都道府県 → 北海道 → 評価リーフ(1-5・0件込み)→ 評価5 = 1 件
        var vm = await NewViewVmAsync();

        vm.ClickChipCommand.Execute(vm.Chips.Single(c => c.Label == "都道府県"));
        vm.ClickChipCommand.Execute(vm.Chips.Single(c => c.Label == "北海道"));
        Assert.Equal(["hokkaido.jpg"], ImageNames(vm));

        // 評価タグ名ノードへ潜る → 定義値リーフ 1..5(0 件込み)
        vm.ClickChipCommand.Execute(vm.Chips.Single(c => c.Label == "評価"));
        Assert.Equal(["1", "2", "3", "4", "5"], vm.Chips.Select(c => c.Label));
        Assert.Equal(["0", "0", "0", "0", "1"], vm.Chips.Select(c => c.Count));

        vm.ClickChipCommand.Execute(vm.Chips.Single(c => c.Label == "5"));
        Assert.Equal(["hokkaido.jpg"], ImageNames(vm)); // 都道府県=北海道 AND 評価=5(数値比較)
    }

    [Fact]
    public async Task ゼロ件を隠すノードでは0件の定義値チップが出ない()
    {
        // REQ-096 裁定 d: hide_empty_values=1 → 表示のみスキップ(未定義値チップと 1 件以上は残る)
        await HeadlessApp.Session.Dispatch(() => true, CancellationToken.None);
        await SeedAsync(hideEmpty: true);
        var vm = await BuildViewVmAsync();

        vm.ClickChipCommand.Execute(vm.Chips.Single(c => c.Label == "都道府県"));
        Assert.Equal(["北海道", "蝦夷"], vm.Chips.Select(c => c.Label));
    }

    [Fact]
    public async Task 選択中ビューの0件隠し保存が追加のビュー切替なしで反映される()
    {
        // ECO-096/R5: タグタブ保存後の stale 再読込経路を直接再現する。
        // 現在 path は「都道府県」のまま保持し、同一 view の hide_empty_values だけを DB 更新する。
        await SeedAsync();
        await HeadlessApp.Session.Dispatch(async () =>
        {
            // Brush を作る VM 構築から描画まで共有 Headless UI スレッド内(ECO-083/084 の初期化規律)。
            var vm = await BuildViewVmAsync();
            vm.ClickChipCommand.Execute(vm.Chips.Single(c => c.Label == "都道府県"));
            Assert.Contains(vm.Chips, c => c.Label == "青森県" && c.Count == "0");

            var views = new ViewService(_db.Views, _db.Clock);
            var hierarchy = (await views.GetHierarchyAsync(_view.Id))
                .Select(n => new HierarchyNode
                {
                    Id = n.Id,
                    ViewId = n.ViewId,
                    TagId = n.TagId,
                    ParentId = n.ParentId,
                    Position = n.Position,
                    Alias = n.Id == _prefNodeId ? "地域" : n.Alias,
                    ConditionType = n.ConditionType,
                    ConditionValue = n.ConditionValue,
                    ExpansionMode = n.ExpansionMode,
                    HideEmptyValues = n.Id == _prefNodeId || n.HideEmptyValues,
                })
                .ToList();
            Assert.True((await views.SaveHierarchyAsync(_view.Id, hierarchy, null)).IsSuccess);

            await vm.ReloadTagCatalogAsync(); // MainWindow の画像タブ復帰時と同じ公開経路

            Assert.Equal(["北海道", "蝦夷"], vm.Chips.Select(c => c.Label));
            Assert.Equal(["地域"], vm.Crumbs.Select(c => c.Name)); // 現在 path は新 graph の別名へ再束縛

            // R7/CAD VC-IMG-6: 実描画でも保存済み別名+非0件/未定義値だけが見え、0件値は残らない。
            var window = new Window { Content = new ImageTabView { DataContext = vm }, Width = 1366, Height = 900 };
            window.Show();
            RunJobs();
            var visibleLabels = window.GetVisualDescendants().OfType<TextBlock>()
                .Where(t => t.IsVisible)
                .Select(t => t.Text)
                .Where(t => t is not null)
                .ToList();
            Assert.Contains("地域", visibleLabels);
            Assert.Contains("北海道", visibleLabels);
            Assert.Contains("蝦夷", visibleLabels);
            Assert.DoesNotContain("青森県", visibleLabels);
            Assert.DoesNotContain("沖縄県", visibleLabels);
            window.Close();

            // read-across: 現在 path のノード自体が保存で消えた場合は、旧 GraphNode 参照を残さず root へ縮退する。
            Assert.True((await views.SaveHierarchyAsync(_view.Id, [], null)).IsSuccess);
            await vm.ReloadTagCatalogAsync();
            Assert.Empty(vm.Crumbs);
            Assert.Empty(vm.Chips);
            Assert.True(vm.IsViewAxis);
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task 未定義値チップは破線枠とバッジで通常チップと視覚区別される()
    {
        // R7/CAD VC-IMG-6(gate① 0d63066): 未定義値=琥珀・破線枠+「未定義」小バッジ。0 件チップは淡色で表示維持。
        // 破線は Border 非対応のため Rectangle(StrokeDashArray)重ね — 実レイアウトで可視を実測する。
        await SeedAsync();
        await HeadlessApp.Session.Dispatch(async () =>
        {
            var vm = await BuildViewVmAsync(); // UI スレッド内で構築(Brush スレッドアフィニティ=ECO-084 教訓)
            vm.ClickChipCommand.Execute(vm.Chips.Single(c => c.Label == "都道府県"));
            var window = new Window { Content = new ImageTabView { DataContext = vm }, Width = 1366, Height = 900 };
            window.Show();
            RunJobs();

            var dashed = window.GetVisualDescendants().OfType<Rectangle>()
                .Where(r => r.IsVisible && r.StrokeDashArray is { Count: > 0 })
                .ToList();
            Assert.True(dashed.Count == 1, $"破線枠の未定義値チップが 1 個でない(実測 {dashed.Count})");

            var badge = window.GetVisualDescendants().OfType<TextBlock>()
                .Where(t => t.IsVisible && t.Text == "未定義")
                .ToList();
            Assert.True(badge.Count == 1, "「未定義」バッジが未定義値チップにだけ出ていない");

            // 0 件の定義値チップ(青森県)も描画されている(淡色で表示維持=裁定 d 既定)
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),
                t => t.IsVisible && t.Text == "青森県");

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("ja")]
    [InlineData("en")]
    public async Task 値の扱いセグメントは日英ともラベルが切れず余白を持つ(string locale)
    {
        // R7/CAD VC-TAG-1+GF-084-01/GF-086-01: 可変内容(文字量・ロケール)の収まりは**余白まで**実測する。
        // GF-086-01 所見=segmentTab テンプレ(Padding 左右 0・3 等分 Stretch 前提)を内容幅で流用し
        // 余白 0 で密着/はみ出し — 初版 probe は「radio 幅≥ラベル幅」しか見ておらず素通しした。
        var tagService = new TagService(_db.Tags);
        var tag = (await tagService.CreateAsync("県", TagType.Textual)).Value!;
        await HeadlessApp.Session.Dispatch(() =>
        {
            var vm = new TagEditorViewModel(tag, tagService, _db.Tags,
                locale == "en" ? TestLoc.En() : TestLoc.Ja());
            var window = new TagEditorWindow { DataContext = vm };
            window.Show();
            RunJobs();

            var radios = window.GetVisualDescendants().OfType<RadioButton>()
                .Where(r => r.GroupName == "valueDomain")
                .ToList();
            Assert.Equal(2, radios.Count);
            foreach (var radio in radios)
            {
                var label = radio.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault();
                Assert.True(label is not null && label.Bounds.Width > 0,
                    $"{locale}: 値の扱いラベルが描画されない");
                double slack = radio.Bounds.Width - label!.Bounds.Width; // 左右余白の合計
                Assert.True(slack >= 20,
                    $"{locale}: ラベル幅 {label.Bounds.Width:0.0} / ボタン幅 {radio.Bounds.Width:0.0} = "
                    + $"余白合計 {slack:0.0}px(<20)— はみ出し/密着(GF-086-01・GF-084-01 同型)");
            }

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task テキストタブの値の扱いはプレビュー帯と重ならずスクロールで到達できる()
    {
        // GF-086-01: 「値の扱い」追加でテキストタブの内容高が MaxHeight を超え、本体
        // (ClipToBounds=false)が下端固定のプレビュー帯・フッターへ重なって描画された。
        // CAD 契約(tag_tab.md layoutInvariant)=ボディ単一スクロール・docked フッター常時可視・
        // プレビュー帯固定。説明カードの実描画矩形がプレビュー帯と交差しないことを実測で封止する。
        var tagService = new TagService(_db.Tags);
        var tag = (await tagService.CreateAsync("性別", TagType.Textual)).Value!;
        Assert.True((await tagService.SetTextualSettingsAsync(tag.Id, ["男", "女"])).IsSuccess);
        await HeadlessApp.Session.Dispatch(async () =>
        {
            var vm = new TagEditorViewModel(tag, tagService, _db.Tags, TestLoc.Ja());
            await vm.LoadAsync();
            var window = new TagEditorWindow { DataContext = vm };
            window.Show();
            RunJobs();

            var note = window.GetVisualDescendants().OfType<TextBlock>()
                .First(t => t.Text == vm.DomainNote);
            var preview = window.GetVisualDescendants().OfType<Border>()
                .First(b => b.Classes.Contains("previewStrip"));

            // ボディ単一スクロール(CAD 契約)が存在し、説明カードはその中にある
            var scroller = window.GetVisualDescendants().OfType<ScrollViewer>()
                .FirstOrDefault(s => s.GetVisualDescendants().Contains(note));
            Assert.True(scroller is not null, "本体のボディ単一スクロール(CAD layoutInvariant)が無い(GF-086-01)");

            // 末尾までスクロールしても、本体の要素が固定領域(プレビュー帯)へ重なって描画されない
            scroller!.ScrollToEnd();
            RunJobs();
            var noteTb = note.GetTransformedBounds()!.Value;
            var previewTb = preview.GetTransformedBounds()!.Value;
            var noteRect = noteTb.Bounds.TransformToAABB(noteTb.Transform);     // 実描画矩形(global)
            var previewRect = previewTb.Bounds.TransformToAABB(previewTb.Transform);
            Assert.False(noteRect.Intersects(previewRect),
                $"値の扱いの説明カードがプレビュー帯へ重なって描画される(GF-086-01: note={noteRect} preview={previewRect})");
            // 到達可能性: 末尾スクロールで説明カードが viewport 内に完全可視(クリップされない)
            var visible = noteRect.Intersect(noteTb.Clip);
            Assert.True(visible.Height >= noteRect.Height - 0.5,
                $"説明カードがスクロールで完全表示できない(可視 {visible.Height:0.0}/{noteRect.Height:0.0}px)");

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("ja")]
    [InlineData("en")]
    public async Task 配置タグ設定の展開モード選択肢は日英ともウィンドウ内に収まる(string locale)
    {
        // GF-086-01 の read-across(GF-073 教訓=同じ失敗は面を変えて連鎖する):
        // 幅 400 のダイアログで展開モード 4 値がはみ出さない・互いに重ならないことを日英で実測。
        var loc = locale == "en" ? TestLoc.En() : TestLoc.Ja();
        await HeadlessApp.Session.Dispatch(() =>
        {
            var tag = new Tag { Id = "t1", Name = "県", Type = TagType.Textual };
            var vm = new NodeConditionDialogViewModel(tag, null, null, loc);
            var window = new NodeConditionDialog { DataContext = vm };
            window.Show();
            RunJobs();

            var radios = window.GetVisualDescendants().OfType<RadioButton>()
                .Where(r => r.GroupName == "expansionMode")
                .ToList();
            Assert.Equal(4, radios.Count);
            var rects = radios.Select(r =>
            {
                var tb = r.GetTransformedBounds()!.Value;
                return tb.Bounds.TransformToAABB(tb.Transform);
            }).ToList();
            foreach (var (radio, rect) in radios.Zip(rects))
            {
                var label = radio.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault();
                Assert.True(label is not null && label.Bounds.Width > 0, $"{locale}: 展開モードラベルが描画されない");
                Assert.True(rect.Right <= window.Bounds.Width + 0.5,
                    $"{locale}: 展開モード選択肢がウィンドウからはみ出す(right={rect.Right:0.0} > {window.Bounds.Width:0.0})");
            }

            for (var i = 0; i < rects.Count; i++)
            {
                for (var j = i + 1; j < rects.Count; j++)
                {
                    Assert.False(rects[i].Intersects(rects[j]),
                        $"{locale}: 展開モード選択肢 {i} と {j} が重なる");
                }
            }

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    // ---- ヘルパ ----

    private static void RunJobs()
    {
        for (var i = 0; i < 8; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static List<string> ImageNames(ImageTabViewModel vm)
        => vm.Items.Where(i => !i.IsFolder).Select(i => i.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();

    private async Task<ImageTabViewModel> NewViewVmAsync()
    {
        // 共有 Headless セッションの先行初期化(ECO-084 教訓: 初期化順序の決定化)
        await HeadlessApp.Session.Dispatch(() => true, CancellationToken.None);
        await SeedAsync();
        return await BuildViewVmAsync();
    }

    private async Task<ImageTabViewModel> BuildViewVmAsync()
    {
        var vm = TestImageTab.NewVm(_db);
        await vm.InitializeAsync(_col.Id);
        await vm.SelectAxisCommand.ExecuteAsync(_view.Id);
        Assert.True(vm.IsViewAxis);
        return vm;
    }

    /// <summary>DB のみの seed。都道府県(closed・候補値3)ノード=defined > 評価(1-5)ノード=defined。</summary>
    private async Task SeedAsync(bool hideEmpty = false)
    {
        _col = new SyncFolder { Id = IdGenerator.NewId(), Name = "C", Path = @"C:\col-086" };
        await _db.Folders.AddAsync(_col);
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in new[] { "hokkaido.jpg", "ezo.jpg", "none.jpg" })
        {
            var id = IdGenerator.NewId();
            ids[name] = id;
            await _db.Images.AddAsync(new ImageRecord
            {
                Id = id, SyncFolderId = _col.Id, RelativePath = name, FileName = name,
                FileSize = 1, Hash = "H" + name, Status = ImageStatus.Normal,
                CreatedDate = "2026-07-14T00:00:00.000Z", ModifiedDate = "2026-07-14T00:00:00.000Z",
            });
        }

        var tagService = new TagService(_db.Tags);
        _prefTag = (await tagService.CreateAsync("都道府県", TagType.Textual)).Value!;
        Assert.True((await tagService.SetTextualSettingsAsync(
            _prefTag.Id, ["北海道", "青森県", "沖縄県"], TagValueDomain.Closed)).IsSuccess);
        _ratingTag = (await tagService.CreateAsync("評価", TagType.Numeric)).Value!;
        Assert.True((await tagService.SetNumericSettingsAsync(_ratingTag.Id, 1, 5, 1, null)).IsSuccess);

        Assert.True((await tagService.TagImageAsync(ids["hokkaido.jpg"], _prefTag.Id, "北海道")).IsSuccess);
        Assert.True((await tagService.TagImageAsync(ids["hokkaido.jpg"], _ratingTag.Id, "5")).IsSuccess);
        Assert.True((await tagService.TagImageAsync(ids["ezo.jpg"], _prefTag.Id, "蝦夷")).IsSuccess);

        var viewService = new ViewService(_db.Views, _db.Clock);
        _view = (await viewService.CreateAsync("V086")).Value!;
        _prefNodeId = IdGenerator.NewId();
        var prefNode = new HierarchyNode
        {
            Id = _prefNodeId, ViewId = _view.Id, TagId = _prefTag.Id, Position = 0,
            ExpansionMode = HierarchyExpansionMode.Defined, HideEmptyValues = hideEmpty,
        };
        var ratingNode = new HierarchyNode
        {
            Id = IdGenerator.NewId(), ViewId = _view.Id, TagId = _ratingTag.Id,
            ParentId = _prefNodeId, Position = 0,
            ExpansionMode = HierarchyExpansionMode.Defined,
        };
        Assert.True((await viewService.SaveHierarchyAsync(_view.Id, [prefNode, ratingNode], null)).IsSuccess);
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
