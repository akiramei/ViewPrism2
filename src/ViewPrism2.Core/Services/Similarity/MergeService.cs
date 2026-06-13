using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;

namespace ViewPrism2.Core.Services.Similarity;

/// <summary>
/// マージサービス(M-MERGE-022 / E-MERGE-034、仕様 §2.10.5)。
/// マージ先(保持)1 枚とマージ元(統合)1 枚以上を指定し、単一トランザクションで原子適用する:
///   1. タグ集約(union): MergeCalculator(マージ先優先・NULL 補完・多元 id 昇順先勝ち・simple union)
///   2. マージ元の status を Deleted にする(image_tags は削除しない)
///   3. 物理画像ファイルは一切移動・削除・変更しない(INV-009)
/// マージ先・マージ元はいずれも Normal を前提とし、同一指定・マージ元重複は拒否する。
/// 失敗時は全ロールバック(IMergeRepository.ApplyMergeAsync が単一トランザクションで保証)。
/// </summary>
public sealed class MergeService
{
    private readonly IImageRepository _images;
    private readonly ITagRepository _tags;
    private readonly IMergeRepository _merge;

    public MergeService(IImageRepository images, ITagRepository tags, IMergeRepository merge)
    {
        _images = images;
        _tags = tags;
        _merge = merge;
    }

    /// <summary>マージを実行する(原子)。マージ先/元は Normal 前提。拒否は ValidationError。</summary>
    public async Task<Result> MergeAsync(string targetId, IReadOnlyList<string> sourceIds)
    {
        ArgumentException.ThrowIfNullOrEmpty(targetId);
        ArgumentNullException.ThrowIfNull(sourceIds);

        if (sourceIds.Count == 0)
        {
            return Result.Fail(ErrorCode.ValidationError, "マージ元を 1 つ以上指定してください。");
        }

        // マージ元の重複拒否(同一 id の重複指定)
        var distinctSources = new HashSet<string>(sourceIds, StringComparer.Ordinal);
        if (distinctSources.Count != sourceIds.Count)
        {
            return Result.Fail(ErrorCode.ValidationError, "マージ元に重複があります。");
        }

        // マージ先=マージ元の自己マージ拒否
        if (distinctSources.Contains(targetId))
        {
            return Result.Fail(ErrorCode.ValidationError, "マージ先とマージ元に同一画像は指定できません。");
        }

        // マージ先は Normal 前提
        var target = await _images.GetByIdAsync(targetId).ConfigureAwait(false);
        if (target is null)
        {
            return Result.Fail(ErrorCode.NotFound, "マージ先画像が存在しません。");
        }

        if (target.Status != ImageStatus.Normal)
        {
            return Result.Fail(ErrorCode.ValidationError, "マージ先は通常状態の画像である必要があります。");
        }

        // マージ元は全て Normal 前提。id 昇順で処理(多元の決着順、§2.10.5)
        var orderedSourceIds = sourceIds.OrderBy(id => id, StringComparer.Ordinal).ToList();
        var sourcesTagsByIdAsc = new List<IReadOnlyList<ImageTag>>(orderedSourceIds.Count);
        foreach (var sourceId in orderedSourceIds)
        {
            var source = await _images.GetByIdAsync(sourceId).ConfigureAwait(false);
            if (source is null)
            {
                return Result.Fail(ErrorCode.NotFound, "マージ元画像が存在しません。");
            }

            if (source.Status != ImageStatus.Normal)
            {
                return Result.Fail(ErrorCode.ValidationError, "マージ元は通常状態の画像である必要があります。");
            }

            var tags = await _tags.GetImageTagsAsync(sourceId).ConfigureAwait(false);
            sourcesTagsByIdAsc.Add(tags);
        }

        // タグ集約(純粋計算)。マージ先の image_id を割り当てる
        var targetTags = await _tags.GetImageTagsAsync(targetId).ConfigureAwait(false);
        var merged = MergeCalculator.Merge(targetTags, sourcesTagsByIdAsc);
        var mergedTags = merged.Tags
            .Select(t => new ImageTag { ImageId = targetId, TagId = t.TagId, Value = t.Value })
            .ToList();

        // 原子適用(単一トランザクション・失敗時全ロールバック)。物理ファイルには触れない(INV-009)
        await _merge.ApplyMergeAsync(targetId, mergedTags, orderedSourceIds).ConfigureAwait(false);
        return Result.Ok();
    }
}
