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
    private readonly Action _recompute;
    private readonly Action _refreshSelectionMarkers;
    private readonly Func<Task> _reloadImagesAsync;

    // ---- 整理モード状態(order §12.1。ECO-044: タグ統合トグル _includeTags は撤去=常時 ON が確定・IMG-011 裁定②) ----
    private string? _mergeTargetId;
    private readonly List<string> _organizeTargets = new();
    private string _searchMethod = "similar";    // "similar" | "criteria"
    private int _similarThreshold = 70; // 既定は仕様値 70(REQ-064/065・ECO-050 — 80 は工場仮置きの逸脱だった)
    private string _criteriaName = "";
    private string _criteriaExt = "";
    private bool _searching;
    private bool _hasSearched;
    private List<(string ImageId, int Score, bool IsCriteria)> _searchResults = new();
    private bool _organizeDone;
    private int _doneSourceCount;
    // ECO-044(IMG-011 裁定③): 直近マージの操作ログ id と取り消し可否・不可理由
    private string? _undoOperationId;
    private bool _canUndo;
    private string? _undoNote;

    public ImageTabOrganizeViewModel(
        IImageRepository images,
        SimilaritySearchService similar,
        MergeService merge,
        Func<string?> getCollectionId,
        Action recompute,
        Action refreshSelectionMarkers,
        Func<Task> reloadImagesAsync)
    {
        _similar = similar;
        _merge = merge;
        _criteriaSearch = new CriteriaSearchService(images); // 整理トレイの条件検索(E-CRITERIA-037)。images のみ依存
        _getCollectionId = getCollectionId;
        _recompute = recompute;
        _refreshSelectionMarkers = refreshSelectionMarkers;
        _reloadImagesAsync = reloadImagesAsync;
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
    public string CriteriaName { get => _criteriaName; set => _criteriaName = value ?? ""; }
    public string CriteriaExt { get => _criteriaExt; set => _criteriaExt = value ?? ""; }
    public bool Searching => _searching;
    public bool HasSearched => _hasSearched;
    /// <summary>検索実行可否: 条件検索は常に / 類似はマージ先(基準)が要る。</summary>
    public bool CanRunSearch => IsCriteriaMethod || _mergeTargetId is not null;

    public bool CanExecuteMerge => _mergeTargetId is not null && _organizeTargets.Count > 0 && !_organizeDone;
    public string MergeButtonLabel => $"マージを実行（{_organizeTargets.Count} 枚）";
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
        _criteriaName = ""; _criteriaExt = "";
        _searching = false; _hasSearched = false;
        _searchResults = new();
        _organizeDone = false; _doneSourceCount = 0;
        _undoOperationId = null; _canUndo = false; _undoNote = null; // ECO-044
        OnPropertyChanged(string.Empty); // 将来用(本段の挙動はホスト一括通知で閉じる・order §12.2)
    }

    /// <summary>グリッドで残したい1枚をマージ先にする(整理対象に入っていれば外す)。呼び出し元(ホスト HandleItemClick)が _organizeMode を確認済み。</summary>
    public void SetMergeTarget(string imageId)
    {
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

    /// <summary>整理対象をマージ先へ昇格し、元のマージ先を整理対象へ戻す(モック「マージ先にする」)。</summary>
    public void PromoteToMergeTarget(string imageId)
    {
        if (!_organizeTargets.Remove(imageId)) return;
        if (_mergeTargetId is not null) _organizeTargets.Add(_mergeTargetId);
        _mergeTargetId = imageId;
        OnPropertyChanged(string.Empty);
    }

    public void SetSearchMethod(string method)
    {
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

    private SearchCriteria BuildCriteria() => new()
    {
        NameContains = string.IsNullOrWhiteSpace(_criteriaName) ? null : _criteriaName.Trim(),
        Extension = string.IsNullOrWhiteSpace(_criteriaExt) ? null : _criteriaExt.Trim(),
        // hash / サイズ範囲 / 更新日範囲の入力は surface 増分(M2)で結線する。
    };

    private static bool CriteriaHasAny(SearchCriteria c) =>
        c.Hash is not null || c.NameContains is not null || c.Extension is not null ||
        c.MtimeFrom is not null || c.MtimeTo is not null || c.SizeMin is not null || c.SizeMax is not null;

    /// <summary>似た画像を探す: 類似(E-SIMSEARCH-032)または条件(E-CRITERIA-037)。結果を中央ペインへ。
    /// 途中通知(searching 表示更新)は recompute 注入で現行位置を保持する(order §12.2)。</summary>
    public async Task RunSearchAsync()
    {
        var collectionId = _getCollectionId();
        if (collectionId is null) return;
        _searching = true; _recompute();
        var results = new List<(string ImageId, int Score, bool IsCriteria)>();
        try
        {
            if (_searchMethod == "criteria")
            {
                var criteria = BuildCriteria();
                if (CriteriaHasAny(criteria))
                {
                    var recs = await _criteriaSearch.SearchAsync(collectionId, criteria,
                        new HashSet<ImageStatus> { ImageStatus.Normal }, CancellationToken.None).ConfigureAwait(true);
                    foreach (var r in recs)
                    {
                        if (string.Equals(r.Id, _mergeTargetId, StringComparison.Ordinal)) continue; // マージ先自身は候補に出さない
                        results.Add((r.Id, 100, true));
                    }
                }
            }
            else if (_mergeTargetId is not null) // 類似は基準(マージ先)が必要
            {
                var found = await _similar.FindSimilarAsync(_mergeTargetId, _similarThreshold).ConfigureAwait(true);
                foreach (var s in found) results.Add((s.ImageId, s.Score, false));
            }
        }
        finally
        {
            _searching = false;
        }
        _searchResults = results;
        _hasSearched = true;
        _recompute();
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
