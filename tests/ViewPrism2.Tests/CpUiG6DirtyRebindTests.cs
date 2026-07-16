using ViewPrism2.App.Services;
using ViewPrism2.App.ViewModels;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-UI-G6(ECO-102): 未保存編集(dirty)中のタグ定義変更の反映 — 構造の保護と表示の鮮度の分離(案 A)。
/// dirty ガード(c103967 由来)は未保存ツリーを守るが、行の Tag 参照・NumericMeta・PlacingTag の
/// 表示鮮度まで一括で犠牲にしていた(ECO-046 の削除面と対になる編集面の谷間)。
/// プローブ先行(R5): 是正前は表示が旧定義のまま(赤)→ 是正で最新へ追随・構造/dirty は不変。
/// </summary>
[Trait("cp", "CP-UI-G6")]
public sealed class CpUiG6DirtyRebindTests : IDisposable
{
    private readonly TempDb _db = new();
    private readonly ViewService _views;
    private readonly TagService _tagService;
    private readonly StubWindows _windows = new();

    public CpUiG6DirtyRebindTests()
    {
        _views = new ViewService(_db.Views, _db.Clock);
        _tagService = new TagService(_db.Tags);
    }

    public void Dispose() => _db.Dispose();

    private async Task<TagsTabViewModel> SetupAsync(params Tag[] tags)
    {
        var view = (await _views.CreateAsync("V")).Value!;
        var tab = new TagsTabViewModel(_views, _tagService, _db.Tags, TestLoc.Ja(), _windows);
        await tab.EnsureLoadedAsync();
        await tab.Editor.LoadAsync(view, tags.ToDictionary(t => t.Id, StringComparer.Ordinal));
        return tab;
    }

    /// <summary>タグ編集ダイアログの完了経路(Palette.EditCommand→TagsChanged→OnTagsChangedAsync)を通す。</summary>
    private async Task TriggerTagsChangedAsync(TagsTabViewModel tab, string tagId)
    {
        _windows.TagEditorResult = true; // ダイアログ=保存済みで閉じた扱い(DB 変更は事前に実施済み)
        var row = tab.Palette.Tags.Single(r => r.Tag.Id == tagId);
        await tab.Palette.EditCommand.ExecuteAsync(row);
    }

