namespace ViewPrism2.Core.Services.Similarity;

/// <summary>
/// ECO-067 / IMG-021: 一般的な見た目の類似ではなく、整理用途の重複関係を表す。
/// 数値は候補順位の強さ順。利用者向け百分率ではない。
/// </summary>
public enum DuplicateRelationship
{
    NonSimilar = 0,
    Similar = 1,
    PartialOverlap = 2,
    SubstantiallySame = 3,
    ImageContentMatch = 4,
    SameFile = 5,
}

/// <summary>重複関係検証の結果。CandidateScore は関係分類と独立した詳細類似度。</summary>
public sealed record DuplicateVerificationResult
{
    public required DuplicateRelationship Relationship { get; init; }

    /// <summary>0〜100 の詳細類似度。検索側でpHash大局スコアとの小さい方を表示・しきい値へ使う。</summary>
    public int CandidateScore { get; init; }

    /// <summary>100%表示が許されるのは、決定的に表示画素一致を含む関係だけ。</summary>
    public bool CanDisplayOneHundredPercent
        => Relationship is DuplicateRelationship.SameFile or DuplicateRelationship.ImageContentMatch;
}

/// <summary>
/// ECO-067/GF-067-04: 位置合わせ後の測定値を関係分類と独立した連続的な詳細類似度へ写像する。
/// 小面積差分は減点するが、関係語彙の固定帯域へ強制しない。近似値は100を返さない。
/// </summary>
public static class DetailSimilarityScore
{
    public static int FromMeasurements(
        double mean,
        double changedFraction,
        double severeFraction,
        double maxBlockMean,
        double topSixBlockFraction)
    {
        var globalPenalty = Math.Clamp(mean / 55.0, 0, 1) * 20.0;
        var localMagnitude = (Math.Sqrt(Math.Clamp(changedFraction, 0, 1)) * 90.0)
            + (Math.Sqrt(Math.Clamp(severeFraction, 0, 1)) * 35.0)
            + (Math.Sqrt(Math.Clamp(maxBlockMean / 255.0, 0, 1)) * 20.0);
        var localPenalty = Math.Clamp(topSixBlockFraction, 0, 1) * Math.Min(25.0, localMagnitude);
        return Math.Clamp(
            (int)Math.Round(99.0 - globalPenalty - localPenalty, MidpointRounding.AwayFromZero),
            1, 99);
    }
}

/// <summary>
/// pHashで抽出した候補が同一原画像由来かを画像実体から検証するCore抽象。
/// 実装の画像decode/位置合わせはInfrastructureへ閉じる。
/// </summary>
public interface IDuplicateRelationshipVerifier
{
    /// <summary>検証器/閾値世代。永続cacheの世代不一致時は再検証する。</summary>
    string AdapterId { get; }

    Task<DuplicateVerificationResult> VerifyAsync(
        string absolutePathA,
        string absolutePathB,
        CancellationToken cancellationToken = default,
        bool bytesKnownDifferent = false);
}

public static class DuplicateRelationshipLabels
{
    public static string ToDisplayLabel(this DuplicateRelationship relationship) => relationship switch
    {
        DuplicateRelationship.SameFile => "同一ファイル",
        DuplicateRelationship.ImageContentMatch => "画像内容一致",
        DuplicateRelationship.SubstantiallySame => "実質同一",
        DuplicateRelationship.PartialOverlap => "部分重複",
        DuplicateRelationship.Similar => "類似（重複ではありません）",
        _ => "候補外",
    };
}
