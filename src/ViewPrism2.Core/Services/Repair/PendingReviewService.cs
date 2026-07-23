using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;

namespace ViewPrism2.Core.Services.Repair;

/// <summary>
/// pending 裁定サービス(ECO-129/139・REQ-101・仕様 §2.11.7・T13/T14/T15)。
/// 個別裁定と高信頼サブセットの確認つき一括再リンクを提供し、pending 以外は拒否する
/// (遷移の強制は repository の WHERE 句=原子)。
/// 物理ファイルへは一切触れない(INV-009)。
/// </summary>
public sealed class PendingReviewService
{
    private readonly IImageRepository _images;

    public PendingReviewService(IImageRepository images)
    {
        _images = images;
    }

    /// <summary>
    /// ECO-139 案Aの高信頼判定。再スキャン新規かつ同一フォルダの同ハッシュ missing を示す
    /// candidate_link_id 在庫がある pending だけを対象とする。
    /// </summary>
    public static bool IsHighConfidence(ImageRecord image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return image.Status == ImageStatus.Pending
               && image.PendingOrigin == PendingOrigin.New
               && image.CandidateLinkId is not null;
    }

    /// <summary>
    /// GF-139-01: 高信頼行のうち candidate missing が入力内で一意な 1:1 組だけを返す。
    /// 同じ missing を指す new が複数あれば全て手動確認へ回す。candidateRows が渡された場合は、
    /// 現在も missing・同一 folder・同一 hash で実際に再リンク可能な組だけへ限定する。
    /// </summary>
    public static IReadOnlyList<ImageRecord> SelectUniquelyRelinkable(
        IEnumerable<ImageRecord> images,
        IReadOnlyDictionary<string, ImageRecord>? candidateRows = null)
    {
        ArgumentNullException.ThrowIfNull(images);
        return images
            .Where(IsHighConfidence)
            .DistinctBy(image => image.Id, StringComparer.Ordinal)
            .GroupBy(image => image.CandidateLinkId!, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .Select(group => group.Single())
            .Where(image =>
                candidateRows is null
                || (candidateRows.TryGetValue(image.CandidateLinkId!, out var candidate)
                    && candidate.Status == ImageStatus.Missing
                    && string.Equals(candidate.SyncFolderId, image.SyncFolderId, StringComparison.Ordinal)
                    && string.Equals(candidate.Hash, image.Hash, StringComparison.Ordinal)))
            .ToList();
    }

    /// <summary>
    /// GF-139-01: 一意な高信頼組だけを候補 missing へ T4/REQ-017 で原子的に一括再リンクする。
    /// 戻り値は UI の提示件数とのパリティに用いる実対象件数。1 組でも stale なら全件失敗。
    /// </summary>
    public async Task<Result<int>> RelinkHighConfidenceAsync(IEnumerable<ImageRecord> images)
    {
        ArgumentNullException.ThrowIfNull(images);
        var targets = SelectUniquelyRelinkable(images);
        if (targets.Count == 0)
        {
            return Result<int>.Ok(0);
        }

        var pairs = targets
            .Select(image => (MissingImageId: image.CandidateLinkId!, PendingImageId: image.Id))
            .ToList();
        return await _images.ApplyRelinkBatchAsync(pairs).ConfigureAwait(false)
            ? Result<int>.Ok(pairs.Count)
            : Result<int>.Fail(
                ErrorCode.ValidationError,
                "一括再リンクの対象が更新されました。未裁定一覧を読み直して確認してください。");
    }

    /// <summary>受け入れる(T13): pending→normal。タグ・image_id・特徴量は不変。</summary>
    public async Task<Result> AcceptAsync(string imageId)
    {
        ArgumentException.ThrowIfNullOrEmpty(imageId);
        return await _images.AdjudicatePendingAsync(imageId, ImageStatus.Normal).ConfigureAwait(false)
            ? Result.Ok()
            : Result.Fail(ErrorCode.ValidationError, "裁定できるのは未裁定(pending)画像のみです。");
    }

    /// <summary>
    /// 別画像として扱う(T14・PEND-001 裁定): 原子的な行置換=新 image_id・normal・
    /// タグ/特徴量/類似キャッシュは CASCADE 消滅・パス/メタ維持(1 パス 1 行の不変)。
    /// 戻り値= 新しい image_id。
    /// </summary>
    public async Task<Result<string>> TreatAsNewAsync(string imageId)
    {
        ArgumentException.ThrowIfNullOrEmpty(imageId);
        var image = await _images.GetByIdAsync(imageId).ConfigureAwait(false);
        if (image is null)
        {
            return Result<string>.Fail(ErrorCode.NotFound, "画像が存在しません。");
        }

        if (image.Status != ImageStatus.Pending)
        {
            return Result<string>.Fail(ErrorCode.ValidationError, "裁定できるのは未裁定(pending)画像のみです。");
        }

        // 新しい画像として作り直す: パス・ファイルメタは現物のまま・関連(ノート含む)は引き継がない
        var replacement = image with
        {
            Id = IdGenerator.NewId(),
            Status = ImageStatus.Normal,
            CandidateLinkId = null,
            PendingOrigin = null,
            Notes = null,
        };
        return await _images.ReplacePendingAsync(imageId, replacement).ConfigureAwait(false)
            ? Result<string>.Ok(replacement.Id)
            : Result<string>.Fail(ErrorCode.ValidationError, "裁定できるのは未裁定(pending)画像のみです。");
    }

    /// <summary>削除する(T15): pending→deleted(ゴミ箱へ・タグ保持・復元可)。</summary>
    public async Task<Result> DeleteAsync(string imageId)
    {
        ArgumentException.ThrowIfNullOrEmpty(imageId);
        return await _images.AdjudicatePendingAsync(imageId, ImageStatus.Deleted).ConfigureAwait(false)
            ? Result.Ok()
            : Result.Fail(ErrorCode.ValidationError, "裁定できるのは未裁定(pending)画像のみです。");
    }
}
