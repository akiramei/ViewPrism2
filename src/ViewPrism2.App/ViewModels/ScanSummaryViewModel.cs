using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ViewPrism2.App.Services;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;

namespace ViewPrism2.App.ViewModels;

/// <summary>二段階スキャンの結末(ECO-130)。既定=破棄(Applied=false・Error=null)。</summary>
public sealed record ScanStagingOutcome(bool Applied, ScanSummary? Summary, string? Error)
{
    public static readonly ScanStagingOutcome Discarded = new(false, null, null);
}

/// <summary>遷移別サマリーの 1 行(SC-2〜4 の L6 概要テーブル)。</summary>
public sealed record ScanSummaryRow(
    string DotColor, string Label, string? TransLabel, string CountText, bool IsDim, bool IsTotal)
{
    public bool HasTrans => TransLabel is not null;

    public bool HasDot => DotColor.Length > 0;
}

/// <summary>詳細(SC-5)の例示 1 行。チップ様式= pending 系(琥珀破線)/missing 系(赤)/新規(青)。</summary>
public sealed record ScanDetailItem(string Path, string? FromLabel, string ToLabel, string ChipStyle)
{
    public bool HasFrom => FromLabel is not null;

    public bool IsAmber => ChipStyle == "amber";

    public bool IsRed => ChipStyle == "red";

    public bool IsBlue => ChipStyle == "blue";
}

/// <summary>詳細(SC-5)の遷移別グループ。</summary>
public sealed record ScanDetailGroup(
    string Title, string CountText, IReadOnlyList<ScanDetailItem> Items, string? MoreText)
{
    public bool HasMore => MoreText is not null;
}

/// <summary>
/// スキャン結果確認(ECO-130/REQ-100・E-UI-SCANSTAGE-048、CAD= ViewPrismUI scan_summary.md SC-1〜6)。
/// 単一 Window 内の状態切替: Scanning(SC-1)→ Summary(SC-2〜4)⇆ Detail(SC-5)→ Applying。
/// SC-6(大規模適用の確認)は既存 ConfirmDialog(CMP-011)を再利用する。
/// ✕クローズ=破棄/キャンセルと同義(適用は明示ボタンのみ=安全側・E-UI-SCANSTAGE-048 invariant)。
/// </summary>
public sealed partial class ScanSummaryViewModel : ObservableObject
{
    private readonly ScanCoordinator _scans;
    private readonly LocalizationService _localization;
    private readonly IWindowService _windows;
    private readonly SyncFolder _folder;
    private readonly CancellationTokenSource _cts = new();
    private readonly Stopwatch _stopwatch = new();
    private ScanStaging? _staging;
    private bool _closeRequested;

    public ScanSummaryViewModel(
        ScanCoordinator scans,
        LocalizationService localization,
        IWindowService windows,
        SyncFolder folder)
    {
        _scans = scans;
        _localization = localization;
        _windows = windows;
        _folder = folder;
        Loc = new LocalizationProxy(localization);
        ScanPathText = folder.IncludeSubfolders
            ? $"{folder.Path} — {localization.T("scan.subfolders")}"
            : folder.Path;
        _windowTitle = T("scan.titleScanning", ("name", folder.Name));
        _processedText = T("scan.processed", ("count", "0"));
        _elapsedText = T("scan.elapsed", ("time", "00:00"));
    }

    /// <summary>ウィンドウを閉じる要求(true=適用完了)。View 側が購読して Close する。</summary>
    public event EventHandler? RequestClose;

    public LocalizationProxy Loc { get; }

    /// <summary>結末(既定=破棄)。WindowService が ShowDialog 後に読む。</summary>
    public ScanStagingOutcome Outcome { get; private set; } = ScanStagingOutcome.Discarded;

    /// <summary>差分計算の自動開始(Window.Opened)。撮影ハーネスは false で表示だけ再現する。</summary>
    public bool AutoStart { get; set; } = true;

    /// <summary>SC-6 確認ダイアログの表示中(再入防止= R8 所見2)。</summary>
    public bool IsConfirmOpen => _confirmOpen;

    /// <summary>VM 起点のクローズ要求済み(View の Closing ブロック判定用)。</summary>
    public bool CloseRequested => _closeRequested;

    private bool _confirmOpen;

    public string CollectionName => _folder.Name;

    public string ScanPathText { get; }

