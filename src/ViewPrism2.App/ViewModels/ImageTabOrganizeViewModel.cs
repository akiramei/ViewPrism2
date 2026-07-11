using CommunityToolkit.Mvvm.ComponentModel;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;
using ViewPrism2.Core.Services.Repair;
using ViewPrism2.Core.Services.Similarity;

namespace ViewPrism2.App.ViewModels;

/// <summary>
/// 整理モード(ECO-014: 類似+マージ統合「整理トレイ」)の状態+実行部+操作を ImageTabViewModel(god-VM)
/// から切り出した子 VM(ECO-036 第3段 = M-UI-ORGANIZE-034)。挙動不変のリファクタで、ホスト型を参照しない
/// (コンストラクタ関数注入)。転送殻方式(order §12.2) — 全公開契約(XAML 40 箇所・tests)はホスト
/// (ImageTabViewModel)側に残り、本 VM の状態を転送プロパティ/コマンド殻経由で読む。モードフラグ
/// (_organizeMode)・排他制御・Recompute のトレイ構築・HandleItemClick ディスパッチはホスト側に残る
/// (60-change-order-eco-036.md §12.2/§12.5)。
/// サービス3種(SimilaritySearchService/MergeService/CriteriaSearchService)の所有は本 VM へ移る
/// (CriteriaSearchService はホストで new していたものを本 VM コンストラクタ内 new へ変更 — images を注入)。
/// </summary>
public sealed partial class ImageTabOrganizeViewModel : ObservableObject
{
    private readonly SimilaritySearchService _similar;
    private readonly MergeService _merge;
    private readonly CriteriaSearchService _criteriaSearch;
    private readonly Func<string?> _getCollectionId;
    private readonly Func<IReadOnlyList<ImageRecord>> _getSimilarityScopeCandidates;
    private readonly Action _recompute;
    private readonly Action _refreshSelectionMarkers;
    private readonly Func<Task> _reloadImagesAsync;
    private readonly Action _notifySearchState;
    private readonly SimilaritySearchSession _searchSession = new();

    // ---- 整理モード状態(order §12.1。ECO-044: タグ統合トグル _includeTags は撤去=常時 ON が確定・IMG-011 裁定②) ----
    private string? _mergeTargetId;
    private readonly List<string> _organizeTargets = new();
    private string _searchMethod = "similar";    // "similar" | "criteria"
    private int _similarThreshold = 70; // 既定は仕様値 70(REQ-064/065・ECO-050 — 80 は工場仮置きの逸脱だった)
    // ECO-055: 条件検索= CAD 意味論(マージ先との属性一致トグル 5 種)。自由入力 2 欄は撤去(裁定②a)
    private bool _condHash;
    private bool _condExt;
    private bool _condSize;
    private bool _condName;
    private bool _condDate;
    private bool _hasSearched;
    private bool _searchOpen; // ECO-056(v2 3 ゾーン): 下部ピンの「似た画像を探す」折りたたみ状態
    private List<(string ImageId, int Score, bool IsCriteria)> _searchResults = new();
    private bool _organizeDone;
    private int _doneSourceCount;
    // ECO-044(IMG-011 裁定③): 直近マージの操作ログ id と取り消し可否・不可理由
    private string? _undoOperationId;
    private bool _canUndo;
    private string? _undoNote;

    private readonly IImageRepository _images; // ECO-055: マージ先レコードの属性解決(条件検索の基準)

