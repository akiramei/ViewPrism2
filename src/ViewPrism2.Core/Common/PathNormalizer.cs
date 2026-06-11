namespace ViewPrism2.Core.Common;

/// <summary>
/// 相対パスの正規化(REQ-014 / INV-005)。
/// 正規形 = スラッシュ区切り・先頭末尾スラッシュなし。比較は case-insensitive。
/// </summary>
public static class PathNormalizer
{
    /// <summary>ルートからの相対パスを正規形で返す(大文字小文字は保持)。</summary>
    public static string ToRelative(string root, string fullPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        ArgumentException.ThrowIfNullOrEmpty(fullPath);
        return Normalize(Path.GetRelativePath(root, fullPath));
    }

    /// <summary>区切りをスラッシュへ統一し、先頭末尾のスラッシュを除去する。</summary>
    public static string Normalize(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return path.Replace('\\', '/').Trim('/');
    }

    /// <summary>正規形どうしの case-insensitive 比較(INV-005)。</summary>
    public static bool Equals(string? a, string? b)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }

        return string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
    }
}