    // ---- 面の状態(Scanning → Summary ⇆ Detail → Applying) ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsScanningPhase))]
    [NotifyPropertyChangedFor(nameof(IsSummaryPhase))]
    [NotifyPropertyChangedFor(nameof(IsDetailPhase))]
    [NotifyPropertyChangedFor(nameof(IsApplyingPhase))]
    private ScanStagePhase _phase = ScanStagePhase.Scanning;

    public bool IsScanningPhase => Phase == ScanStagePhase.Scanning;

    public bool IsSummaryPhase => Phase == ScanStagePhase.Summary;

    public bool IsDetailPhase => Phase == ScanStagePhase.Detail;

    public bool IsApplyingPhase => Phase == ScanStagePhase.Applying;

    [ObservableProperty]
    private string _windowTitle;

    // ---- SC-1 スキャン中 ----

    [ObservableProperty]
    private string _processedText;

    [ObservableProperty]
    private string _elapsedText;

    // ---- SC-2〜4 サマリー ----

    [ObservableProperty]
    private string _headerSubText = string.Empty;

    [ObservableProperty]
    private bool _isRateGreen;

    [ObservableProperty]
    private bool _isRateYellow;

    [ObservableProperty]
    private bool _isRateRed;

    [ObservableProperty]
    private string _rateValueText = string.Empty;

    [ObservableProperty]
    private string _rateDescText = string.Empty;

    [ObservableProperty]
    private string _workloadLeadText = string.Empty;

    [ObservableProperty]
    private string _workloadSubText = string.Empty;

    [ObservableProperty]
    private bool _hasWorkload;

    public List<ScanSummaryRow> SummaryRows { get; } = [];

    [ObservableProperty]
    private bool _showDetailButton;

    [ObservableProperty]
    private string _applyLabel = string.Empty;

    [ObservableProperty]
    private bool _canApply;

    [ObservableProperty]
    private string? _statusMessage;

    // ---- 適用中 ----

    [ObservableProperty]
    private int _applyPercent;

    // ---- SC-5 詳細 ----

    public List<ScanDetailGroup> DetailGroups { get; } = [];

    /// <summary>差分計算を開始する(Window.Opened から。UI thread で呼ぶこと=Progress の文脈捕捉)。</summary>
    public async Task StartAsync()
    {
        _stopwatch.Start();
        var processed = new Progress<int>(count =>
        {
            ProcessedText = T("scan.processed", ("count", N0(count)));
            ElapsedText = T("scan.elapsed", ("time", _stopwatch.Elapsed.ToString(@"mm\:ss", CultureInfo.InvariantCulture)));
        });

        try
        {
            var result = await _scans.StageAsync(_folder.Id, processed, _cts.Token);
            if (!result.IsSuccess)
            {
                Outcome = new ScanStagingOutcome(false, null, ErrorMessages.Resolve(_localization, result.Error));
                Close();
                return;
            }

            PresentSummary(result.Value!);
        }
        catch (OperationCanceledException)
        {
            // キャンセル=結果全破棄(DB 無変更・REQ-100)。Outcome は既定の Discarded のまま
            Close();
        }
        catch (Exception ex)
        {
            // R8 所見5: fire-and-forget の例外漏れ= SC-1 スピナーのまま永久+未観測 Task 化を防ぐ。
            // DB 無変更(差分計算中)なので失敗理由を結末へ載せて閉じる
            Outcome = new ScanStagingOutcome(false, null, ex.Message);
            Close();
        }
    }

    /// <summary>
    /// サマリー面の提示(差分計算完了時。probe/撮影ハーネスはステージングを直接注入してここから入る)。
    /// </summary>
    public void PresentSummary(ScanStaging staging)
    {
        _staging = staging;
        BuildSummary(staging);
        WindowTitle = T("scan.titleSummary", ("name", _folder.Name));
        Phase = ScanStagePhase.Summary;
    }

