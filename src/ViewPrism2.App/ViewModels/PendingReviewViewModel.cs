using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ViewPrism2.App.Services;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;
using ViewPrism2.Core.Services;
using ViewPrism2.Core.Services.Repair;

namespace ViewPrism2.App.ViewModels;

/// <summary>pending 一覧の 1 行(PD-2/3 左ペイン)。</summary>
public sealed partial class PendingItemVM : ObservableObject
{
    public PendingItemVM(
        ImageRecord record,
        string absolutePath,
        string originLabel,
        string originClass,
        string? candidateFileName = null,
        bool isHighConfidence = false)
    {
        Record = record;
        AbsolutePath = absolutePath;
        OriginLabel = originLabel;
        OriginClass = originClass;
        CandidateFileName = candidateFileName;
        IsHighConfidence = isHighConfidence;
    }

    /// <summary>左ペインの選択ハイライト(accent.bg)。</summary>
    [ObservableProperty]
    private bool _isSelected;

    public ImageRecord Record { get; }

    public string AbsolutePath { get; }

    public string FileName => Record.FileName;

    public string OriginLabel { get; }

    /// <summary>由来チップの視覚クラス: changed=琥珀/new=青/restored・reappeared=灰。</summary>
    public string OriginClass { get; }

    public string? CandidateFileName { get; }

    [ObservableProperty]
    private bool _isHighConfidence;

    public bool IsOriginAmber => OriginClass == "amber";

    public bool IsOriginBlue => OriginClass == "blue";

    public bool IsOriginGray => OriginClass == "gray";
}

/// <summary>
/// pending 裁定ダイアログ(ECO-129/139・REQ-101・仕様 §2.11.7・CAD= pending_review.md PD-2〜6)。
/// 手動裁定は 1 件ずつ確定し、ECO-139 の一意な高信頼サブセットだけ確認つきで原子一括再リンクする。
/// ✕クローズはいつでも可(破棄の概念なし)。
/// 遷移は PendingReviewService(T13/T14/T15)経由のみ・「修復で再リンク…」は既存修復フローへ委譲。
/// </summary>
public sealed partial class PendingReviewViewModel : ObservableObject
{
    private readonly PendingReviewService _review;
    private readonly IImageRepository _images;
    private readonly ITagRepository _tags;
    private readonly LocalizationService _localization;
    private readonly IWindowService _windows;
    private readonly SyncFolder _folder;
    private readonly Dictionary<string, int> _tagCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ImageRecord> _candidateRows = new(StringComparer.Ordinal);

    public PendingReviewViewModel(
        PendingReviewService review,
        IImageRepository images,
        ITagRepository tags,
        LocalizationService localization,
        IWindowService windows,
        SyncFolder folder)
    {
        _review = review;
        _images = images;
        _tags = tags;
        _localization = localization;
        _windows = windows;
        _folder = folder;
        Loc = new LocalizationProxy(localization);
    }

    public event EventHandler? RequestClose;

    public LocalizationProxy Loc { get; }

    /// <summary>1 件以上裁定した(呼び出し側の再読込トリガ)。</summary>
    public bool Adjudicated { get; private set; }

    public List<PendingItemVM> Items { get; } = [];

    public List<PendingItemVM> HighConfidenceItems { get; } = [];

    public List<PendingItemVM> IndividualItems { get; } = [];

    public int HighConfidenceCount => HighConfidenceItems.Count;

    public int IndividualCount => IndividualItems.Count;

    public bool HasHighConfidence => HighConfidenceCount > 0;

    public bool HasIndividualItems => IndividualCount > 0;

    public bool ShowIndividualGroupHeader => HasHighConfidence && HasIndividualItems;

    public string AutoCalloutLead => T(
        "pending.autoCalloutLead",
        ("count", HighConfidenceCount.ToString(CultureInfo.InvariantCulture)));

    public string AutoAdjudicateLabel => T(
        "pending.autoButton",
        ("count", HighConfidenceCount.ToString(CultureInfo.InvariantCulture)));

    public string HighConfidenceHeader => T(
        "pending.autoGroupHeader",
        ("count", HighConfidenceCount.ToString(CultureInfo.InvariantCulture)));

