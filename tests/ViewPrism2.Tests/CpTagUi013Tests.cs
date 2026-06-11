using CommunityToolkit.Mvvm.Input;
using ViewPrism2.App.Services;
using ViewPrism2.App.ViewModels;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-TAGUI-013: タグ付与パネルの ViewModel ロジック(M-UI-016、REQ-046、v1.2 追加)。
/// 共通タグ算出・連番(選択順、FMEA-014)・固定値・ダイアログ内 min/max 検証・
/// 原子バッチ(REQ-027)・解除・選択 0 件プレースホルダを実 DB で exact 検査する。
/// </summary>
[Trait("cp", "CP-TAGUI-013")]
public sealed class CpTagUi013Tests : IDisposable
{
    private readonly TempDb _db = new();
    private readonly TagService _tagService;
    private readonly LocalizationService _localization;
    private readonly StubWindows _windows = new();
    private readonly SyncFolder _folder;

    public CpTagUi013Tests()
    {
        _tagService = new TagService(_db.Tags);
        _localization = new LocalizationService(
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["ja"] = new Dictionary<string, string>
                {
                    ["tag.type.simple"] = "シンプル",
                    ["tag.type.textual"] = "テキスト",
                    ["tag.type.numeric"] = "数値",
                    ["success.updated"] = "更新しました",
                    ["tagging.valueRequired"] = "値を入力してください",
                    ["tagging.numericDialog.outOfRange"] = "値が範囲外です",
                    ["tagging.numericDialog.stepMismatch"] = "値がステップ刻みに一致しません",
                },
            });
        _folder = new SyncFolder { Id = IdGenerator.NewId(), Name = "f", Path = @"C:\pics" };
        _db.Folders.AddAsync(_folder).GetAwaiter().GetResult();
    }

    public void Dispose() => _db.Dispose();

    private sealed class StubWindows : IWindowService
    {
        public IReadOnlyList<string>? NumericDialogResult { get; set; }

        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(true);

        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);

        public Task ShowFolderManagementAsync() => Task.CompletedTask;

        public Task ShowSettingsAsync() => Task.CompletedTask;

        public Task<bool> ShowTagEditorAsync(Tag? existing) => Task.FromResult(false);

        public Task<bool> ShowViewEditDialogAsync(View? existing) => Task.FromResult(false);

        public Task<IReadOnlyList<string>?> ShowNumericValueDialogAsync(
            Tag tag, NumericTagSettings? settings, int selectionCount)
            => Task.FromResult(NumericDialogResult);

        public Task<NodeConditionResult?> ShowNodeConditionDialogAsync(
            Tag tag, HierarchyConditionType? currentType, string? currentValueJson)
            => Task.FromResult<NodeConditionResult?>(null);

        public Task ShowRelinkAsync(string folderId) => Task.CompletedTask;

        public void ShowViewer(IReadOnlyList<ImageEntry> ordered, int startIndex)
        {
        }
    }

    private async Task<ImageRecord> SeedImageAsync(string name)
    {
        var image = new ImageRecord
        {
            Id = IdGenerator.NewId(),
            SyncFolderId = _folder.Id,
            RelativePath = name,
            FileName = name,
            FileSize = 1,
            Hash = new string('0', 64),
            Status = ImageStatus.Normal,
            CreatedDate = "2026-01-01T00:00:00.000Z",
            ModifiedDate = "2026-01-01T00:00:00.000Z",
        };
        await _db.Images.AddAsync(image);
        return image;
    }

    /// <summary>DB のタグ付け状態から表示系の ImageEntry を組み立てる(シェルと同じ素材)。</summary>
    private async Task<ImageEntry> EntryOfAsync(ImageRecord record)
    {
        var tagById = (await _db.Tags.GetAllAsync()).ToDictionary(t => t.Id, StringComparer.Ordinal);
        var assigned = await _db.Tags.GetImageTagsAsync(record.Id);
        var evalTags = assigned
            .Where(t => tagById.ContainsKey(t.TagId))
            .Select(t => new EvalTagValue(t.TagId, tagById[t.TagId].Type, t.Value))
            .ToList();
        return new ImageEntry(record, @"C:\pics\" + record.FileName, evalTags);
    }

    private async Task<TaggingPanelViewModel> NewPanelAsync(params ImageRecord[] selectionInOrder)
    {
        var panel = new TaggingPanelViewModel(_tagService, _db.Tags, _localization, _windows);
        var tagById = (await _db.Tags.GetAllAsync()).ToDictionary(t => t.Id, StringComparer.Ordinal);
        panel.UpdateTags(tagById);

        var entries = new List<ImageEntry>();
        foreach (var record in selectionInOrder)
        {
            entries.Add(await EntryOfAsync(record));
        }

        panel.SetSelection(entries);
        return panel;
    }

    private async Task<Dictionary<string, string?>> TagValuesAsync(string tagId)
    {
        var all = await _db.Tags.GetAllImageTagsAsync();
        return all.Where(t => t.TagId == tagId)
            .ToDictionary(t => t.ImageId, t => t.Value, StringComparer.Ordinal);
    }

    // ---- vector 1: 共通タグ算出 ----

    [Fact]
    public async Task 共通タグ算出_3画像中2画像のみ持つタグは現在のタグに出ない()
    {
        var a = await SeedImageAsync("a.jpg");
        var b = await SeedImageAsync("b.jpg");
        var c = await SeedImageAsync("c.jpg");
        var common = (await _tagService.CreateAsync("全員", TagType.Simple)).Value!;
        var partial = (await _tagService.CreateAsync("一部", TagType.Simple)).Value!;
        await _tagService.TagImagesAsync([a.Id, b.Id, c.Id], common.Id, null);
        await _tagService.TagImagesAsync([a.Id, b.Id], partial.Id, null);

        var panel = await NewPanelAsync(a, b, c);

        Assert.Contains(panel.CurrentTags, r => r.Tag.Id == common.Id);
        Assert.DoesNotContain(panel.CurrentTags, r => r.Tag.Id == partial.Id);
    }

    // ---- vector 2: 連番(選択順、FMEA-014) ----

    [Fact]
    public async Task 連番は選択順に開始値プラスiで振られる_id順でない()
    {
        var a = await SeedImageAsync("a.jpg");
        var b = await SeedImageAsync("b.jpg");
        var c = await SeedImageAsync("c.jpg");
        var tag = (await _tagService.CreateAsync("番号", TagType.Numeric)).Value!;

        // ダイアログ VM: 開始値 5・選択 3 件 → [5,6,7]
        var dialog = new NumericValueDialogViewModel(tag, null, 3, _localization)
        {
            IsSequential = true,
            StartValueText = "5",
        };
        var values = dialog.TryBuildValues();
        Assert.NotNull(values);
        Assert.Equal(["5", "6", "7"], values);

        // 選択順 [C, A, B](id 昇順とは異なる順)→ C=5, A=6, B=7
        var panel = await NewPanelAsync(c, a, b);
        var result = await panel.ApplyNumericAsync(tag.Id, values);
        Assert.True(result.IsSuccess);

        var applied = await TagValuesAsync(tag.Id);
        Assert.Equal("5", applied[c.Id]);
        Assert.Equal("6", applied[a.Id]);
        Assert.Equal("7", applied[b.Id]);
    }

    // ---- vector 3: 固定値 ----

    [Fact]
    public async Task 固定値は全選択画像に同値で適用される()
    {
        var a = await SeedImageAsync("a.jpg");
        var b = await SeedImageAsync("b.jpg");
        var c = await SeedImageAsync("c.jpg");
        var tag = (await _tagService.CreateAsync("評価", TagType.Numeric)).Value!;

        var dialog = new NumericValueDialogViewModel(tag, null, 3, _localization) { FixedValueText = "7" };
        var values = dialog.TryBuildValues();
        Assert.Equal(["7", "7", "7"], values);

        // ApplyCommand 経由(numeric → ダイアログ → 原子バッチ)
        var panel = await NewPanelAsync(a, b, c);
        _windows.NumericDialogResult = values;
        panel.SelectedTag = panel.AvailableTags.First(r => r.Tag.Id == tag.Id);
        await ((IAsyncRelayCommand)panel.ApplyCommand).ExecuteAsync(null);

        var applied = await TagValuesAsync(tag.Id);
        Assert.Equal(3, applied.Count);
        Assert.All(applied.Values, v => Assert.Equal("7", v));
    }

    // ---- vector 4: min/max のダイアログ内検証 ----

    [Fact]
    public async Task 範囲外の値はダイアログ内で拒否され適用0件()
    {
        var a = await SeedImageAsync("a.jpg");
        var tag = (await _tagService.CreateAsync("星", TagType.Numeric)).Value!;
        Assert.True((await _tagService.SetNumericSettingsAsync(tag.Id, 1, 5, null, null)).IsSuccess);
        var settings = await _db.Tags.GetNumericSettingsAsync(tag.Id);

        // 固定値 6 → 拒否
        var dialog = new NumericValueDialogViewModel(tag, settings, 1, _localization) { FixedValueText = "6" };
        Assert.Null(dialog.TryBuildValues());
        Assert.NotNull(dialog.ErrorMessage);

        // 連番 4 開始×3 件 → 4,5,6 の 6 が範囲外 → 全体拒否
        var sequential = new NumericValueDialogViewModel(tag, settings, 3, _localization)
        {
            IsSequential = true,
            StartValueText = "4",
        };
        Assert.Null(sequential.TryBuildValues());

        // 境界は受理(REQ-025: 両端含む)
        var boundary = new NumericValueDialogViewModel(tag, settings, 1, _localization) { FixedValueText = "5" };
        Assert.Equal(["5"], boundary.TryBuildValues());

        Assert.Empty(await TagValuesAsync(tag.Id)); // 適用 0 件

        // step 刻みの検証(min=1, step=2 → 4 は不一致)
        Assert.True((await _tagService.SetNumericSettingsAsync(tag.Id, 1, 9, 2, null)).IsSuccess);
        var stepSettings = await _db.Tags.GetNumericSettingsAsync(tag.Id);
        var stepDialog = new NumericValueDialogViewModel(tag, stepSettings, 1, _localization) { FixedValueText = "4" };
        Assert.Null(stepDialog.TryBuildValues());
        var stepOk = new NumericValueDialogViewModel(tag, stepSettings, 1, _localization) { FixedValueText = "5" };
        Assert.Equal(["5"], stepOk.TryBuildValues());
        _ = a;
    }

    // ---- vector 5: 原子バッチ(REQ-027) ----

    [Fact]
    public async Task 適用中1件失敗で全ロールバック()
    {
        var a = await SeedImageAsync("a.jpg");
        var b = await SeedImageAsync("b.jpg");
        var tag = (await _tagService.CreateAsync("番号", TagType.Numeric)).Value!;

        // 存在しない画像 id を選択末尾に混ぜる(FK 違反で 3 件目が失敗)
        var ghost = new ImageRecord
        {
            Id = IdGenerator.NewId(),
            SyncFolderId = _folder.Id,
            RelativePath = "ghost.jpg",
            FileName = "ghost.jpg",
            FileSize = 1,
            Hash = new string('0', 64),
            Status = ImageStatus.Normal,
            CreatedDate = "2026-01-01T00:00:00.000Z",
            ModifiedDate = "2026-01-01T00:00:00.000Z",
        };

        var panel = new TaggingPanelViewModel(_tagService, _db.Tags, _localization, _windows);
        panel.UpdateTags((await _db.Tags.GetAllAsync()).ToDictionary(t => t.Id, StringComparer.Ordinal));
        panel.SetSelection([await EntryOfAsync(a), await EntryOfAsync(b), new ImageEntry(ghost, @"C:\pics\ghost.jpg", [])]);

        var result = await panel.ApplyNumericAsync(tag.Id, ["1", "2", "3"]);

        Assert.False(result.IsSuccess);
        Assert.Empty(await TagValuesAsync(tag.Id)); // 0 件適用(部分適用なし、INV-006)
    }

    // ---- vector 6: 解除 ----

    [Fact]
    public async Task 解除は当該タグのみ選択画像全件から外す()
    {
        var a = await SeedImageAsync("a.jpg");
        var b = await SeedImageAsync("b.jpg");
        var remove = (await _tagService.CreateAsync("外す", TagType.Simple)).Value!;
        var keep = (await _tagService.CreateAsync("残す", TagType.Simple)).Value!;
        await _tagService.TagImagesAsync([a.Id, b.Id], remove.Id, null);
        await _tagService.TagImagesAsync([a.Id, b.Id], keep.Id, null);

        var panel = await NewPanelAsync(a, b);
        var row = panel.CurrentTags.First(r => r.Tag.Id == remove.Id);
        await ((IAsyncRelayCommand)panel.RemoveTagCommand).ExecuteAsync(row);

        Assert.Empty(await TagValuesAsync(remove.Id));
        Assert.Equal(2, (await TagValuesAsync(keep.Id)).Count);
    }

    // ---- vector 7: 選択 0 件プレースホルダ ----

    [Fact]
    public async Task 選択0件はプレースホルダ状態になる()
    {
        var panel = new TaggingPanelViewModel(_tagService, _db.Tags, _localization, _windows);
        panel.UpdateTags(new Dictionary<string, Tag>(StringComparer.Ordinal));
        panel.SetSelection([]);

        Assert.False(panel.HasSelection);
        Assert.Empty(panel.CurrentTags);

        var a = await SeedImageAsync("a.jpg");
        panel.SetSelection([await EntryOfAsync(a)]);
        Assert.True(panel.HasSelection);

        panel.SetSelection([]);
        Assert.False(panel.HasSelection);
    }

    // ---- 補助: textual の適用(候補値+自由入力)と検索フィルタ ----

    [Fact]
    public async Task textualは値必須で適用され検索は部分一致大文字小文字無視()
    {
        var a = await SeedImageAsync("a.jpg");
        var tag = (await _tagService.CreateAsync("Color", TagType.Textual)).Value!;
        await _tagService.SetTextualSettingsAsync(tag.Id, ["red", "blue"]);

        var panel = await NewPanelAsync(a);
        panel.SelectedTag = panel.AvailableTags.First(r => r.Tag.Id == tag.Id);

        // 候補値は predefined_values 順(REQ-024)
        var waited = 0;
        while (panel.CandidateValues.Count == 0 && waited < 100)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
            waited++;
        }

        Assert.Equal(["red", "blue"], panel.CandidateValues);

        // 値が空のままの適用は拒否(適用 0 件)
        await ((IAsyncRelayCommand)panel.ApplyCommand).ExecuteAsync(null);
        Assert.Empty(await TagValuesAsync(tag.Id));

        // 候補値の選択 → ValueText へ反映 → 適用
        panel.SelectedCandidate = "red";
        Assert.Equal("red", panel.ValueText);
        await ((IAsyncRelayCommand)panel.ApplyCommand).ExecuteAsync(null);
        Assert.Equal("red", (await TagValuesAsync(tag.Id))[a.Id]);

        // 検索: 部分一致・大文字小文字無視
        panel.SearchText = "col";
        Assert.Contains(panel.AvailableTags, r => r.Tag.Id == tag.Id);
        panel.SearchText = "xyz";
        Assert.DoesNotContain(panel.AvailableTags, r => r.Tag.Id == tag.Id);
    }
}
