using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ViewPrism2.App.Services;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;
using ViewPrism2.Core.Services;
using ViewPrism2.Core.Services.Repair;

namespace ViewPrism2.App.ViewModels;

/// <summary>統合裁定一覧の 1 行。由来キーを権威値として保持し、表示時に解決する。</summary>
public sealed partial class IntegrityReviewItemViewModel : ObservableObject
{
    private readonly LocalizationService _localization;

    public IntegrityReviewItemViewModel(
        IntegrityReviewEvent reviewEvent,
        string absolutePath,
        string originKey,
        LocalizationService localization,
        string originClass)
    {
        Event = reviewEvent;
        AbsolutePath = absolutePath;
        OriginKey = originKey;
        _localization = localization;
        OriginClass = originClass;
    }

    public IntegrityReviewEvent Event { get; }

    public ImageRecord Record => Event.Primary;

    public string FileName => Record.FileName;

    public string AbsolutePath { get; }

    public string OriginKey { get; }

    public string OriginLabel => _localization.T(OriginKey);

    public string OriginClass { get; }

    public bool IsOriginAmber => OriginClass == "amber";

    public bool IsOriginBlue => OriginClass == "blue";

    public bool IsOriginGray => OriginClass == "gray";

    public bool IsOriginRed => OriginClass == "red";

    public bool IsAutomatic => Event.Group == IntegrityReviewGroup.Automatic;

    public bool IsMissing => Event.Group == IntegrityReviewGroup.Missing;

    [ObservableProperty]
    private bool _isSelected;

    internal void NotifyLocalizedProperties() => OnPropertyChanged(nameof(OriginLabel));
}

/// <summary>
/// ECO-140/M-UI-INTEGRITY-055: 「要確認の画像」統合裁定面。
/// UI は分類・hash・relink 判定を再実装せず Core/E-RELINK-007 の結果だけを描画する。
/// </summary>
public sealed partial class IntegrityReviewViewModel : ObservableObject
{
    private readonly IntegrityReviewService _integrity;
    private readonly PendingReviewService _pending;
    private readonly IImageRepository _images;
    private readonly ITagRepository _tags;
    private readonly IRelinkService _relink;
    private readonly TrashService _trash;
    private readonly LocalizationService _localization;
    private readonly IWindowService _windows;
    private readonly SyncFolder _folder;
    private readonly Dictionary<string, int> _tagCounts = new(StringComparer.Ordinal);
    private CancellationTokenSource? _loadCts;
    private int _candidateSearchVersion;