    public string IndividualHeader => T(
        "pending.individualGroupHeader",
        ("count", IndividualCount.ToString(CultureInfo.InvariantCulture)));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private PendingItemVM? _selected;

    public bool IsEmpty => Items.Count == 0;

    public bool HasSelection => Selected is not null;

    [ObservableProperty]
    private string _windowTitle = string.Empty;

    // ---- 右メイン(選択項目の詳細) ----

    [ObservableProperty]
    private string _pathLine = string.Empty;

    [ObservableProperty]
    private string _previewPath = string.Empty;

    [ObservableProperty]
    private string _whyLead = string.Empty;

    [ObservableProperty]
    private string _whyDesc = string.Empty;

    [ObservableProperty]
    private bool _whyIsCandidate; // 青(候補あり)/false=琥珀(内容変更系)

    [ObservableProperty]
    private string _sizeText = string.Empty;

    [ObservableProperty]
    private string _dateText = string.Empty;

    [ObservableProperty]
    private string _tagCountText = string.Empty;

    [ObservableProperty]
    private bool _hasTagRow;

    // CTA の出し分け(§2.11.7): 別画像= changed/reappeared/restored のみ・再リンク導線= candidate つき新規のみ
    [ObservableProperty]
    private bool _showTreatAsNew;

    [ObservableProperty]
    private bool _showRelink;

    [ObservableProperty]
    private string _acceptLabel = string.Empty;

    [ObservableProperty]
    private string? _statusMessage;