    public ImageTabOrganizeViewModel(
        IImageRepository images,
        SimilaritySearchService similar,
        MergeService merge,
        Func<string?> getCollectionId,
        Func<IReadOnlyList<ImageRecord>> getSimilarityScopeCandidates,
        Action recompute,
        Action refreshSelectionMarkers,
        Func<Task> reloadImagesAsync,
        Action? notifySearchState = null)
    {
        _similar = similar;
        _merge = merge;
        _criteriaSearch = new CriteriaSearchService(images); // 整理トレイの条件検索(E-CRITERIA-037)。images のみ依存
        _images = images; // ECO-055
        _getCollectionId = getCollectionId;
        _getSimilarityScopeCandidates = getSimilarityScopeCandidates;
        _recompute = recompute;
        _refreshSelectionMarkers = refreshSelectionMarkers;
        _reloadImagesAsync = reloadImagesAsync;
        _notifySearchState = notifySearchState ?? (() => { });
        _searchSession.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(string.Empty);
            _notifySearchState();
        };
    }

    // ---- ホストの転送プロパティが読む生状態(R1: 本体は子・ホストは委譲のみ) ----
    public string? MergeTargetId => _mergeTargetId;
    public IReadOnlyList<string> Targets => _organizeTargets;
    public IReadOnlyList<(string ImageId, int Score, bool IsCriteria)> SearchResults => _searchResults;

    public bool HasMergeTarget => _mergeTargetId is not null;
    public bool HasOrganizeTargets => _organizeTargets.Count > 0;
    public string OrganizeTargetsCountLabel => $"{_organizeTargets.Count} 枚";

    public bool IsSimilarMethod => _searchMethod == "similar";
    public bool IsCriteriaMethod => _searchMethod == "criteria";

    /// <summary>類似度しきい値(%)。1枚から探す=マージ先起点。</summary>
    public int SimilarThreshold
    {
        get => _similarThreshold;
        set { _similarThreshold = Math.Clamp(value, 50, 100); OnPropertyChanged(); OnPropertyChanged(nameof(SimilarThresholdLabel)); }
    }
    public string SimilarThresholdLabel => $"{_similarThreshold}%";
    /// <summary>類似検索はマージ先(基準画像)が必要。</summary>
    public bool CanRunSimilar => _mergeTargetId is not null;
    // ECO-055: マージ先との属性一致トグル(順序はモック condDefs: hash/ext/size/name/date)。
    // 通知は全通知(ECO-038 CR-6 同型 — CanRunSearch の派生を漏らさない)
    public bool CondHash { get => _condHash; set { _condHash = value; OnPropertyChanged(string.Empty); } }
    public bool CondExt { get => _condExt; set { _condExt = value; OnPropertyChanged(string.Empty); } }
    public bool CondSize { get => _condSize; set { _condSize = value; OnPropertyChanged(string.Empty); } }
    public bool CondName { get => _condName; set { _condName = value; OnPropertyChanged(string.Empty); } }
    public bool CondDate { get => _condDate; set { _condDate = value; OnPropertyChanged(string.Empty); } }
    private bool HasAnyCond => _condHash || _condExt || _condSize || _condName || _condDate;
    public bool Searching => _searchSession.IsActive;
    public bool SearchPreparing => _searchSession.Preparing;
    public bool SearchComparing => _searchSession.Comparing;
    public bool SearchCancelling => _searchSession.Cancelling;
    public bool ShowSearchProgress => _searchSession.ShowProgress;
    public bool ShowSearchSettings => !_searchSession.ShowProgress;
    public bool ShowStartSearch => !_searchSession.ShowProgress;
    public bool ShowCancelSearch => _searchSession.ShowProgress;
    public bool CanCancelSearch => !_searchSession.Cancelling;
    public string SearchCancelButtonLabel => _searchSession.Cancelling ? "停止中" : "停止";
    public string SearchProgressLabel => _searchSession.ProgressLabel;
    public double SearchProgressValue => _searchSession.ProgressValue;
    public bool SearchProgressIndeterminate => _searchSession.ProgressIndeterminate;
    public bool HasSearched => _hasSearched;
    /// <summary>検索実行可否(ECO-055 裁定③): 条件検索もマージ先が必須(dest と比べる意味論)+
    /// 条件 1 つ以上(空条件非実行= REQ-068)。類似は従来どおりマージ先必須。</summary>
    public bool CanRunSearch => _mergeTargetId is not null && (!IsCriteriaMethod || HasAnyCond);

    public bool CanExecuteMerge => _mergeTargetId is not null && _organizeTargets.Count > 0 && !_organizeDone;
    // ECO-056(v2 モック): 実行可= 総数(対象+マージ先)→1枚 を明示。不可= 素の文言+下の理由注記
    public string MergeButtonLabel => CanExecuteMerge ? $"マージを実行（{_organizeTargets.Count + 1}枚 → 1枚）" : "マージを実行";
    public bool ShowMergeBlockedNote => !_organizeDone && !CanExecuteMerge;
    public string MergeBlockedNote => _mergeTargetId is null ? "宛先を選んでください" : "整理対象を1枚以上追加してください";
    /// <summary>下部ピンの「似た画像を探す」折りたたみ(ECO-056/v2 3 ゾーン: 畳んで整理対象リストに場所を譲る)。</summary>
    public bool SearchOpen => _searchOpen;
    /// <summary>検索結果ヘッダ右端の方式ラベル(ECO-056/v2 モック searchMethodLabel)。</summary>
    public string SearchMethodLabel => _searchMethod == "similar" ? $"類似画像検索 · {_similarThreshold}% 以上" : "条件検索";
    public bool OrganizeDone => _organizeDone;
    public string DoneSummary => $"{_doneSourceCount + 1} 枚を 1 枚へまとめ、{_doneSourceCount} 枚を削除しました。";

    // ---- ECO-044(IMG-011 裁定③): ログに基づく補償 Undo ----
    public bool CanUndo => _canUndo;
    /// <summary>取り消し不可時の理由(完了パネルに表示)。null=非表示。</summary>
    public string? UndoNote => _undoNote;
    public bool HasUndoNote => _undoNote is not null;

    /// <summary>整理モード開始/再開時の状態リセット(旧 ResetOrganizeState と同型)。ホストの ToggleOrganize/EnterDelete/ToggleWork 等の排他リセットから直接呼ばれる。</summary>
    public void ResetState()
    {
        _mergeTargetId = null;
        _organizeTargets.Clear();
        _searchMethod = "similar";
        _condHash = false; _condExt = false; _condSize = false; _condName = false; _condDate = false; // ECO-055
        _searchSession.Invalidate(); _hasSearched = false;
        _searchOpen = false; // ECO-056: 検索パネルは畳んだ状態で開始(v2 モック direct シナリオ)
        _searchResults = new();
        _organizeDone = false; _doneSourceCount = 0;
        _undoOperationId = null; _canUndo = false; _undoNote = null; // ECO-044
        OnPropertyChanged(string.Empty); // 将来用(本段の挙動はホスト一括通知で閉じる・order §12.2)
    }

    /// <summary>グリッドで残したい1枚をマージ先にする(整理対象に入っていれば外す)。呼び出し元(ホスト HandleItemClick)が _organizeMode を確認済み。</summary>
    public void SetMergeTarget(string imageId)
    {
        InvalidateSearchContext();
        _organizeTargets.Remove(imageId);
        _mergeTargetId = imageId;
        OnPropertyChanged(string.Empty);
    }

    public void ToggleTarget(string imageId)
    {
        if (_mergeTargetId is null || imageId == _mergeTargetId) return;
        if (!_organizeTargets.Remove(imageId)) _organizeTargets.Add(imageId);
        OnPropertyChanged(string.Empty);
    }

    public void RemoveTarget(string imageId)
    {
        _organizeTargets.Remove(imageId);
        OnPropertyChanged(string.Empty);
    }

    /// <summary>マージ先の解除(ECO-056/CAD v2 clearDest・A-2 裁定=REQ-067): 整理対象は保持し、
    /// マージ先のみ未設定へ戻す(実行・検索の不活性化は CanExecuteMerge/CanRunSearch 派生が担う)。</summary>
    public void ClearMergeTarget()
    {
        InvalidateSearchContext();
        _mergeTargetId = null;
        OnPropertyChanged(string.Empty);
    }

    /// <summary>整理対象をすべて外す(ECO-056/v2 モック clearTray「すべて解除」)。マージ先は保持。</summary>
    public void ClearTargets()
    {
        _organizeTargets.Clear();
        OnPropertyChanged(string.Empty);
    }

    /// <summary>検索結果からグリッドへ戻る(ECO-056/CAD backToGrid — v1 モックから定義・実装は 51ad8ee から欠落)。
    /// モック実測= view のみ切替・検索結果は保持(再検索まで不変)。整理モードは維持。</summary>
    public void BackToGrid()
    {
        _hasSearched = false;
        OnPropertyChanged(string.Empty);
    }

    /// <summary>「似た画像を探す」パネルの開閉(ECO-056/v2 3 ゾーン: 下部ピン内の折りたたみ)。</summary>
    public void ToggleSearchOpen()
    {
        _searchOpen = !_searchOpen;
        OnPropertyChanged(string.Empty);
    }

    /// <summary>整理対象をマージ先へ昇格し、元のマージ先を整理対象へ戻す(モック「マージ先にする」)。</summary>
    public void PromoteToMergeTarget(string imageId)
    {
        if (!_organizeTargets.Remove(imageId)) return;
        InvalidateSearchContext();
        if (_mergeTargetId is not null) _organizeTargets.Add(_mergeTargetId);
        _mergeTargetId = imageId;
        OnPropertyChanged(string.Empty);
    }

    public void SetSearchMethod(string method)
    {
        if (_searchSession.IsActive) return;
        _searchMethod = method == "criteria" ? "criteria" : "similar";
        OnPropertyChanged(string.Empty);
    }

    /// <summary>検索結果の候補を整理対象へ追加する(マージ先が前提・モック「整理対象に追加」)。</summary>
    public void AddCandidate(string imageId)
    {
        if (_mergeTargetId is null || string.Equals(imageId, _mergeTargetId, StringComparison.Ordinal)) return;
        if (!_organizeTargets.Contains(imageId)) _organizeTargets.Add(imageId);
        OnPropertyChanged(string.Empty);
    }

    /// <summary>似た画像を探す: 類似(E-SIMSEARCH-032)または条件(E-CRITERIA-037)。結果を中央ペインへ。
    /// 類似検索は世代付きセッションで管理し、キャンセル済みの遅延完了を公開しない。</summary>
    public async Task RunSearchAsync()
    {
        var collectionId = _getCollectionId();
        if (collectionId is null) return;
        var results = new List<(string ImageId, int Score, bool IsCriteria)>();
        SimilaritySearchSession.Run? run = null;
        try
        {
            if (_searchMethod == "criteria")
            {
                // ECO-055: マージ先(dest)との属性一致検索(CAD 意味論)。dest 必須(裁定③)・空条件非実行
                if (_mergeTargetId is not null && HasAnyCond)
                {
                    var dest = await _images.GetByIdAsync(_mergeTargetId).ConfigureAwait(true);
                    if (dest is not null)
                    {
                        var criteria = OrganizeCriteria.FromMergeTarget(
                            dest, _condHash, _condExt, _condSize, _condName, _condDate);
                        var recs = await _criteriaSearch.SearchAsync(collectionId, criteria,
                            new HashSet<ImageStatus> { ImageStatus.Normal }, CancellationToken.None).ConfigureAwait(true);
                        foreach (var r in recs)
                        {
                            if (string.Equals(r.Id, _mergeTargetId, StringComparison.Ordinal)) continue; // マージ先自身は候補に出さない
                            results.Add((r.Id, 100, true));
                        }
                    }
                }
            }
            else if (_mergeTargetId is not null) // 類似は基準(マージ先)が必要
            {
                run = _searchSession.Start();
                var found = await _similar.FindSimilarInScopeAsync(
                    _mergeTargetId, _similarThreshold, _getSimilarityScopeCandidates(),
                    ct: run.Token,
                    detailedProgress: _searchSession.CreateProgress(run)).ConfigureAwait(true);
                foreach (var s in found) results.Add((s.ImageId, s.Score, false));
                if (!_searchSession.TryComplete(run)) return;
            }
        }
        catch (OperationCanceledException) when (run?.Token.IsCancellationRequested == true)
        {
            return;
        }
        finally
        {
            if (run is not null) _searchSession.Finish(run);
        }
        _searchResults = results;
        _hasSearched = true;
        _recompute();
    }

    /// <summary>利用者操作による停止。完了済み結果は次の正常完了まで保持する。</summary>
    public void CancelSearch() => _searchSession.Cancel();

    /// <summary>整理文脈が変わったとき、実行中検索とその遅延結果を無効化する。</summary>
    public void InvalidateSearchContext(bool clearResults = true)
    {
        _searchSession.Invalidate();
        if (clearResults)
        {
            _hasSearched = false;
            _searchResults = new();
        }
        OnPropertyChanged(string.Empty);
    }

    /// <summary>マージ実行: E-MERGE-034(原子・タグ union・source=deleted・物理非破壊 INV-009)。完了後にデータ再読込。</summary>
    public async Task ExecuteMergeAsync()
    {
        if (_mergeTargetId is null || _organizeTargets.Count == 0) return;
        var target = _mergeTargetId;
        var sources = _organizeTargets.ToList();
        var result = await _merge.MergeAsync(target, sources).ConfigureAwait(true);
        if (!result.IsSuccess) return;
        _doneSourceCount = sources.Count;
        _organizeDone = true;
        _mergeTargetId = null;
        _organizeTargets.Clear();
        _hasSearched = false;
        _searchResults = new();
        // ECO-044: 直近マージの操作ログを「取り消す」の対象として保持(実行可能条件も初期評価)
        var op = await _merge.GetLatestOperationAsync(target).ConfigureAwait(true);
        _undoOperationId = op?.Id;
        _canUndo = op is not null && (await _merge.EvaluateUndoAsync(op.Id).ConfigureAwait(true)).IsSuccess;
        _undoNote = null;
        await _reloadImagesAsync().ConfigureAwait(true);
        _recompute();
    }

    /// <summary>
    /// 取り消す(ECO-044/IMG-011 裁定③): ログに基づく補償 Undo。実行可能条件はサービス側で
    /// 再判定される(破れていれば失敗理由を UndoNote に出しボタンを不活性化)。
    /// 成功時は完了状態を畳んでトレイをリセットし、データ再読込(sources が一覧へ戻る)。
    /// </summary>
    public async Task UndoMergeAsync()
    {
        if (_undoOperationId is null) return;
        var result = await _merge.UndoMergeAsync(_undoOperationId).ConfigureAwait(true);
        if (!result.IsSuccess)
        {
            _canUndo = false;
            _undoNote = result.Message ?? "取り消しできません。";
            _recompute();
            return;
        }
        ResetState(); // 完了状態を畳んでトレイ初期化(整理モードは維持・_undoOperationId も消える)
        await _reloadImagesAsync().ConfigureAwait(true);
        _recompute();
    }

    /// <summary>別の整理を続ける: 完了状態を畳んでトレイをリセット(整理モードは維持)。
    /// 通知は殻側の最終 Recompute 1 回のみ(旧版と同一回数 — golden 所見 G-E36S3 の是正: 二重 Recompute の除去)。</summary>
    public void ContinueOrganize() => ResetState();
}