    /// <summary>✕クローズ(View の Closing から): スキャン中ならキャンセル。適用はしない(安全側)。</summary>
    public void OnWindowClosing()
    {
        if (!_cts.IsCancellationRequested)
        {
            _cts.Cancel();
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _cts.Cancel();
    }

    [RelayCommand]
    private void Discard()
    {
        // R8 所見2: SC-6 確認待ち中の再入禁止(破棄報告後に確認側の適用が走る事故防止)
        if (_confirmOpen || IsApplyingPhase)
        {
            return;
        }

        Close();
    }

    [RelayCommand]
    private void ShowDetail()
    {
        if (_confirmOpen)
        {
            return;
        }

        WindowTitle = T("scan.titleDetail", ("name", _folder.Name));
        Phase = ScanStagePhase.Detail;
    }

    [RelayCommand]
    private void Back()
    {
        if (_confirmOpen)
        {
            return;
        }

        WindowTitle = T("scan.titleSummary", ("name", _folder.Name));
        Phase = ScanStagePhase.Summary;
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (_staging is not { } staging || staging.TotalChanges == 0)
        {
            return;
        }

        // 確認強度(SCAN-002): 変更合計 1,000 以上は適用前確認(CMP-011・primary=物理非破壊)
        if (ScanSummaryLogic.ConfirmTier(staging.TotalChanges) == ScanConfirmTier.ConfirmDialog)
        {
            _confirmOpen = true;
            bool confirmed;
            try
            {
                confirmed = await _windows.ConfirmAsync(
                    T("scan.applyConfirmTitle"),
                    T("scan.applyConfirmMessage",
                        ("count", N0(staging.TotalChanges)), ("missing", N0(staging.MissingTotal))),
                    T("scan.applyConfirmCta", ("count", N0(staging.TotalChanges))));
            }
            finally
            {
                _confirmOpen = false;
            }

            if (!confirmed)
            {
                return;
            }
        }

        Phase = ScanStagePhase.Applying;
        var progress = new Progress<int>(p => ApplyPercent = p);
        var result = await _scans.ApplyStagedAsync(staging, progress, CancellationToken.None);
        if (!result.IsSuccess)
        {
            StatusMessage = ErrorMessages.Resolve(_localization, result.Error);
            Phase = ScanStagePhase.Summary;
            return;
        }

        Outcome = new ScanStagingOutcome(true, result.Value, null);
        Close();
    }

    private void BuildSummary(ScanStaging s)
    {
        HeaderSubText = T("scan.headerSub",
            ("managed", N0(s.ManagedTotal)),
            ("scanned", N0(s.ScannedFiles)),
            ("completed", DateTime.Now.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture)));

        // missing 率カード(色は情報表示のみ=適用可否に影響させない・REQ-100)
        var tier = ScanSummaryLogic.RateTier(s.MissingTotal, s.ManagedTotal);
        IsRateGreen = tier == MissingRateTier.Green;
        IsRateYellow = tier == MissingRateTier.Yellow;
        IsRateRed = tier == MissingRateTier.Red;
        // 表示は小数 1 桁へ最近接丸め(9/12,400=0.07%→0.1%。閾値判定は RateTier の整数演算が正)
        var rate = s.ManagedTotal > 0
            ? (s.MissingTotal * 100.0 / s.ManagedTotal).ToString("0.0", CultureInfo.InvariantCulture)
            : "0.0";
        RateValueText = T("scan.rateValue",
            ("missing", N0(s.MissingTotal)), ("managed", N0(s.ManagedTotal)), ("rate", rate));
        RateDescText = tier switch
        {
            MissingRateTier.Red => T("scan.rateDescRed"),
            MissingRateTier.Yellow => T("scan.rateDescYellow"),
            _ => T("scan.rateDescGreen"),
        };

        // 遷移別サマリー(0 件の行は出さない。「変更なし」「変更合計」は常設。語彙= v5.0/ECO-129)
        SummaryRows.Clear();
        SummaryRows.Add(new ScanSummaryRow(DotGray, T("scan.rowUnchanged"), null, N0(s.Unchanged), true, false));
        AddRowIf(s.ContentChanged, DotAmber, "scan.rowMeta", "scan.transMeta", false);
        AddRowIf(s.Reappeared, DotAmber, "scan.rowReappeared", "scan.transReappeared", false);
        AddRowIf(s.MissingFromNormal, DotRed, "scan.rowMissing", "scan.transMissing", false);
        AddRowIf(s.MissingFromPending, DotRed, "scan.rowCandidateLost", "scan.transCandidateLost", false);
        AddRowIf(s.AddedPending, DotBlue, "scan.rowAddedPending", "scan.transAddedPending", false);
        AddRowIf(s.DeletedExcluded, DotGray, "scan.rowDeletedExcluded", null, true);
        AddRowIf(s.ReadFailures, DotAmber, "scan.rowReadFailed", null, false);
        SummaryRows.Add(new ScanSummaryRow(string.Empty, T("scan.rowTotal"), null, N0(s.TotalChanges), false, true));

        void AddRowIf(int count, string dot, string labelKey, string? transKey, bool dim)
        {
            if (count > 0)
            {
                SummaryRows.Add(new ScanSummaryRow(
                    dot, T(labelKey), transKey is null ? null : T(transKey), N0(count), dim, false));
            }
        }

        // 適用後作業量(該当があるときのみ=CAD)。裁定対象= 新規+内容変更+再出現(§2.11.7 へ接続)
        HasWorkload = s.MissingTotal > 0 || s.PendingTotal > 0;
        if (HasWorkload)
        {
            WorkloadLeadText = s.PendingTotal > 0 && s.MissingTotal > 0
                ? T("scan.workloadLeadBoth", ("missing", N0(s.MissingTotal)), ("pending", N0(s.PendingTotal)))
                : s.PendingTotal > 0
                    ? T("scan.workloadLeadPending", ("pending", N0(s.PendingTotal)))
                    : T("scan.workloadLead", ("missing", N0(s.MissingTotal)));
            // R8 所見6: 「このうち K 件」= 今回 missing 化する行のうち候補が付いた distinct 件数
            // (同一 missing を複数新ファイルが参照しても 1 と数える。既存 missing への候補は含めない)
            var newMissing = s.StatusUpdates
                .Where(u => u.Status == ImageStatus.Missing)
                .Select(u => u.Id)
                .ToHashSet(StringComparer.Ordinal);
            var candidateTargets = s.Adds
                .Where(a => a.Status == ImageStatus.Pending
                    && a.CandidateLinkId is { } link && newMissing.Contains(link))
                .Select(a => a.CandidateLinkId!)
                .Distinct(StringComparer.Ordinal)
                .Count();
            WorkloadSubText = candidateTargets > 0
                ? T("scan.workloadCandidates", ("count", N0(candidateTargets)))
                : string.Empty;
        }

        // 確認強度(SCAN-002)と適用 CTA(件数入りラベル=REQ-100)
        var confirmTier = ScanSummaryLogic.ConfirmTier(s.TotalChanges);
        ShowDetailButton = confirmTier is ScanConfirmTier.WithDetail or ScanConfirmTier.ConfirmDialog;
        CanApply = s.TotalChanges > 0;
        ApplyLabel = CanApply ? T("scan.applyCount", ("count", N0(s.TotalChanges))) : T("scan.noChanges");

        BuildDetailGroups(s);
    }

