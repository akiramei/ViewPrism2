using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;

namespace ViewPrism2.Core.Services.Repair;

/// <summary>
/// pending 裁定サービス(ECO-129/139・REQ-101・仕様 §2.11.7・T13/T14/T15)。
/// 個別裁定と高信頼サブセットの確認つき一括受入を提供し、pending 以外は拒否する
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
    /// ECO-139: 入力中の高信頼行だけを T13(pending→normal)として原子的に一括受入する。
    /// 戻り値は UI の提示件数とのパリティに用いる実対象件数。1 件でも stale/非 pending なら全件失敗。
    /// </summary>
    public async Task<Result<int>> AcceptHighConfidenceAsync(IEnumerable<ImageRecord> images)
    {
        ArgumentNullException.ThrowIfNull(images);
        var ids = images
            .Where(IsHighConfidence)
            .Select(image => image.Id)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (ids.Count == 0)
        {
            return Result<int>.Ok(0);
        }

        return await _images.AdjudicatePendingBatchAsync(ids, ImageStatus.Normal).ConfigureAwait(false)
            ? Result<int>.Ok(ids.Count)
            : Result<int>.Fail(
                ErrorCode.ValidationError,
                "一括裁定の対象が更新されました。未裁定一覧を読み直して確認してください。");
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
