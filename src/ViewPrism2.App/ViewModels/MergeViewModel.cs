using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ViewPrism2.App.Services;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using ViewPrism2.Core.Services.Similarity;

namespace ViewPrism2.App.ViewModels;

/// <summary>マージ UI に表示する画像 1 件(マージ先/元の役割ラベル付き)。</summary>
public sealed record MergeImageViewModel(ImageEntry Entry, bool IsTarget, string RoleLabel)
{
    public string FileName => Entry.Record.FileName;

    public string AbsolutePath => Entry.AbsolutePath;
}

/// <summary>統合後タグプレビューの 1 行(タグ名+値)。</summary>
public sealed record MergedTagPreviewViewModel(string TagName, string? Value)
{
    /// <summary>simple タグ(値なし)はチェック印、値ありは値。</summary>
    public string DisplayValue => string.IsNullOrEmpty(Value) ? "✓" : Value!;
}

/// <summary>
/// マージ UI の ViewModel(M-UI-SIMILARITY-023 / E-UI-MERGE-036、仕様 §2.10.5)。
/// マージ先(1 枚・保持)とマージ元(1 枚以上・統合)を受け取り、統合後タグプレビュー(MergeCalculator を呼ぶ)と
/// 非破壊注記(物理ファイルは削除されない)を示し、実行は MergeService.MergeAsync のみ経由する。
/// マージ実行は原子(UI 側でタグ操作を再実装しない)。INV-009: UI からも物理ファイルを操作しない。
/// </summary>
public sealed partial class MergeViewModel : ObservableObject
{
    private readonly ImageEntry _target;
    private readonly IReadOnlyList<ImageEntry> _sources;
    private readonly IReadOnlyDictionary<string, Tag> _tagById;
    private readonly MergeService _mergeService;
    private readonly LocalizationService _localization;

    public MergeViewModel(
        ImageEntry target,
        IReadOnlyList<ImageEntry> sources,
        IReadOnlyDictionary<string, Tag> tagById,
        MergeService mergeService,
        LocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(tagById);
        _target = target;
        _sources = sources;
        _tagById = tagById;
        _mergeService = mergeService;
        _localization = localization;
        Loc = new LocalizationProxy(localization);
        localization.CultureChanged += (_, _) =>
        {
            Loc = new LocalizationProxy(localization);
            OnPropertyChanged(nameof(Loc));
        };

        // マージ先(保持)を先頭・マージ元(統合)を続けて視覚区別(K-DESIGN v3.0)
        Images.Add(new MergeImageViewModel(target, IsTarget: true, localization.T("merge.roleTarget")));
        foreach (var source in sources)
        {
            Images.Add(new MergeImageViewModel(source, IsTarget: false, localization.T("merge.roleSource")));
        }

        BuildPreview();
    }

    public LocalizationProxy Loc { get; private set; }

    /// <summary>マージ先(先頭)+マージ元の表示。</summary>
    public ObservableCollection<MergeImageViewModel> Images { get; } = [];

    /// <summary>統合後タグプレビュー(MergeCalculator の純粋計算結果)。</summary>
    public ObservableCollection<MergedTagPreviewViewModel> TagPreview { get; } = [];

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isExecuting;

    /// <summary>マージ後に true(呼び出し側がダイアログを閉じてリロードする)。</summary>
    public bool Merged { get; private set; }

    /// <summary>マージ完了イベント(View が Close(true) する)。</summary>
    public event EventHandler? MergeCompleted;

    /// <summary>統合後タグプレビューを MergeCalculator で算出する(純粋計算)。</summary>
    private void BuildPreview()
    {
        var targetTags = ToImageTags(_target);
        // マージ元は id 昇順(多元の決着順、§2.10.5)
        var sourcesTagsByIdAsc = _sources
            .OrderBy(s => s.Record.Id, StringComparer.Ordinal)
            .Select(s => (IReadOnlyList<ImageTag>)ToImageTags(s))
            .ToList();

        var merged = MergeCalculator.Merge(targetTags, sourcesTagsByIdAsc);

        TagPreview.Clear();
        foreach (var tag in merged.Tags)
        {
            var name = _tagById.TryGetValue(tag.TagId, out var t) ? t.Name : tag.TagId;
            TagPreview.Add(new MergedTagPreviewViewModel(name, tag.Value));
        }
    }

    private static List<ImageTag> ToImageTags(ImageEntry entry)
        => entry.Tags
            .Select(t => new ImageTag { ImageId = entry.Record.Id, TagId = t.TagId, Value = t.Value })
            .ToList();

    [RelayCommand]
    private async Task ExecuteAsync()
    {
        if (IsExecuting)
        {
            return;
        }

        IsExecuting = true;
        try
        {
            // マージ実行は MergeService の原子 API のみ経由(UI でタグ操作を再実装しない)
            var sourceIds = _sources.Select(s => s.Record.Id).ToList();
            var result = await _mergeService.MergeAsync(_target.Record.Id, sourceIds);
            if (result.IsSuccess)
            {
                Merged = true;
                StatusMessage = _localization.T("merge.completed");
                MergeCompleted?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                StatusMessage = _localization.T("merge.failed") + ": "
                    + ErrorMessages.Resolve(_localization, result.Error);
            }
        }
        finally
        {
            IsExecuting = false;
        }
    }
}