    private void BuildDetailGroups(ScanStaging s)
    {
        DetailGroups.Clear();
        AddGroup(ScanTransitionKind.ContentChanged, s.ContentChanged, "scan.rowMeta",
            e => new ScanDetailItem(e, T("scan.stateNormal"), T("scan.statePending"), "amber"));
        AddGroup(ScanTransitionKind.Reappeared, s.Reappeared, "scan.rowReappeared",
            e => new ScanDetailItem(e, T("scan.stateMissing"), T("scan.statePending"), "amber"));
        AddGroup(ScanTransitionKind.MissingFromNormal, s.MissingFromNormal, "scan.rowMissing",
            e => new ScanDetailItem(e, T("scan.stateNormal"), T("scan.stateMissing"), "red"));
        AddGroup(ScanTransitionKind.MissingFromPending, s.MissingFromPending, "scan.rowCandidateLost",
            e => new ScanDetailItem(e, T("scan.statePending"), T("scan.stateMissing"), "red"));
        AddGroup(ScanTransitionKind.AddedPending, s.AddedPending, "scan.rowAddedPending",
            e => new ScanDetailItem(e, T("scan.stateNew"), T("scan.statePending"), "blue"));

        void AddGroup(ScanTransitionKind kind, int total, string titleKey, Func<string, ScanDetailItem> map)
        {
            if (total == 0)
            {
                return;
            }

            var items = s.Examples
                .Where(e => e.Kind == kind)
                .Select(e => map(e.RelativePath))
                .ToList();
            var more = total - items.Count;
            DetailGroups.Add(new ScanDetailGroup(
                T(titleKey), N0(total), items,
                more > 0 ? T("scan.moreItems", ("count", N0(more))) : null));
        }
    }

    private void Close()
    {
        if (_closeRequested)
        {
            return;
        }

        _closeRequested = true;
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    private static string N0(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private string T(string key, params (string Key, string Value)[] args)
        => args.Length == 0
            ? _localization.T(key)
            : _localization.T(key, args.ToDictionary(a => a.Key, a => a.Value));

    private const string DotGray = "#CBD1DA";
    private const string DotAmber = "#B5670C";
    private const string DotRed = "#D83A3F";
    private const string DotBlue = "#2F6BED";
}

/// <summary>面の状態(SC-1=Scanning/SC-2〜4=Summary/SC-5=Detail/適用中=Applying)。</summary>
public enum ScanStagePhase
{
    Scanning,
    Summary,
    Detail,
    Applying,
}
