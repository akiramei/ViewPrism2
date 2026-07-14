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
    public async Task 値の扱いセグメントは日英ともラベルが切れない(string locale)
    {
        // R7/CAD VC-TAG-1+GF-084-01 教訓: 可変内容(文字量・ロケール)の収まりを両ロケールで実測する。
        // Suggestions/Closed set(en)がセグメントからはみ出さない(RadioButton=内容幅の segmentTab 共有部品)。
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
                Assert.True(radio.Bounds.Width >= label!.Bounds.Width,
                    $"{locale}: ラベルがセグメントからはみ出す({label.Bounds.Width:0.0} > {radio.Bounds.Width:0.0})");
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
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(true);
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
