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

/// <summary>
/// 修復ライフサイクル UI の ViewModel(M-UI-REPAIR-027 / REQ-072、仕様 §2.11.5)。
/// relink フロー(missing 選択→候補自動提示→確定)。criteria フォームは**再リンク候補の絞り込み条件**であり、
/// 検索ボタンは選択中 missing の候補(Pending∪Normal)を現在の条件で再探索する(GF-V4-03・原典
/// AdvancedRepairModal 準拠=検索結果は再リンク候補に統一。Normal 限定の別検索結果リストは持たない)。
/// 各操作は M-RELINK-025 の API のみ経由(UI で状態遷移・タグ操作を再実装しない)。
/// </summary>
public sealed partial class RepairViewModel : ObservableObject
{
    private readonly string _collectionId;
    private readonly IImageRepository _images;
    private readonly ISyncFolderRepository _folders;
    private readonly RelinkService _relink;
    private readonly TrashService _trash;
    private readonly LocalizationService _localization;
    private readonly IWindowService? _windows;

    /// <summary>collection の物理ルート(サムネイル絶対パス解決用)。LoadAsync で解決する。</summary>
    private string? _rootPath;

    public RepairViewModel(
        string collectionId,
        IImageRepository images,
        ISyncFolderRepository folders,
        RelinkService relink,
        TrashService trash,
        LocalizationService localization,
        IWindowService? windows = null)
    {
        _collectionId = collectionId;
        _images = images;
        _folders = folders;
        _relink = relink;
        _trash = trash;
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

    // ---- relink フロー ----
    // ECO-075: 大量 missing(26 万行)でも CollectionChanged を大量発火させない — 一覧は
    // インスタンス一括差し替えで反映する(setter は LoadAsync のみが使う)
    [ObservableProperty]
    private ObservableCollection<MissingImageViewModel> _missingImages = [];

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

    /// <summary>候補検索(検索ボタン)が可能か。missing が選択されている必要がある(候補は missing 単位)。</summary>
    public bool CanSearchCandidates => SelectedMissing is not null;

    /// <summary>除外(選択 missing をトラッシュへ)が可能か。missing が選択されている必要がある。</summary>
    public bool CanExcludeSelected => SelectedMissing is not null;

    /// <summary>「すべて自動修復」ボタンの活性条件(自動修復可能な missing が 1 件以上)。</summary>
    public bool HasAutoRepairable => AutoRepairableCount > 0;

    /// <summary>「すべて自動修復 (M件)」ボタン文言。count を埋め込む(RepairSummary と同じ T(key, dict) 方式)。</summary>
    public string AutoRepairAllLabel => _localization.T("repair.autoRepairAll", new Dictionary<string, string>
    {
        ["count"] = AutoRepairableCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
    });

    public bool HasNoMissing => MissingImages.Count == 0;

    public bool HasNoCandidates => Candidates.Count == 0 && SelectedMissing is not null;

    public async Task LoadAsync()
    {
        Candidates.Clear();
        SelectedMissing = null;
        SelectedCandidate = null;

        // collection 物理ルートを解決(サムネイル絶対パス用)。TrashViewModel と同パターン。
        var folder = await _folders.GetByIdAsync(_collectionId);
        _rootPath = folder?.Path;

        // ECO-075: 大量 missing(26 万行)で UI スレッドを塞がない — 行ロード+VM 構築+件数計算は
        // バックグラウンド。自動修復可能数は missing ごとの候補探索(O(M×N) 全行再ロード)でなく
        // RelinkService の単一ロード集合演算で数える。
        var (items, autoRepairable) = await Task.Run(async () =>
        {
            var records = await _images.GetByFolderAsync(_collectionId).ConfigureAwait(false);
            var list = new List<MissingImageViewModel>();
            foreach (var record in records.Where(r => r.Status == ImageStatus.Missing)
                         .OrderBy(r => r.RelativePath, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(new MissingImageViewModel(record, ResolveAbsolute(record.RelativePath)));
            }

            var auto = await _relink.CountAutoRepairableAsync(_collectionId).ConfigureAwait(false);
            return (list, auto);
        });

        MissingImages = new ObservableCollection<MissingImageViewModel>(items);
        AutoRepairableCount = autoRepairable;

        OnPropertyChanged(nameof(HasNoMissing));
        OnPropertyChanged(nameof(HasNoCandidates));
        OnPropertyChanged(nameof(RepairSummary));
    }

    /// <summary>自動修復用の検索条件(原典 既定 useHash+useExtension+useSize。値は missing から導出)。</summary>
    private static SearchCriteria DeriveAutoCriteria(ImageRecord record) => new()
    {
        Hash = record.Hash,
        Extension = System.IO.Path.GetExtension(record.FileName),
        SizeMin = record.FileSize,
        SizeMax = record.FileSize,
    };

    /// <summary>
    /// 検索(GF-V4-03・原典 AdvancedRepairModal 準拠): 現在のフォーム条件で選択中 missing の
    /// 再リンク候補(Pending∪Normal)を再探索する。検索結果は再リンク候補に統一(Normal 限定の別リストは持たない)。
    /// 条件を編集(例: hash を消してサイズだけにする)→検索 で候補を広げ/絞れる。
    /// </summary>
    [RelayCommand]
    public Task SearchAsync() => RefreshCandidatesAsync();

    /// <summary>relink 確定(RelinkService.CommitRelinkAsync のみ経由)。タグ付き候補は拒否され案内文言を出す。</summary>
    [RelayCommand]
    public async Task CommitRelinkAsync()
    {
        if (SelectedMissing is not { } missing || SelectedCandidate is not { } candidate)
        {
            return;
        }

        if (_windows is not null && !await _windows.ConfirmAsync(
                _localization.T("repair.relink.title"), _localization.T("repair.relink.confirmMessage"),
                _localization.T("relink.cta")))
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

    // ---- 自動修復(VM オーケストレーション・新遷移なし。relink は CommitRelinkAsync 経由のみ) ----

    /// <summary>
    /// すべて自動修復(C-AUTOREPAIR-001 / 原典 autoRepairAll): MissingImages を走査し、各 missing の
    /// DeriveAutoCriteria(hash+拡張子+サイズ)で候補が**ちょうど 1 件**なら CommitRelinkAsync。成功数を数える。
    /// タグ付き拒否等の失敗(IsSuccess=false)はスキップし、一括を止めない(原典の per-item try/catch と同義)。
    /// 完了後 LoadAsync で再読込し、結果文言を出す。戻り値=成功数(unit 検査用)。
    /// </summary>
    public async Task<int> AutoRepairAllAsync()
    {
        var repaired = 0;
        // GF-075-01: missing ごとの候補探索(毎回フォルダ全行ロード= O(M×N))をやめ、
        // 単一パスで自動修復ペアを確定してから逐次 commit する。同一候補を取り合う 2 組目以降は
        // CommitRelinkAsync 側の検証が拒否する(旧逐次探索と同じ帰結・一括を止めない)
        var pairs = await Task.Run(() => _relink.GetAutoRepairablePairsAsync(_collectionId));
        foreach (var pair in pairs)
        {
            var result = await _relink.CommitRelinkAsync(pair.MissingImageId, pair.CandidateImageId);
            if (result.IsSuccess)
            {
                repaired++;
            }
            // 失敗(タグ付き拒否等)はスキップ — 数えない・一括を止めない
        }

        await LoadAsync();
        StatusMessage = _localization.T("repair.autoRepair.result", new Dictionary<string, string>
        {
            ["count"] = repaired.ToString(System.Globalization.CultureInfo.InvariantCulture),
        });
        return repaired;
    }

    /// <summary>
    /// 単一自動修復(C-AUTOREPAIR-001 / 原典 autoRepairSingle): 指定 missing の auto-candidate
    /// (DeriveAutoCriteria でちょうど 1 件)を CommitRelink する。1 件でなければ何もしない。完了後 LoadAsync+文言。
    /// </summary>
    public async Task AutoRepairSingleAsync(MissingImageViewModel missing)
    {
        ArgumentNullException.ThrowIfNull(missing);

        // GF-075-01: 候補探索(フォルダ全行ロード)で UI スレッドを塞がない
        var candidates = await Task.Run(() => _relink.GetCandidatesAsync(
            missing.Record.Id, DeriveAutoCriteria(missing.Record)));
        if (candidates.Count != 1)
        {
            return; // 0 件/2 件以上は自動修復対象外(曖昧)— 何もしない
        }

        var result = await _relink.CommitRelinkAsync(missing.Record.Id, candidates[0].ImageId);
        await LoadAsync();
        StatusMessage = result.IsSuccess
            ? _localization.T("repair.autoRepair.result", new Dictionary<string, string>
            {
                ["count"] = "1",
            })
            : _localization.T("repair.relink.failed") + ": "
                + ErrorMessages.Resolve(_localization, result.Error);
    }

    /// <summary>
    /// 除外(C-AUTOREPAIR-001 / T9 / 原典 excludeSelectedImages): 指定 missing を TrashService.ExcludeAsync で
    /// トラッシュへ移す(missing→deleted・物理非破壊・復元可)。成功で LoadAsync+結果文言、失敗で失敗文言。
    /// </summary>
    public async Task ExcludeAsync(MissingImageViewModel missing)
    {
        ArgumentNullException.ThrowIfNull(missing);

        var result = await _trash.ExcludeAsync(missing.Record.Id);
        if (result.IsSuccess)
        {
            await LoadAsync();
            StatusMessage = _localization.T("repair.exclude.result");
        }
        else
        {
            StatusMessage = _localization.T("repair.exclude") + ": "
                + ErrorMessages.Resolve(_localization, result.Error);
        }
    }

    /// <summary>UI バインド: 「すべて自動修復」ボタン(AutoRepairableCount>0 で活性)→ AutoRepairAllAsync。</summary>
    [RelayCommand]
    private Task AutoRepairAllCommandAsync() => AutoRepairAllAsync();

    /// <summary>UI バインド: 選択中 missing の単一自動修復(行ボタンが項目を直接渡せない場合のフォールバック)。</summary>
    [RelayCommand]
    private async Task AutoRepairSelectedAsync()
    {
        if (SelectedMissing is { } missing)
        {
            await AutoRepairSingleAsync(missing);
        }
    }

    /// <summary>UI バインド: 選択中 missing を除外(SelectedMissing 選択時に活性)。</summary>
    [RelayCommand]
    private async Task ExcludeSelectedAsync()
    {
        if (SelectedMissing is { } missing)
        {
            await ExcludeAsync(missing);
        }
    }

    partial void OnAutoRepairableCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasAutoRepairable));
        OnPropertyChanged(nameof(AutoRepairAllLabel));
    }