    [Fact]
    public async Task dirty編集中のタグ改名と色変更が中央ペインの既存ノードへ即反映される()
    {
        var tag = (await _tagService.CreateAsync("旧名", TagType.Textual, color: "#12a594")).Value!;
        var other = (await _tagService.CreateAsync("印", TagType.Simple)).Value!;
        var tab = await SetupAsync(tag, other);

        // dirty な編集ツリーを作る(親=旧名 { 子=印 }・別名/条件/HOME も載せて随伴を検査)
        var parent = tab.Editor.AddNode(tag, null)!;
        var child = tab.Editor.AddNode(other, parent)!;
        tab.Editor.SetCondition(parent, HierarchyConditionType.Equals, """{"value":"x"}""");
        tab.Editor.ToggleHomeCommand.Execute(child);
        Assert.True(tab.Editor.IsDirty);

        // パレット経由でタグ定義を変更(改名+色変更)→ TagsChanged
        Assert.True((await _tagService.UpdateAsync(tag with { Name = "新名", Color = "#e93d82" })).IsSuccess);
        await TriggerTagsChangedAsync(tab, tag.Id);

        // 表示の鮮度: 既存ノードが最新定義へ追随(是正前=旧名/旧色のままで赤)
        Assert.Equal("新名", parent.DisplayName);
        Assert.Equal("#e93d82", parent.Color);
        Assert.Equal("#29e93d82", parent.RingColor);

        // 構造の保護: ツリー・別名・条件・HOME・dirty は不変
        Assert.True(tab.Editor.IsDirty);
        Assert.Same(parent, tab.Editor.Roots.Single());
        Assert.Same(child, parent.Children.Single());
        Assert.True(parent.HasCondition);
        Assert.True(child.IsHome);

        // 保存回帰: 改名後の保存で階層が無傷に永続する
        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)tab.Editor.SaveCommand).ExecuteAsync(null);
        Assert.False(tab.Editor.IsDirty);
        Assert.Equal(2, (await _views.GetHierarchyAsync(tab.Editor.View!.Id)).Count);
    }

    [Fact]
    public async Task dirty編集中の数値範囲変更が行の数値メタへ即反映される()
    {
        var rating = (await _tagService.CreateAsync("評価", TagType.Numeric)).Value!;
        Assert.True((await _tagService.SetNumericSettingsAsync(rating.Id, 1, 5, 1, "★")).IsSuccess);
        var tab = await SetupAsync(rating);

        var node = tab.Editor.AddNode(rating, null)!;
        Assert.Equal("1–5 ★", node.NumericMeta);
        Assert.True(tab.Editor.IsDirty);

        Assert.True((await _tagService.SetNumericSettingsAsync(rating.Id, 1, 10, 1, "★")).IsSuccess);
        await TriggerTagsChangedAsync(tab, rating.Id);

        Assert.Equal("1–10 ★", node.NumericMeta); // 是正前=旧範囲のままで赤
        Assert.True(tab.Editor.IsDirty);
    }

    [Fact]
    public async Task 配置モード中のタグ改名は帯文言と配置対象へ即反映される()
    {
        var tag = (await _tagService.CreateAsync("旧名", TagType.Simple)).Value!;
        var tab = await SetupAsync(tag);
        tab.Editor.AddNode(tag, null); // dirty にして再ロード経路(placing 解除)を避ける

        tab.TogglePlacing(tab.Palette.Tags.Single(r => r.Tag.Id == tag.Id));
        Assert.True(tab.Editor.IsPlacing);

        Assert.True((await _tagService.UpdateAsync(tag with { Name = "新名" })).IsSuccess);
        await TriggerTagsChangedAsync(tab, tag.Id);

        Assert.Equal("新名", tab.Editor.PlacingTag?.Name);                                  // 是正前=旧参照で赤
        Assert.Contains("新名", tab.Editor.PlacingBannerText, StringComparison.Ordinal);
        Assert.True(tab.Editor.IsPlacing);                                                   // 配置モードは維持

        // 配置実行で最新定義のノードが生まれる
        tab.Editor.InsertRootEndCommand.Execute(null);
        Assert.Equal("新名", tab.Editor.Roots[^1].DisplayName);
    }

    [Fact]
    public async Task 非dirty時は従来どおり全再ロードで追随する()
    {
        var tag = (await _tagService.CreateAsync("旧名", TagType.Simple)).Value!;
        var tab = await SetupAsync(tag);
        tab.Editor.AddNode(tag, null);
        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)tab.Editor.SaveCommand).ExecuteAsync(null);
        Assert.False(tab.Editor.IsDirty);

        // 非 dirty 経路はビュー選択行が前提(SelectedViewRow を実導線で確立する)
        var viewRow = tab.Views.Single();
        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)tab.SelectViewCommand).ExecuteAsync(viewRow);

        Assert.True((await _tagService.UpdateAsync(tag with { Name = "新名" })).IsSuccess);
        await TriggerTagsChangedAsync(tab, tag.Id);

        Assert.Equal("新名", tab.Editor.Roots.Single().DisplayName); // 従来経路(LoadAsync)の回帰なし
        Assert.False(tab.Editor.IsDirty);
    }

    private sealed class StubWindows : IWindowService
    {
        public bool TagEditorResult { get; set; }

        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(true);

        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);

        public Task ShowFolderManagementAsync() => Task.CompletedTask;

        public Task ShowSettingsAsync() => Task.CompletedTask;

        public Task ShowSnapshotsAsync() => Task.CompletedTask;

        public Task ShowCollectionExportAsync(string collectionId) => Task.CompletedTask;

        public Task ShowCollectionImportAsync(string collectionId) => Task.CompletedTask;

        public Task<bool> ShowTagEditorAsync(Tag? existing) => Task.FromResult(TagEditorResult);

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
