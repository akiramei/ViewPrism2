using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;

namespace ViewPrism2.Core.Services.Repair;

/// <summary>
/// pending 裁定サービス(ECO-129/REQ-101・仕様 §2.11.7・T13/T14/T15)。
/// 裁定は 1 件ずつ確定・pending 以外は拒否(遷移の強制は repository の WHERE 句=原子)。
/// 物理ファイルへは一切触れない(INV-009)。
/// </summary>
public sealed class PendingReviewService
{
    private readonly IImageRepository _images;

    public PendingReviewService(IImageRepository images)
    {
        _images = images;
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
