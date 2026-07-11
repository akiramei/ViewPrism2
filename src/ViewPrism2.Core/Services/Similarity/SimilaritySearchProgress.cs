namespace ViewPrism2.Core.Services.Similarity;

/// <summary>ECO-066/REQ-089: 類似検索の段階・候補件数進捗。</summary>
public enum SimilaritySearchPhase
{
    Preparing,
    Comparing,
}

/// <summary>
/// 結果件数ではなく候補処理の進捗。Total=0 の正常完了は 100% とする。
/// </summary>
public readonly record struct SimilaritySearchProgress(
    SimilaritySearchPhase Phase,
    int Completed,
    int Total)
{
    public int Percent => Total <= 0 ? 100 : Math.Clamp(Completed * 100 / Total, 0, 100);
}
