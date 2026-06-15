using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;

namespace ViewPrism2.Core.Services.Repair;

/// <summary>
/// トラッシュ(deleted 画像)の復元・完全削除サービス(M-TRASH-026 / OC-21・OC-22、仕様 §2.11.3-4)。
/// INV-009 第 2 の実アクション: 復元は物理ファイルの読み取り存在確認のみ(<see cref="IFilePresenceProbe"/>)、
/// 完全削除は images 行の削除のみ(image_tags/image_features/image_similarity は FK CASCADE)で、
/// どちらも物理画像ファイルへ一切触れない。復元・完全削除はともに deleted 状態のみ許可する。
/// </summary>
public sealed class TrashService
{
    private readonly IImageRepository _images;
    private readonly ISyncFolderRepository _folders;
    private readonly IFilePresenceProbe _probe;

    public TrashService(IImageRepository images, ISyncFolderRepository folders, IFilePresenceProbe probe)
    {
        _images = images;
        _folders = folders;
        _probe = probe;
    }

    /// <summary>
    /// 復元(OC-21 / T6・T7): deleted 限定。記録パス(folder.path + relative_path)に物理ファイルが
    /// 存在すれば status=normal(T6)、不在なら status=missing(T7・幽霊 normal 防止 — INV-013)。
    /// status のみ更新し、タグ/ID/特徴量は不変。deleted 以外は <see cref="ErrorCode.ValidationError"/>。
    /// 戻り値の <see cref="ImageStatus"/> は復元後の status。
    /// </summary>
    public async Task<Result<ImageStatus>> RestoreAsync(string imageId)
    {
        ArgumentException.ThrowIfNullOrEmpty(imageId);

        var image = await _images.GetByIdAsync(imageId).ConfigureAwait(false);
        if (image is null)
        {
            return Result<ImageStatus>.Fail(ErrorCode.NotFound, "画像が存在しません。");
        }

        if (image.Status != ImageStatus.Deleted)
        {
            return Result<ImageStatus>.Fail(
                ErrorCode.ValidationError, "復元できるのは削除済み(deleted)画像のみです。");
        }

        var folder = await _folders.GetByIdAsync(image.SyncFolderId).ConfigureAwait(false);
        if (folder is null)
        {
            return Result<ImageStatus>.Fail(ErrorCode.NotFound, "コレクションが存在しません。");
        }

        // 記録パスを復元。relative_path は正規形(スラッシュ区切り)なので OS 区切りへ戻す。
        // Path.Combine は純粋な文字列操作(物理 I/O ではない)。存在確認は IFilePresenceProbe に委譲する(INV-009)
        var absolutePath = Path.Combine(
            folder.Path, image.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        var fileExists = _probe.Exists(absolutePath);

        var newStatus = TrashTransition.ResolveRestore(fileExists);
        await _images.UpdateStatusAsync(imageId, newStatus).ConfigureAwait(false);
        return Result<ImageStatus>.Ok(newStatus);
    }

    /// <summary>
    /// 除外(OC-19 / T9): missing 限定。リンク切れ画像をトラッシュへ移す(status=deleted)。物理ファイルには
    /// 一切触れず(INV-009)、タグ/ID/特徴量も不変(status 更新のみ)。復元(T6/T7)で戻せる。
    /// missing 以外は <see cref="ErrorCode.ValidationError"/>。T5(normal→deleted=マージ)と対称。
    /// </summary>
    public async Task<Result> ExcludeAsync(string imageId)
    {
        ArgumentException.ThrowIfNullOrEmpty(imageId);

        var image = await _images.GetByIdAsync(imageId).ConfigureAwait(false);
        if (image is null)
        {
            return Result.Fail(ErrorCode.NotFound, "画像が存在しません。");
        }

        if (image.Status != ImageStatus.Missing)
        {
            return Result.Fail(
                ErrorCode.ValidationError, "除外できるのはリンク切れ(missing)画像のみです。");
        }

        // status=deleted のみ(物理非破壊・INV-009)。タグ/ID/特徴量は触れない。
        await _images.UpdateStatusAsync(imageId, ImageStatus.Deleted).ConfigureAwait(false);
        return Result.Ok();
    }

    /// <summary>
    /// 完全削除(OC-22 / T8): deleted 限定。images 行を削除し、image_tags/image_features/image_similarity は
    /// FK CASCADE で消滅する。物理ファイルには一切触れない(INV-014)。deleted 以外は
    /// <see cref="ErrorCode.ValidationError"/>。
    /// </summary>
    public async Task<Result> PermanentDeleteAsync(string imageId)
    {
        ArgumentException.ThrowIfNullOrEmpty(imageId);

        var image = await _images.GetByIdAsync(imageId).ConfigureAwait(false);
        if (image is null)
        {
            return Result.Fail(ErrorCode.NotFound, "画像が存在しません。");
        }

        if (image.Status != ImageStatus.Deleted)
        {
            return Result.Fail(
                ErrorCode.ValidationError, "完全削除できるのは削除済み(deleted)画像のみです。");
        }

        // images 行削除のみ(image_tags/image_features/image_similarity は FK CASCADE)。物理ファイル非破壊(INV-014)
        await _images.DeleteAsync(imageId).ConfigureAwait(false);
        return Result.Ok();
    }
}
