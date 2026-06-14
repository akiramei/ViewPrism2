using ViewPrism2.Core.Models;

namespace ViewPrism2.Core.Services.Repair;

/// <summary>
/// criteria 条件検索の純粋絞り込み(M-CRITERIA-024 / OC-19、仕様 §2.11.1)。
/// I/O を持たない純粋関数(固定オラクルが直接呼ぶ)。指定された条件のみ AND 結合し、
/// status は <paramref name="statusTargets"/> に含むもののみを対象とする。
/// 結果は安定順(relative_path 昇順・同値は id 昇順)。
/// **1 つも条件が指定されない場合は実行せず空列を返す**(全件を返さない=誤操作防止)。
/// </summary>
public static class CriteriaMatcher
{
    /// <summary>
    /// 条件 <paramref name="c"/> に一致する画像の id 列を安定順で返す(OC-19)。
    /// </summary>
    /// <param name="images">対象画像集合(同一コレクション想定。呼び出し側が供給)。</param>
    /// <param name="c">検索条件(指定されたもののみ AND)。</param>
    /// <param name="statusTargets">対象とする status の集合(用途別。空なら結果は空)。</param>
    public static IReadOnlyList<string> Match(
        IEnumerable<ImageRecord> images,
        SearchCriteria c,
        IReadOnlySet<ImageStatus> statusTargets)
    {
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(statusTargets);

        // 空条件(全項目未指定)は非実行: 全件を返さない(§2.11.1)
        if (!HasAnyCondition(c))
        {
            return [];
        }

        var normalizedExtension = NormalizeExtension(c.Extension);

        return images
            .Where(i => statusTargets.Contains(i.Status))
            .Where(i => Matches(i, c, normalizedExtension))
            .OrderBy(i => i.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.Id, StringComparer.Ordinal)
            .Select(i => i.Id)
            .ToList();
    }

    private static bool HasAnyCondition(SearchCriteria c) =>
        c.Hash is not null ||
        c.NameContains is not null ||
        c.Extension is not null ||
        c.MtimeFrom is not null ||
        c.MtimeTo is not null ||
        c.SizeMin is not null ||
        c.SizeMax is not null;

    private static bool Matches(ImageRecord image, SearchCriteria c, string? normalizedExtension)
    {
        // hash: SHA-256 完全一致(Ordinal)
        if (c.Hash is not null && !string.Equals(image.Hash, c.Hash, StringComparison.Ordinal))
        {
            return false;
        }

        // ファイル名: 部分一致(OrdinalIgnoreCase)
        if (c.NameContains is not null &&
            image.FileName.IndexOf(c.NameContains, StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }

        // 拡張子: 完全一致(case-insensitive)。先頭ドットは正規化済み
        if (normalizedExtension is not null)
        {
            var imageExtension = NormalizeExtension(Path.GetExtension(image.FileName));
            if (!string.Equals(imageExtension, normalizedExtension, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // mtime 範囲: ISO 8601 序数文字列比較(INV-002: 文字列ソート=時系列ソート)
        if (c.MtimeFrom is not null &&
            string.CompareOrdinal(image.ModifiedDate, c.MtimeFrom) < 0)
        {
            return false;
        }

        if (c.MtimeTo is not null &&
            string.CompareOrdinal(image.ModifiedDate, c.MtimeTo) > 0)
        {
            return false;
        }

        // ファイルサイズ範囲
        if (c.SizeMin is { } min && image.FileSize < min)
        {
            return false;
        }

        if (c.SizeMax is { } max && image.FileSize > max)
        {
            return false;
        }

        return true;
    }

    /// <summary>拡張子の正規化: 先頭ドット除去・空は null(先頭ドット有無を吸収する実装定数)。</summary>
    private static string? NormalizeExtension(string? extension)
    {
        if (extension is null)
        {
            return null;
        }

        var trimmed = extension.TrimStart('.');
        return trimmed.Length == 0 ? null : trimmed;
    }
}
