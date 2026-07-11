using ViewPrism2.Core.Models;

namespace ViewPrism2.App.ViewModels;

/// <summary>
/// ECO-062/REQ-087: 画像タブの検索実行時コンテキストを類似候補へ写像する純粋ロジック。
/// FS は親 relative path の完全一致で、prefix 衝突とサブフォルダ混入を構造的に避ける。
/// </summary>
public static class SimilarityScopeResolver
{
    public static IReadOnlyList<ImageRecord> ForFileSystem(
        IReadOnlyCollection<ImageRecord> collectionImages,
        ImageRecord baseImage,
        IReadOnlyList<string> currentPath)
    {
        var current = NormalizePath(string.Join('/', currentPath));
        if (!string.Equals(ParentPath(baseImage.RelativePath), current, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        return collectionImages
            .Where(image => image.Status == ImageStatus.Normal
                && string.Equals(image.SyncFolderId, baseImage.SyncFolderId, StringComparison.Ordinal)
                && string.Equals(ParentPath(image.RelativePath), current, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public static IReadOnlyList<ImageRecord> ForView(
        IReadOnlyCollection<ImageRecord> currentNodeImages,
        ImageRecord baseImage)
        => currentNodeImages
            .Where(image => image.Status == ImageStatus.Normal
                && string.Equals(image.SyncFolderId, baseImage.SyncFolderId, StringComparison.Ordinal))
            .ToList();

    private static string ParentPath(string relativePath)
    {
        var normalized = NormalizePath(relativePath);
        var slash = normalized.LastIndexOf('/');
        return slash < 0 ? "" : normalized[..slash];
    }

    private static string NormalizePath(string path)
        => path.Replace('\\', '/').Trim('/');
}
