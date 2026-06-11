using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;

namespace ViewPrism2.Infrastructure.Scanning;

/// <summary>
/// 再リンクサービス(M-SCAN-005、REQ-017)。
/// missing 画像に対し、同一 sync_folder 内・同ハッシュの pending 画像を候補として提示し、
/// 確定で missing 行へパス情報を上書きする(missing 側 image_id は不変 — INV-001)。
/// </summary>
public sealed class RelinkService
{
    private readonly IImageRepository _images;

    public RelinkService(IImageRepository images)
    {
        _images = images;
    }

    /// <summary>候補列挙: 同一フォルダ・同ハッシュの pending を relative_path 昇順で返す(REQ-017)。</summary>
    public async Task<IReadOnlyList<RelinkCandidate>> GetCandidatesAsync(string missingImageId)
    {
        var missing = await _images.GetByIdAsync(missingImageId).ConfigureAwait(false);
        if (missing is null || missing.Status != ImageStatus.Missing)
        {
            return [];
        }

        var inFolder = await _images.GetByFolderAsync(missing.SyncFolderId).ConfigureAwait(false);
        return inFolder
            .Where(i => i.Status == ImageStatus.Pending &&
                        string.Equals(i.Hash, missing.Hash, StringComparison.Ordinal))
            .OrderBy(i => i.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.Id, StringComparer.Ordinal)
            .Select(i => new RelinkCandidate
            {
                ImageId = i.Id,
                RelativePath = i.RelativePath,
                FileSize = i.FileSize,
                ModifiedDate = i.ModifiedDate,
            })
            .ToList();
    }

    /// <summary>
    /// 確定(REQ-017): missing 行に pending 行のパス情報を上書きし status=normal・candidate_link_id=NULL、
    /// pending 行は削除(単一トランザクション)。遷移表外の入力は拒否する。
    /// </summary>
    public async Task<Result> CommitRelinkAsync(string missingImageId, string pendingImageId)
    {
        var missing = await _images.GetByIdAsync(missingImageId).ConfigureAwait(false);
        if (missing is null)
        {
            return Result.Fail(ErrorCode.NotFound, "missing 画像が存在しません。");
        }

        var pending = await _images.GetByIdAsync(pendingImageId).ConfigureAwait(false);
        if (pending is null)
        {
            return Result.Fail(ErrorCode.NotFound, "pending 画像が存在しません。");
        }

        if (missing.Status != ImageStatus.Missing || pending.Status != ImageStatus.Pending)
        {
            return Result.Fail(ErrorCode.ValidationError, "missing と pending の組み合わせのみ再リンクできます。");
        }

        if (!string.Equals(missing.SyncFolderId, pending.SyncFolderId, StringComparison.Ordinal) ||
            !string.Equals(missing.Hash, pending.Hash, StringComparison.Ordinal))
        {
            return Result.Fail(ErrorCode.ValidationError, "同一フォルダ・同ハッシュの候補のみ再リンクできます。");
        }

        await _images.ApplyRelinkAsync(missingImageId, pendingImageId).ConfigureAwait(false);
        return Result.Ok();
    }
}
