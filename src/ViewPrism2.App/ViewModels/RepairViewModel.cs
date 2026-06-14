using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ViewPrism2.App.Services;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;
using ViewPrism2.Core.Services;
using ViewPrism2.Core.Services.Repair;
using ViewPrism2.Infrastructure.Scanning;

namespace ViewPrism2.App.ViewModels;

/// <summary>criteria 検索結果の表示行(REQ-072)。</summary>
public sealed record CriteriaResultViewModel(ImageRecord Record, string SizeText, string StatusText)
{
    public string RelativePath => Record.RelativePath;
}

/// <summary>
/// 修復ライフサイクル UI の ViewModel(M-UI-REPAIR-027 / REQ-072、仕様 §2.11.5)。
/// (a) criteria 検索フォーム(hash/名前/拡張子/mtime 範囲/サイズ範囲・AND・対象コレクション)→結果一覧、
/// (b) relink フロー(missing への候補提示・選択・確定)。
/// 各操作は M-CRITERIA-024/M-RELINK-025 の API のみ経由(UI で状態遷移・タグ操作を再実装しない)。
/// 空条件は検索ボタン非活性。ロジックは ViewModel で unit 検査可能に分離する(コードビハインド判定禁止)。
/// </summary>
public sealed partial class RepairViewModel : ObservableObject
{
    private static readonly IReadOnlySet<ImageStatus> NormalOnly =
        new HashSet<ImageStatus> { ImageStatus.Normal };

    private readonly string _collectionId;
    private readonly IImageRepository _images;
    private readonly CriteriaSearchService _criteriaSearch;
    private readonly RelinkService _relink;
    private readonly LocalizationService _localization;
    private readonly IWindowService? _windows;

    public RepairViewModel(
        string collectionId,
        IImageRepository images,
        CriteriaSearchService criteriaSearch,
        RelinkService relink,
        LocalizationService localization,
        IWindowService? windows = null)
    {
        _collectionId = collectionId;
        _images = images;
        _criteriaSearch = criteriaSearch;
        _relink = relink;
        _localization = localization;
        _windows = windows;
        Loc = new LocalizationProxy(localization);
        localization.CultureChanged += (_, _) =>
        {
            Loc = new LocalizationProxy(localization);
            OnPropertyChanged(nameof(Loc));
        };
    }

    public LocalizationProxy Loc { get; private set; }

    // ---- criteria 検索フォーム入力 ----
    [ObservableProperty]
    private string? _hashInput;

    [ObservableProperty]
    private string? _nameContainsInput;

    [ObservableProperty]
    private string? _extensionInput;

    [ObservableProperty]
    private string? _mtimeFromInput;

    [ObservableProperty]
    private string? _mtimeToInput;

    [ObservableProperty]
    private string? _sizeMinInput;

    [ObservableProperty]
    private string? _sizeMaxInput;

    public ObservableCollection<CriteriaResultViewModel> SearchResults { get; } = [];

    // ---- relink フロー ----
    public ObservableCollection<MissingImageViewModel> MissingImages { get; } = [];

    public ObservableCollection<RelinkCandidateViewModel> Candidates { get; } = [];

    [ObservableProperty]
    private MissingImageViewModel? _selectedMissing;

    [ObservableProperty]
    private RelinkCandidateViewModel? _selectedCandidate;

    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>現在のフォーム入力から構築した検索条件。</summary>
    public SearchCriteria CurrentCriteria => BuildCriteria();

    /// <summary>検索可能か(1 つ以上の条件が指定されている)。空条件は非実行(§2.11.1)。</summary>
    public bool CanSearch => HasAnyCriteria(BuildCriteria());

    public bool HasNoResults => SearchResults.Count == 0;

    public bool HasNoMissing => MissingImages.Count == 0;

    public bool HasNoCandidates => Candidates.Count == 0 && SelectedMissing is not null;

