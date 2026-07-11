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

/// <summary>重複関係検証の結果。CandidateScore はUIの一致度%と検索しきい値に使う決定値。</summary>
public sealed record DuplicateVerificationResult
{
    public required DuplicateRelationship Relationship { get; init; }

    /// <summary>0〜100 の決定的一致度。確率ではなく、関係帯域内の順位・表示・検索に共通利用する。</summary>
    public int CandidateScore { get; init; }

    /// <summary>100%表示が許されるのは、決定的に表示画素一致を含む関係だけ。</summary>
    public bool CanDisplayOneHundredPercent
        => Relationship is DuplicateRelationship.SameFile or DuplicateRelationship.ImageContentMatch;
}

/// <summary>
/// ECO-067/GF-067-03: 検証器の画像測定値を利用者向け一致度帯へ写像する単一正本。
/// 関係ごとの帯域を跨がせず、検索しきい値・表示・順位の同一軸を保つ。
/// </summary>
public static class DuplicateCandidateScore
{
    public static int FromMean(DuplicateRelationship relationship, double mean) => relationship switch
    {
        DuplicateRelationship.SameFile or DuplicateRelationship.ImageContentMatch => 100,
        DuplicateRelationship.SubstantiallySame => InBand(99, 90, mean, 12),
        DuplicateRelationship.PartialOverlap => InBand(79, 70, mean, 40),
        DuplicateRelationship.Similar => InBand(49, 40, mean, 55),
        _ => 0,
    };

    private static int InBand(int ceiling, int floor, double value, double acceptedMaximum)
    {
        var normalized = Math.Clamp(value / acceptedMaximum, 0, 1);
        return Math.Clamp(
            (int)Math.Round(ceiling - (normalized * (ceiling - floor)), MidpointRounding.AwayFromZero),
            floor, ceiling);
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