    public IntegrityReviewViewModel(
        IntegrityReviewService integrity,
        PendingReviewService pending,
        IImageRepository images,
        ITagRepository tags,
        IRelinkService relink,
        TrashService trash,
        LocalizationService localization,
        IWindowService windows,
        SyncFolder folder)
    {
        _integrity = integrity;
        _pending = pending;
        _images = images;
        _tags = tags;
        _relink = relink;
        _trash = trash;
        _localization = localization;
        _windows = windows;
        _folder = folder;
        Loc = new LocalizationProxy(localization);
        localization.CultureChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Loc));
            foreach (var item in Items)
            {
                item.NotifyLocalizedProperties();
            }

            foreach (var candidate in Candidates)
            {
                candidate.NotifyLocalizedProperties();
            }

            NotifyLocalizedProperties();
            RefreshDetail();
        };
    }

    public event EventHandler? RequestClose;

    public LocalizationProxy Loc { get; }

    public bool Adjudicated { get; private set; }

    public bool HasLoaded { get; private set; }

    public ObservableCollection<IntegrityReviewItemViewModel> Items { get; } = [];

    public ObservableCollection<IntegrityReviewItemViewModel> AutomaticItems { get; } = [];

    public ObservableCollection<IntegrityReviewItemViewModel> IndividualItems { get; } = [];

    public ObservableCollection<IntegrityReviewItemViewModel> MissingItems { get; } = [];

    public ObservableCollection<RelinkCandidateViewModel> Candidates { get; } = [];

    public int AutomaticCount => AutomaticItems.Count;

    public int IndividualCount => IndividualItems.Count;

    public int MissingCount => MissingItems.Count;

    public int TotalCount => Items.Count;

    public bool HasAutomaticItems => AutomaticCount > 0;

    public bool HasAutomatic => !IsHashChecking && AutomaticCount > 0;

    public bool HasIndividual => IndividualCount > 0;

    public bool HasMissing => MissingCount > 0;

    public bool IsEmpty => !IsHashChecking && Items.Count == 0;

    public double WindowWidth => IsEmpty ? 480 : 800;

    public bool HasSelection => Selected is not null;

    public string AutomaticHeader => T("integrity.groupAutomatic", ("count", Count(AutomaticCount)));

    public string IndividualHeader => T("integrity.groupIndividual", ("count", Count(IndividualCount)));

    public string MissingHeader => T("integrity.groupMissing", ("count", Count(MissingCount)));

    public string AutoCalloutLead => T("integrity.autoLead", ("count", Count(AutomaticCount)));

    public string AutoButtonLabel => T("integrity.autoButton", ("count", Count(AutomaticCount)));

    public string HashProgressText => T(
        "integrity.hashProgress",
        ("completed", Count(HashCompleted)),
        ("total", Count(HashTotal)));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAutomatic))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(WindowWidth))]
    private bool _isHashChecking;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HashProgressText))]
    private int _hashCompleted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HashProgressText))]
    private int _hashTotal;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private IntegrityReviewItemViewModel? _selected;

    [ObservableProperty]
    private RelinkCandidateViewModel? _selectedCandidate;

    [ObservableProperty]
    private string _windowTitle = string.Empty;

    [ObservableProperty]
    private string _previewPath = string.Empty;

    [ObservableProperty]
    private string _pathLine = string.Empty;

    [ObservableProperty]
    private string _whyLead = string.Empty;

    [ObservableProperty]
    private string _whyDescription = string.Empty;

    [ObservableProperty]
    private string _sizeText = string.Empty;

    [ObservableProperty]
    private string _dateText = string.Empty;

    [ObservableProperty]
    private string _tagCountText = string.Empty;

    [ObservableProperty]
    private bool _showTagRow;

    [ObservableProperty]
    private bool _isSelectedMoved;

    [ObservableProperty]
    private bool _isSelectedMissing;

    [ObservableProperty]
    private bool _whyIsBlue;

    [ObservableProperty]
    private bool _whyIsAmber;

    [ObservableProperty]
    private bool _showTreatAsNew;

    [ObservableProperty]
    private bool _showAccept;

    [ObservableProperty]
    private string _acceptLabel = string.Empty;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string? _nameContainsInput;

    [ObservableProperty]
    private string? _extensionInput;

    [ObservableProperty]
    private string? _mtimeFromInput;

    [ObservableProperty]
    private string? _sizeToleranceInput;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoCandidates))]
    private bool _hasSearchedCandidates;

    public bool CanCommitCandidate => IsSelectedMissing && SelectedCandidate is not null;

    public bool CanSearchCandidates =>
        IsSelectedMissing
        && (Normalize(NameContainsInput) is not null
            || Normalize(ExtensionInput) is not null
            || Normalize(MtimeFromInput) is not null
            || ParseLong(SizeToleranceInput) is not null);

    public bool ShowNoCandidates => HasSearchedCandidates && Candidates.Count == 0;

    public async Task LoadAsync(bool reuseVerifiedHashes = false)
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;
        IsHashChecking = true;
        HasLoaded = false;
        HashCompleted = 0;
        HashTotal = 0;
        StatusMessage = null;

        try
        {
            _tagCounts.Clear();
            foreach (var group in (await _tags.GetIntegrityReviewImageTagsByFolderAsync(_folder.Id, ct))
                         .GroupBy(it => it.ImageId, StringComparer.Ordinal))
            {
                _tagCounts[group.Key] = group.Count();
            }

            var progress = new Progress<IntegrityReviewHashProgress>(value =>
            {
                HashCompleted = value.Completed;
                HashTotal = value.Total;
            });
            var finalApplied = false;
            var interimSnapshots = new Progress<IntegrityReviewSnapshot>(value =>
            {
                if (!finalApplied)
                {
                    ApplySnapshot(value);
                }
            });
            var snapshot = await _integrity.LoadAsync(
                _folder,
                ct,
                progress,
                interimSnapshots,
                reuseVerifiedHashes);
            finalApplied = true;
            ApplySnapshot(snapshot);
            HasLoaded = true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                IsHashChecking = false;
                NotifyCounts();
                RefreshDetail();
            }
        }
    }

    public void CancelLoading() => _loadCts?.Cancel();

    private void ApplySnapshot(IntegrityReviewSnapshot snapshot)
    {
        Items.Clear();
        AutomaticItems.Clear();
        IndividualItems.Clear();
        MissingItems.Clear();
        Candidates.Clear();
        SelectedCandidate = null;

        foreach (var reviewEvent in snapshot.Events)
        {
            var (key, cls) = OriginVisual(reviewEvent);
            var item = new IntegrityReviewItemViewModel(
                reviewEvent,
                ResolveAbsolute(reviewEvent.Primary.RelativePath),
                key,
                _localization,
                cls);
            Items.Add(item);
            switch (reviewEvent.Group)
            {
                case IntegrityReviewGroup.Automatic:
                    AutomaticItems.Add(item);
                    break;
                case IntegrityReviewGroup.Individual:
                    IndividualItems.Add(item);
                    break;
                case IntegrityReviewGroup.Missing:
                    MissingItems.Add(item);
                    break;
            }
        }

        Selected = IsHashChecking
            ? IndividualItems.FirstOrDefault() ?? MissingItems.FirstOrDefault()
            : Items.FirstOrDefault();
        NotifyCounts();
    }

    private static (string Key, string Class) OriginVisual(IntegrityReviewEvent reviewEvent)
        => reviewEvent.Kind switch
        {
            IntegrityReviewKind.Moved => ("integrity.originMoved", "blue"),
            IntegrityReviewKind.Changed => ("integrity.originChanged", "amber"),
            IntegrityReviewKind.New => ("integrity.originNew", "blue"),
            IntegrityReviewKind.Restored => ("integrity.originRestored", "gray"),
            IntegrityReviewKind.Reappeared => ("integrity.originReappeared", "gray"),
            IntegrityReviewKind.Missing => ("integrity.originMissing", "red"),
            _ => ("integrity.originNew", "blue"),
        };

    partial void OnSelectedChanged(IntegrityReviewItemViewModel? value)
    {
        foreach (var item in Items)
        {
            item.IsSelected = ReferenceEquals(item, value);
        }

        Candidates.Clear();
        SelectedCandidate = null;
        HasSearchedCandidates = false;
        Interlocked.Increment(ref _candidateSearchVersion);
        PrefillMissingCriteria(value);
        RefreshDetail();
        OnPropertyChanged(nameof(CanSearchCandidates));
        SearchCandidatesCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedCandidateChanged(RelinkCandidateViewModel? value)
        => OnPropertyChanged(nameof(CanCommitCandidate));

    partial void OnNameContainsInputChanged(string? value) => CandidateCriteriaChanged();

    partial void OnExtensionInputChanged(string? value) => CandidateCriteriaChanged();

    partial void OnMtimeFromInputChanged(string? value) => CandidateCriteriaChanged();

    partial void OnSizeToleranceInputChanged(string? value) => CandidateCriteriaChanged();

    private void CandidateCriteriaChanged()
    {
        HasSearchedCandidates = false;
        OnPropertyChanged(nameof(CanSearchCandidates));
        SearchCandidatesCommand.NotifyCanExecuteChanged();
    }

    private void RefreshDetail()
    {
        WindowTitle = Selected is null
            ? T("integrity.title", ("name", _folder.Name))
            : T(
                "integrity.titleIndexed",
                ("name", _folder.Name),
                ("index", Count(Items.IndexOf(Selected) + 1)),
                ("total", Count(TotalCount)));
        if (Selected is not { } item)
        {
            PreviewPath = string.Empty;
            PathLine = string.Empty;
            WhyLead = string.Empty;
            WhyDescription = string.Empty;
            ShowTagRow = false;
            IsSelectedMoved = false;
            IsSelectedMissing = false;
            WhyIsBlue = false;
            WhyIsAmber = false;
            ShowTreatAsNew = false;
            ShowAccept = false;
            AcceptLabel = string.Empty;
            OnPropertyChanged(nameof(CanCommitCandidate));
            return;
        }

        var reviewEvent = item.Event;
        var record = reviewEvent.Primary;
        PreviewPath = item.AbsolutePath;
        PathLine = record.RelativePath.Replace('/', Path.DirectorySeparatorChar);
        SizeText = ByteSizeFormatter.Format(record.FileSize);
        DateText = LocaleFormats.FormatTimestamp(record.ModifiedDate, _localization.CurrentLocale);
        var tagCount = _tagCounts.GetValueOrDefault(record.Id);
        ShowTagRow = tagCount > 0;
        TagCountText = T("integrity.tagsKept", ("count", Count(tagCount)));

        (WhyLead, WhyDescription) = reviewEvent.Kind switch
        {
            IntegrityReviewKind.Moved => (
                T("integrity.whyMovedLead"),
                T(
                    "integrity.whyMovedDesc",
                    ("name", reviewEvent.Counterpart?.FileName ?? string.Empty),
                    ("path", reviewEvent.Counterpart?.RelativePath ?? string.Empty))),
            IntegrityReviewKind.Changed => (
                T("integrity.whyChangedLead"), T("integrity.whyChangedDesc")),
            IntegrityReviewKind.New => (
                T("integrity.whyNewLead"), T("integrity.whyNewDesc")),
            IntegrityReviewKind.Restored => (
                T("integrity.whyRestoredLead"), T("integrity.whyRestoredDesc")),
            IntegrityReviewKind.Reappeared when reviewEvent.HashOutcome == IntegrityReviewHashOutcome.Mismatch => (
                T("integrity.whyReappearedMismatchLead"), T("integrity.whyReappearedMismatchDesc")),
            IntegrityReviewKind.Reappeared when reviewEvent.HashOutcome == IntegrityReviewHashOutcome.Failed => (
                T("integrity.whyReappearedFailedLead"), T("integrity.whyReappearedFailedDesc")),
            IntegrityReviewKind.Reappeared => (
                T("integrity.whyReappearedLead"), T("integrity.whyReappearedDesc")),
            IntegrityReviewKind.Missing => (
                T("integrity.whyMissingLead"), T("integrity.whyMissingDesc")),
            _ => (string.Empty, string.Empty),
        };

        IsSelectedMoved = reviewEvent.Kind == IntegrityReviewKind.Moved;
        IsSelectedMissing = reviewEvent.Kind == IntegrityReviewKind.Missing;
        WhyIsBlue = reviewEvent.Kind is IntegrityReviewKind.Moved or IntegrityReviewKind.New
            || (reviewEvent.Kind == IntegrityReviewKind.Reappeared
                && reviewEvent.HashOutcome == IntegrityReviewHashOutcome.Match);
        WhyIsAmber = reviewEvent.Kind is IntegrityReviewKind.Changed or IntegrityReviewKind.Missing
            || (reviewEvent.Kind == IntegrityReviewKind.Reappeared
                && reviewEvent.HashOutcome is IntegrityReviewHashOutcome.Mismatch
                    or IntegrityReviewHashOutcome.Failed);
        var isNew = reviewEvent.Kind == IntegrityReviewKind.New;
        ShowTreatAsNew = reviewEvent.Kind is IntegrityReviewKind.Changed
            or IntegrityReviewKind.Reappeared
            or IntegrityReviewKind.Restored;
        ShowAccept = !IsSelectedMissing && !IsSelectedMoved;
        AcceptLabel = isNew ? T("integrity.acceptNew") : T("integrity.accept");
        OnPropertyChanged(nameof(CanCommitCandidate));
    }

    private void PrefillMissingCriteria(IntegrityReviewItemViewModel? item)
    {
        if (item?.Event.Kind != IntegrityReviewKind.Missing)
        {
            NameContainsInput = null;
            ExtensionInput = null;
            MtimeFromInput = null;
            SizeToleranceInput = null;
            return;
        }

        var record = item.Record;
        NameContainsInput = Path.GetFileNameWithoutExtension(record.FileName);
        ExtensionInput = Path.GetExtension(record.FileName);
        MtimeFromInput = record.ModifiedDate;
        SizeToleranceInput = record.FileSize.ToString(CultureInfo.InvariantCulture);
    }

    [RelayCommand]
    private void Select(IntegrityReviewItemViewModel item) => Selected = item;

    [RelayCommand]
    private async Task AutoAdjudicateAsync()
    {
        var targets = AutomaticItems.Select(item => item.Event).ToList();
        if (IsHashChecking || targets.Count == 0)
        {
            return;
        }

        var confirmationItems = targets.Select(reviewEvent =>
        {
            var operation = reviewEvent.Kind == IntegrityReviewKind.Moved
                ? T(
                    "integrity.confirmRelink",
                    ("name", reviewEvent.Counterpart?.FileName ?? string.Empty))
                : T("integrity.confirmAccept");
            return new ConfirmationListItem(
                reviewEvent.Primary.FileName,
                operation,
                ResolveAbsolute(reviewEvent.Primary.RelativePath));
        }).ToList();
        var count = Count(targets.Count);
        if (!await _windows.ConfirmListAsync(
                T("integrity.confirmTitle"),
                T("integrity.confirmLead", ("count", count)),
                T("integrity.confirmSupport"),
                T("integrity.confirmApply"),
                confirmationItems))
        {
            return;
        }

        var result = await _integrity.ApplyAutomaticAsync(targets);
        if (!result.IsSuccess || result.Value != targets.Count)
        {
            StatusMessage = T("integrity.stale");
            return;
        }

        Adjudicated = true;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task AcceptAsync()
    {
        if (Selected is not { } item)
        {
            return;
        }

        var wasHashChecking = IsHashChecking;
        var result = await _pending.AcceptAsync(item.Record.Id);
        await CompleteAsync(
            result.IsSuccess,
            wasHashChecking);
    }

    [RelayCommand]
    private async Task TreatAsNewAsync()
    {
        if (Selected is not { } item)
        {
            return;
        }

        var wasHashChecking = IsHashChecking;
        var result = await _pending.TreatAsNewAsync(item.Record.Id);
        await CompleteAsync(
            result.IsSuccess,
            wasHashChecking);
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (Selected is not { } item)
        {
            return;
        }

        var wasHashChecking = IsHashChecking;
        var result = await _pending.DeleteAsync(item.Record.Id);
        await CompleteAsync(
            result.IsSuccess,
            wasHashChecking);
    }

    [RelayCommand]
    private async Task RelinkMovedAsync()
    {
        if (Selected?.Event is not
            {
                Kind: IntegrityReviewKind.Moved,
                Counterpart: { } missing,
            } reviewEvent)
        {
            return;
        }

        var item = Selected;
        var wasHashChecking = IsHashChecking;
        var result = await _relink.CommitRelinkAsync(missing.Id, reviewEvent.Primary.Id);
        await CompleteAsync(result.IsSuccess, wasHashChecking);
    }

    [RelayCommand(CanExecute = nameof(CanSearchCandidates))]
    private async Task SearchCandidatesAsync()
    {
        Candidates.Clear();
        SelectedCandidate = null;
        HasSearchedCandidates = false;
        if (Selected?.Event.Kind != IntegrityReviewKind.Missing || !CanSearchCandidates)
        {
            return;
        }

        var selectedId = Selected.Record.Id;
        var version = Interlocked.Increment(ref _candidateSearchVersion);
        var size = ParseLong(SizeToleranceInput);
        long? tolerance = size is null ? null : Math.Max(1, size.Value / 10);
        var criteria = new SearchCriteria
        {
            NameContains = Normalize(NameContainsInput),
            Extension = Normalize(ExtensionInput),
            MtimeFrom = Normalize(MtimeFromInput),
            SizeMin = size is null ? null : Math.Max(0, size.Value - tolerance!.Value),
            SizeMax = size is null ? null : size.Value + tolerance!.Value,
        };
        var candidates = await _relink.GetCandidatesAsync(selectedId, criteria);
        if (version != Volatile.Read(ref _candidateSearchVersion)
            || !string.Equals(Selected?.Record.Id, selectedId, StringComparison.Ordinal))
        {
            return;
        }

        foreach (var candidate in candidates)
        {
            Candidates.Add(new RelinkCandidateViewModel(
                candidate,
                _localization,
                ResolveAbsolute(candidate.RelativePath)));
        }

        HasSearchedCandidates = true;
        OnPropertyChanged(nameof(ShowNoCandidates));
        OnPropertyChanged(nameof(CanCommitCandidate));
    }

    [RelayCommand]
    private async Task CommitCandidateAsync()
    {
        if (Selected?.Event.Kind != IntegrityReviewKind.Missing
            || SelectedCandidate is not { } candidate)
        {
            return;
        }

        var item = Selected;
        var wasHashChecking = IsHashChecking;
        var result = await _relink.CommitRelinkAsync(item.Record.Id, candidate.Candidate.ImageId);
        await CompleteAsync(result.IsSuccess, wasHashChecking);
    }

    [RelayCommand]
    private async Task ExcludeMissingAsync()
    {
        if (Selected?.Event.Kind != IntegrityReviewKind.Missing)
        {
            return;
        }

        var item = Selected;
        var wasHashChecking = IsHashChecking;
        var result = await _trash.ExcludeAsync(item.Record.Id);
        await CompleteAsync(result.IsSuccess, wasHashChecking);
    }

    [RelayCommand]
    private void Defer()
    {
        if (Selected is not { } selected || Items.Count <= 1)
        {
            return;
        }

        Selected = Items[(Items.IndexOf(selected) + 1) % Items.Count];
    }

    [RelayCommand]
    private void CloseWindow() => RequestClose?.Invoke(this, EventArgs.Empty);

    private async Task CompleteAsync(
        bool success,
        bool wasHashChecking)
    {
        if (!success)
        {
            StatusMessage = T("integrity.stale");
            return;
        }

        Adjudicated = true;
        StatusMessage = null;
        // DB 母集合と relink 一意性は毎回再選別する。一方、確認済みで裁定基準・記録値・
        // metadata が不変な reappeared は hash outcome を再利用し、逐次裁定を O(N²)
        // full-file I/O にしない。確認中 snapshot との競合時だけ全件を再確認する。
        await LoadAsync(reuseVerifiedHashes: !wasHashChecking);
    }

    private void NotifyCounts()
    {
        OnPropertyChanged(nameof(AutomaticCount));
        OnPropertyChanged(nameof(IndividualCount));
        OnPropertyChanged(nameof(MissingCount));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(HasAutomaticItems));
        OnPropertyChanged(nameof(HasAutomatic));
        OnPropertyChanged(nameof(HasIndividual));
        OnPropertyChanged(nameof(HasMissing));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(WindowWidth));
        OnPropertyChanged(nameof(AutomaticHeader));
        OnPropertyChanged(nameof(IndividualHeader));
        OnPropertyChanged(nameof(MissingHeader));
        OnPropertyChanged(nameof(AutoCalloutLead));
        OnPropertyChanged(nameof(AutoButtonLabel));
    }

    private void NotifyLocalizedProperties()
    {
        NotifyCounts();
        OnPropertyChanged(nameof(HashProgressText));
    }

    private string ResolveAbsolute(string relativePath)
        => Path.Combine(_folder.Path, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

    private string T(string key, params (string Key, string Value)[] args)
        => args.Length == 0
            ? _localization.T(key)
            : _localization.T(key, args.ToDictionary(a => a.Key, a => a.Value));

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static long? ParseLong(string? value)
        => long.TryParse(
            Normalize(value),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : null;
}