    public async Task LoadAsync()
    {
        Items.Clear();
        HighConfidenceItems.Clear();
        IndividualItems.Clear();
        _tagCounts.Clear();
        _candidateRows.Clear();
        var rows = await _images.GetPendingByFolderAsync(_folder.Id);
        var candidateIds = rows
            .Where(PendingReviewService.IsHighConfidence)
            .Select(record => record.CandidateLinkId!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        foreach (var candidate in await _images.GetByIdsAsync(candidateIds))
        {
            _candidateRows[candidate.Id] = candidate;
        }

        var uniquelyRelinkableIds = PendingReviewService.SelectUniquelyRelinkable(rows, _candidateRows)
            .Select(record => record.Id)
            .ToHashSet(StringComparer.Ordinal);
        // タグ件数(「受け入れても保持されます」の安心表示)= フォルダ単位 1 クエリ(R8 所見4: N+1 回避)
        foreach (var group in (await _tags.GetImageTagsByFolderAsync(_folder.Id))
                     .GroupBy(it => it.ImageId, StringComparer.Ordinal))
        {
            _tagCounts[group.Key] = group.Count();
        }

        foreach (var record in rows)
        {
            var abs = Path.Combine(_folder.Path, record.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            var (label, cls) = record.PendingOrigin switch
            {
                PendingOrigin.Changed => (T("pending.originChanged"), "amber"),
                PendingOrigin.New => (T("pending.originNew"), "blue"),
                PendingOrigin.Reappeared => (T("pending.originReappeared"), "gray"),
                PendingOrigin.Restored => (T("pending.originRestored"), "gray"),
                _ => (T("pending.originNew"), "blue"), // origin 不明(移行前データ等)は新規扱い
            };
            _candidateRows.TryGetValue(record.CandidateLinkId ?? string.Empty, out var candidate);
            var item = new PendingItemVM(
                record, abs, label, cls, candidate?.FileName,
                uniquelyRelinkableIds.Contains(record.Id));
            if (item.IsHighConfidence)
            {
                HighConfidenceItems.Add(item);
            }
            else
            {
                IndividualItems.Add(item);
            }
        }

        Items.AddRange(HighConfidenceItems);
        Items.AddRange(IndividualItems);
        Selected = Items.FirstOrDefault();
        OnPropertyChanged(nameof(Items));
        OnPropertyChanged(nameof(IsEmpty));
        NotifyGroupState();
        RefreshDetail();
    }

    partial void OnSelectedChanged(PendingItemVM? value)
    {
        foreach (var item in Items)
        {
            item.IsSelected = ReferenceEquals(item, value);
        }

        RefreshDetail();
    }

    private void RefreshDetail()
    {
        WindowTitle = Selected is null
            ? T("pending.title", ("name", _folder.Name))
            : T("pending.titleIndexed",
                ("name", _folder.Name),
                ("index", (Items.IndexOf(Selected) + 1).ToString(CultureInfo.InvariantCulture)),
                ("total", Items.Count.ToString(CultureInfo.InvariantCulture)));
        if (Selected is not { } item)
        {
            return;
        }

        var r = item.Record;
        PreviewPath = item.AbsolutePath;
        var dir = r.RelativePath.Contains('/')
            ? r.RelativePath[..(r.RelativePath.LastIndexOf('/') + 1)].Replace('/', '\\')
            : string.Empty;
        PathLine = dir.Length > 0 ? $"{r.FileName} — {dir}" : r.FileName;

        var isNew = r.PendingOrigin is PendingOrigin.New or null;
        var hasCandidate = r.CandidateLinkId is not null;
        WhyIsCandidate = isNew && hasCandidate;
        (WhyLead, WhyDesc) = r.PendingOrigin switch
        {
            PendingOrigin.Changed => (T("pending.whyChangedLead"), T("pending.whyChangedDesc")),
            PendingOrigin.Reappeared => (T("pending.whyReappearedLead"), T("pending.whyReappearedDesc")),
            PendingOrigin.Restored => (T("pending.whyRestoredLead"), T("pending.whyRestoredDesc")),
            _ => hasCandidate
                ? (T("pending.whyCandidateLead"), T("pending.whyCandidateDesc"))
                : (T("pending.whyNewLead"), T("pending.whyNewDesc")),
        };

        SizeText = FmtSize(r.FileSize);
        DateText = LocaleFormats.FormatTimestamp(r.ModifiedDate, _localization.CurrentLocale);
        var tagCount = _tagCounts.GetValueOrDefault(r.Id);
        HasTagRow = !isNew && tagCount > 0;
        TagCountText = HasTagRow
            ? T("pending.tagsKept", ("count", tagCount.ToString(CultureInfo.InvariantCulture)))
            : string.Empty;

        ShowTreatAsNew = !isNew; // changed/reappeared/restored のみ(§2.11.7)
        ShowRelink = WhyIsCandidate;
        AcceptLabel = isNew ? T("pending.acceptNew") : T("pending.accept");
    }

    [RelayCommand]
    private void Select(PendingItemVM item) => Selected = item;

    [RelayCommand]
    private async Task AcceptAsync()
    {
        if (Selected is not { } item)
        {
            return;
        }

        var result = await _review.AcceptAsync(item.Record.Id);
        Complete(item, result.IsSuccess, result.Message);
    }

    [RelayCommand]
    private async Task TreatAsNewAsync()
    {
        if (Selected is not { } item)
        {
            return;
        }

        var result = await _review.TreatAsNewAsync(item.Record.Id);
        Complete(item, result.IsSuccess, result.Message);
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (Selected is not { } item)
        {
            return;
        }

        var result = await _review.DeleteAsync(item.Record.Id);
        Complete(item, result.IsSuccess, result.Message);
    }

    [RelayCommand]
    private void Defer()
    {
        // 保留して次へ(遷移なし)。末尾なら先頭へ回る(残があれば必ず次を見せる)
        if (Selected is not { } item || Items.Count <= 1)
        {
            return;
        }

        var index = Items.IndexOf(item);
        Selected = Items[(index + 1) % Items.Count];
        RefreshDetail();
    }

    [RelayCommand]
    private async Task OpenRepairAsync()
    {
        // 「修復で再リンク…」= 既存修復フローへ委譲(タグ引き継ぎ= T4 の既存契約)。閉じ後に再読込
        await _windows.ShowRepairAsync(_folder.Id);
        Adjudicated = true; // relink は pending を消費し得る=呼び出し側の再読込を促す
        await LoadAsync();
        if (Items.Count == 0)
        {
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    [RelayCommand]
    private async Task AutoAdjudicateAsync()
    {
        var targets = HighConfidenceItems.ToList();
        if (targets.Count == 0)
        {
            return;
        }

        var count = targets.Count.ToString(CultureInfo.InvariantCulture);
        var confirmationItems = targets.Select(item => new ConfirmationListItem(
            item.FileName,
            T(
                "pending.autoMatch",
                ("name", item.CandidateFileName ?? item.Record.CandidateLinkId ?? string.Empty)),
            item.AbsolutePath)).ToList();
        var confirmed = await _windows.ConfirmListAsync(
            T("pending.autoConfirmTitle"),
            T("pending.autoConfirmLead", ("count", count)),
            T("pending.autoConfirmSupport", ("count", count)),
            T("pending.autoConfirmAction"),
            confirmationItems);
        if (!confirmed)
        {
            return;
        }

        var result = await _review.RelinkHighConfidenceAsync(targets.Select(item => item.Record));
        if (!result.IsSuccess || result.Value != targets.Count)
        {
            StatusMessage = T("pending.autoFailedStale");
            return;
        }

        StatusMessage = null;
        Adjudicated = true;
        foreach (var item in targets)
        {
            Items.Remove(item);
            HighConfidenceItems.Remove(item);
        }

        OnPropertyChanged(nameof(Items));
        OnPropertyChanged(nameof(IsEmpty));
        NotifyGroupState();
        Selected = Items.FirstOrDefault();
        RefreshDetail();
    }

    [RelayCommand]
    private void CloseWindow() => RequestClose?.Invoke(this, EventArgs.Empty);

    private void Complete(PendingItemVM item, bool success, string? message)
    {
        if (!success)
        {
            StatusMessage = message;
            return;
        }

        StatusMessage = null;
        Adjudicated = true;
        var index = Items.IndexOf(item);
        Items.Remove(item);
        ReclassifyGroups();
        OnPropertyChanged(nameof(IsEmpty));
        Selected = Items.Count > 0 ? Items[Math.Min(index, Items.Count - 1)] : null;
        RefreshDetail();
    }

    private void ReclassifyGroups()
    {
        var remaining = Items.ToList();
        var uniquelyRelinkableIds = PendingReviewService
            .SelectUniquelyRelinkable(remaining.Select(item => item.Record), _candidateRows)
            .Select(record => record.Id)
            .ToHashSet(StringComparer.Ordinal);
        HighConfidenceItems.Clear();
        IndividualItems.Clear();
        foreach (var item in remaining)
        {
            item.IsHighConfidence = uniquelyRelinkableIds.Contains(item.Record.Id);
            (item.IsHighConfidence ? HighConfidenceItems : IndividualItems).Add(item);
        }

        Items.Clear();
        Items.AddRange(HighConfidenceItems);
        Items.AddRange(IndividualItems);
        OnPropertyChanged(nameof(Items));
        NotifyGroupState();
    }

    private void NotifyGroupState()
    {
        OnPropertyChanged(nameof(HighConfidenceItems));
        OnPropertyChanged(nameof(IndividualItems));
        OnPropertyChanged(nameof(HighConfidenceCount));
        OnPropertyChanged(nameof(IndividualCount));
        OnPropertyChanged(nameof(HasHighConfidence));
        OnPropertyChanged(nameof(HasIndividualItems));
        OnPropertyChanged(nameof(ShowIndividualGroupHeader));
        OnPropertyChanged(nameof(AutoCalloutLead));
        OnPropertyChanged(nameof(AutoAdjudicateLabel));
        OnPropertyChanged(nameof(HighConfidenceHeader));
        OnPropertyChanged(nameof(IndividualHeader));
    }

    private static string FmtSize(long bytes)
    {
        double mb = bytes / 1024.0 / 1024.0;
        return mb >= 1
            ? mb.ToString("0.0", CultureInfo.InvariantCulture) + " MB"
            : Math.Round(bytes / 1024.0).ToString(CultureInfo.InvariantCulture) + " KB";
    }

    private string T(string key, params (string Key, string Value)[] args)
        => args.Length == 0
            ? _localization.T(key)
            : _localization.T(key, args.ToDictionary(a => a.Key, a => a.Value));
}
