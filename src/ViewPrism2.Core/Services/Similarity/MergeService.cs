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
    private readonly IClock _clock;

    // clock は optional 拡張(ECO-044)— 既存 call site(固定オラクル含む)を壊さない(CHEAT-01 前例)。
    public MergeService(IImageRepository images, ITagRepository tags, IMergeRepository merge, IClock? clock = null)
    {
        _images = images;
        _tags = tags;
        _merge = merge;
        _clock = clock ?? new SystemClock();
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
        var sourceRecords = new List<ImageRecord>(orderedSourceIds.Count);
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
            sourceRecords.Add(source);
        }

        // タグ集約(純粋計算)。マージ先の image_id を割り当てる
        var targetTags = await _tags.GetImageTagsAsync(targetId).ConfigureAwait(false);
        var merged = MergeCalculator.Merge(targetTags, sourcesTagsByIdAsc);
        var mergedTags = merged.Tags
            .Select(t => new ImageTag { ImageId = targetId, TagId = t.TagId, Value = t.Value })
            .ToList();

        // 操作ログ(ECO-044/IMG-011 裁定③): タグ差分+マージ直後の内容指紋を記録(補償 Undo の根拠)。
        // 指紋はマージ後の状態を事前計算する: destination= Normal+merged タグ / sources= Deleted+タグ不変。
        var (addedTagIds, filledTags) = MergeUndoCalculator.ComputeDelta(targetTags, mergedTags);
        var sourceFingerprints = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < orderedSourceIds.Count; i++)
        {
            sourceFingerprints[orderedSourceIds[i]] = MergeUndoCalculator.ComputeFingerprint(
                ImageStatus.Deleted, sourceRecords[i].Hash, sourcesTagsByIdAsc[i]);
        }
        var operation = new MergeOperationRecord
        {
            Id = IdGenerator.NewId(),
            TargetId = targetId,
            SourceIds = orderedSourceIds,
            AddedTagIds = addedTagIds,
            FilledTags = filledTags,
            ExecutedAt = _clock.UtcNowIso(),
            TargetFingerprint = MergeUndoCalculator.ComputeFingerprint(
                ImageStatus.Normal, target.Hash, mergedTags),
            SourceFingerprints = sourceFingerprints,
        };

        // 原子適用(単一トランザクション・失敗時全ロールバック・ログ同梱)。物理ファイルには触れない(INV-009)
        await _merge.ApplyMergeAsync(targetId, mergedTags, orderedSourceIds, operation).ConfigureAwait(false);
        return Result.Ok();
    }

    // ---- ECO-044(IMG-011 裁定③): 補償 Undo ----

    /// <summary>指定 destination の最新マージ操作ログ(「取り消す」の対象)。</summary>
    public Task<MergeOperationRecord?> GetLatestOperationAsync(string targetId)
    {
        ArgumentException.ThrowIfNullOrEmpty(targetId);
        return _merge.GetLatestOperationAsync(targetId);
    }

    /// <summary>実行可能条件のみ判定する(補償は適用しない)。CanUndo の根拠。</summary>
    public async Task<Result> EvaluateUndoAsync(string operationId)
    {
        ArgumentException.ThrowIfNullOrEmpty(operationId);
        var op = await _merge.GetOperationAsync(operationId).ConfigureAwait(false);
        if (op is null)
        {
            return Result.Fail(ErrorCode.NotFound, "取り消し対象のマージ操作が見つかりません。");
        }

        return await EvaluateAsync(op).ConfigureAwait(false);
    }

    /// <summary>
    /// 補償 Undo: 実行可能条件(未取り消し・destination/sources 現存かつ指紋一致)を再判定してから
    /// 原子適用する(追加タグ行の削除+補完値の元値復帰+sources deleted→normal+undone_at)。
    /// </summary>
    public async Task<Result> UndoMergeAsync(string operationId)
    {
        ArgumentException.ThrowIfNullOrEmpty(operationId);
        var op = await _merge.GetOperationAsync(operationId).ConfigureAwait(false);
        if (op is null)
        {
            return Result.Fail(ErrorCode.NotFound, "取り消し対象のマージ操作が見つかりません。");
        }

        var eligible = await EvaluateAsync(op).ConfigureAwait(false);
        if (!eligible.IsSuccess)
        {
            return eligible;
        }

        await _merge.ApplyUndoAsync(op, _clock.UtcNowIso()).ConfigureAwait(false);
        return Result.Ok();
    }

    /// <summary>現在の destination/sources から内容指紋を再計算し、決定論判定へ渡す。</summary>
    private async Task<Result> EvaluateAsync(MergeOperationRecord op)
    {
        string? targetFingerprint = null;
        var target = await _images.GetByIdAsync(op.TargetId).ConfigureAwait(false);
        if (target is not null)
        {
            var targetTags = await _tags.GetImageTagsAsync(op.TargetId).ConfigureAwait(false);
            targetFingerprint = MergeUndoCalculator.ComputeFingerprint(target.Status, target.Hash, targetTags);
        }

        var sourceFingerprints = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var sourceId in op.SourceIds)
        {
            var source = await _images.GetByIdAsync(sourceId).ConfigureAwait(false);
            if (source is null)
            {
                sourceFingerprints[sourceId] = null; // 完全削除= 行不在
                continue;
            }

            var tags = await _tags.GetImageTagsAsync(sourceId).ConfigureAwait(false);
            sourceFingerprints[sourceId] = MergeUndoCalculator.ComputeFingerprint(source.Status, source.Hash, tags);
        }

        return MergeUndoCalculator.Evaluate(op, targetFingerprint, sourceFingerprints);
    }
}