    public async Task LoadAsync()
    {
        MissingImages.Clear();
        Candidates.Clear();
        SelectedMissing = null;
        SelectedCandidate = null;

        var records = await _images.GetByFolderAsync(_collectionId);
        foreach (var record in records.Where(r => r.Status == ImageStatus.Missing)
                     .OrderBy(r => r.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            MissingImages.Add(new MissingImageViewModel(record));
        }

        OnPropertyChanged(nameof(HasNoMissing));
        OnPropertyChanged(nameof(HasNoCandidates));
    }

    /// <summary>criteria 検索(単体検索=status {Normal}・§2.11.1)。空条件は実行しない。</summary>
    [RelayCommand]
    public async Task SearchAsync()
    {
        SearchResults.Clear();
        var criteria = BuildCriteria();
        if (!HasAnyCriteria(criteria))
        {
            OnPropertyChanged(nameof(HasNoResults));
            return;
        }

        var matched = await _criteriaSearch.SearchAsync(_collectionId, criteria, NormalOnly, CancellationToken.None);
        foreach (var record in matched)
        {
            SearchResults.Add(new CriteriaResultViewModel(
                record,
                ByteSizeFormatter.Format(record.FileSize),
                record.Status.ToString()));
        }

        OnPropertyChanged(nameof(HasNoResults));
    }

    /// <summary>relink 確定(RelinkService.CommitRelinkAsync のみ経由)。タグ付き候補は拒否され案内文言を出す。</summary>
    [RelayCommand]
    public async Task CommitRelinkAsync()
    {
        if (SelectedMissing is not { } missing || SelectedCandidate is not { } candidate)
        {
            return;
        }

        if (_windows is not null && !await _windows.ConfirmAsync(
                _localization.T("repair.relink.title"), _localization.T("repair.relink.confirmMessage")))
        {
            return;
        }

        var result = await _relink.CommitRelinkAsync(missing.Record.Id, candidate.Candidate.ImageId);
        if (result.IsSuccess)
        {
            StatusMessage = _localization.T("repair.relink.success");
            await LoadAsync();
        }
        else
        {
            // タグ付き候補の拒否はマージ案内文言(INV-015)
            StatusMessage = _localization.T("repair.relink.failed") + ": "
                + ErrorMessages.Resolve(_localization, result.Error);
        }
    }

    partial void OnSelectedMissingChanged(MissingImageViewModel? value) => _ = LoadCandidatesAsync(value);

    /// <summary>選択中の missing に対する候補を再読込する(UI バインド・unit 検査の双方から呼べる awaitable 経路)。</summary>
    public Task RefreshCandidatesAsync() => LoadCandidatesAsync(SelectedMissing);

    partial void OnHashInputChanged(string? value) => OnPropertyChanged(nameof(CanSearch));

    partial void OnNameContainsInputChanged(string? value) => OnPropertyChanged(nameof(CanSearch));

    partial void OnExtensionInputChanged(string? value) => OnPropertyChanged(nameof(CanSearch));

    partial void OnMtimeFromInputChanged(string? value) => OnPropertyChanged(nameof(CanSearch));

    partial void OnMtimeToInputChanged(string? value) => OnPropertyChanged(nameof(CanSearch));

    partial void OnSizeMinInputChanged(string? value) => OnPropertyChanged(nameof(CanSearch));

    partial void OnSizeMaxInputChanged(string? value) => OnPropertyChanged(nameof(CanSearch));

    private async Task LoadCandidatesAsync(MissingImageViewModel? missing)
    {
        Candidates.Clear();
        SelectedCandidate = null;
        if (missing is not null)
        {
            // 候補=exact-hash pending ∪ criteria 結果(タグ付き除外・安定順は RelinkService 側で実施)。
            // 現在のフォーム条件を criteria として渡す(空条件なら null=exact-hash pending のみ)
            var criteria = BuildCriteria();
            var candidates = await _relink.GetCandidatesAsync(
                missing.Record.Id, HasAnyCriteria(criteria) ? criteria : null);
            foreach (var candidate in candidates)
            {
                Candidates.Add(new RelinkCandidateViewModel(
                    candidate,
                    ByteSizeFormatter.Format(candidate.FileSize),
                    LocaleFormats.FormatTimestamp(candidate.ModifiedDate, _localization.CurrentLocale)));
            }
        }

        OnPropertyChanged(nameof(HasNoCandidates));
    }

    private SearchCriteria BuildCriteria() => new()
    {
        Hash = Normalize(HashInput),
        NameContains = Normalize(NameContainsInput),
        Extension = Normalize(ExtensionInput),
        MtimeFrom = Normalize(MtimeFromInput),
        MtimeTo = Normalize(MtimeToInput),
        SizeMin = ParseLong(SizeMinInput),
        SizeMax = ParseLong(SizeMaxInput),
    };

    private static bool HasAnyCriteria(SearchCriteria c) =>
        c.Hash is not null || c.NameContains is not null || c.Extension is not null ||
        c.MtimeFrom is not null || c.MtimeTo is not null || c.SizeMin is not null || c.SizeMax is not null;

    /// <summary>空白のみ・空文字は未指定(null)へ正規化する。</summary>
    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static long? ParseLong(string? value)
        => long.TryParse(Normalize(value), System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
}
