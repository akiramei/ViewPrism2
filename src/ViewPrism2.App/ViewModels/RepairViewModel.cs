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

    [ObservableProperty]
    private int _autoRepairableCount;

    /// <summary>
    /// 「N 件のリンク切れ画像(M 件が自動修復可能)」見出し(GF-V4-02・原典 view-prism RepairModal 準拠)。
    /// 自動修復可能=missing ごとに hash+拡張子+サイズで候補探索し**ちょうど 1 件**の missing 数。
    /// </summary>
    public string RepairSummary => _localization.T("repair.summary", new Dictionary<string, string>
    {
        ["missing"] = MissingImages.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["auto"] = AutoRepairableCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
    });

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

        AutoRepairableCount = await CountAutoRepairableAsync();

        OnPropertyChanged(nameof(HasNoMissing));
        OnPropertyChanged(nameof(HasNoCandidates));
        OnPropertyChanged(nameof(RepairSummary));
    }

    /// <summary>
    /// 自動修復可能な missing 数(GF-V4-02・原典準拠): 各 missing を hash+拡張子+サイズで候補探索し、
    /// 候補が**ちょうど 1 件**(一意=曖昧でない)のものを数える。0 件/2 件以上は自動修復可能としない。
    /// </summary>
    private async Task<int> CountAutoRepairableAsync()
    {
        var count = 0;
        foreach (var missing in MissingImages)
        {
            var candidates = await _relink.GetCandidatesAsync(missing.Record.Id, DeriveAutoCriteria(missing.Record));
            if (candidates.Count == 1)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>自動修復用の検索条件(原典 既定 useHash+useExtension+useSize。値は missing から導出)。</summary>
    private static SearchCriteria DeriveAutoCriteria(ImageRecord record) => new()
    {
        Hash = record.Hash,
        Extension = System.IO.Path.GetExtension(record.FileName),
        SizeMin = record.FileSize,
        SizeMax = record.FileSize,
    };

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

    partial void OnSelectedMissingChanged(MissingImageViewModel? value)
    {
        // GF-V4-01(golden 是正・view-prism 自動修復準拠 §2.11.5): 選択した missing 自身の
        // hash+拡張子+サイズを criteria へ事前入力し、再リンク候補を**手入力なしで自動探索**する
        // (view-prism RepairModal の既定 useHash/useExtension/useSize。filename/mtime はリネームで変わるため OFF)。
        PrefillCriteriaFromMissing(value);
        _ = LoadCandidatesAsync(value);
    }

    /// <summary>選択中の missing に対する候補を再読込する(UI バインド・unit 検査の双方から呼べる awaitable 経路)。</summary>
    public Task RefreshCandidatesAsync() => LoadCandidatesAsync(SelectedMissing);

    /// <summary>
    /// 選択した missing の属性(hash・拡張子・サイズ)を criteria フォームへ事前入力する(GF-V4-01)。
    /// これにより LoadCandidatesAsync が同一 hash+拡張子+サイズの pending/normal を自動的に候補化する
    /// (ユーザーが SHA-256 を手入力する必要がなくなる)。null 選択でフォームをクリアする。
    /// </summary>
    private void PrefillCriteriaFromMissing(MissingImageViewModel? value)
    {
        if (value is null)
        {
            HashInput = null;
            NameContainsInput = null;
            ExtensionInput = null;
            MtimeFromInput = null;
            MtimeToInput = null;
            SizeMinInput = null;
            SizeMaxInput = null;
            return;
        }

        var record = value.Record;
        HashInput = record.Hash;                                   // 同一内容=同一ハッシュ(移動/リネームの中核条件)
        ExtensionInput = System.IO.Path.GetExtension(record.FileName); // ".png"(CriteriaMatcher が先頭ドット正規化)
        var size = record.FileSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
        SizeMinInput = size;
        SizeMaxInput = size;
        NameContainsInput = null;                                  // ファイル名はリネームで変わるため既定 OFF
        MtimeFromInput = null;
        MtimeToInput = null;
    }

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