    partial void OnSelectedMissingChanged(MissingImageViewModel? value)
    {
        // GF-V4-01(golden 是正・view-prism 自動修復準拠 §2.11.5): 選択した missing 自身の
        // hash+拡張子+サイズを criteria へ事前入力し、再リンク候補を**手入力なしで自動探索**する
        // (view-prism RepairModal の既定 useHash/useExtension/useSize。filename/mtime はリネームで変わるため OFF)。
        PrefillCriteriaFromMissing(value);
        OnPropertyChanged(nameof(CanSearchCandidates));
        OnPropertyChanged(nameof(CanExcludeSelected));
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

    /// <summary>候補探索の世代(GF-075-01: 非同期化に伴う並行再入ガード。最新の探索だけを反映する)。</summary>
    private int _candidatesVersion;

    private async Task LoadCandidatesAsync(MissingImageViewModel? missing)
    {
        var version = ++_candidatesVersion;
        Candidates.Clear();
        SelectedCandidate = null;
        if (missing is not null)
        {
            // 候補=exact-hash pending ∪ criteria 結果(タグ付き除外・安定順は RelinkService 側で実施)。
            // 現在のフォーム条件を criteria として渡す(空条件なら null=exact-hash pending のみ)。
            // GF-075-01: 探索(フォルダ全行ロード)は UI スレッドを塞がない
            var criteria = BuildCriteria();
            var candidates = await Task.Run(() => _relink.GetCandidatesAsync(
                missing.Record.Id, HasAnyCriteria(criteria) ? criteria : null));
            if (version != _candidatesVersion)
            {
                return; // 追い越された古い探索結果は反映しない
            }

            foreach (var candidate in candidates)
            {
                // GF-V4-04(§2.11.5 表示パリティ): 候補カードはサムネイル+ファイル名+パス+サイズ+更新日時を提示し、
                // ユーザーが再リンク可否を判断できるようにする(原典 AdvancedRepairModal 準拠)。
                Candidates.Add(new RelinkCandidateViewModel(
                    candidate,
                    ByteSizeFormatter.Format(candidate.FileSize),
                    LocaleFormats.FormatTimestamp(candidate.ModifiedDate, _localization.CurrentLocale),
                    ResolveAbsolute(candidate.RelativePath)));
            }
        }

        OnPropertyChanged(nameof(HasNoCandidates));
    }

    /// <summary>
    /// 正規形(スラッシュ区切り)の相対パスを物理絶対パスへ解決する(サムネイル描画用)。
    /// ルート未解決なら null(ThumbnailImage はプレースホルダ表示)。物理 I/O はしない純粋な文字列結合。
    /// </summary>
    private string? ResolveAbsolute(string relativePath)
        => _rootPath is null
            ? null
            : System.IO.Path.Combine(_rootPath, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));

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
