using System.Globalization;

namespace ViewPrism2.Core.Common;

/// <summary>
/// ISO 8601 UTC 日時文字列の共通フォーマッタ(INV-002: ミリ秒 3 桁・literal Z)。
/// ファイルタイムスタンプの変換等もここを経由する(K-WINFS)。
/// 文字列ソート=時系列ソートが成立する正規形。
/// </summary>
public static class IsoTimestamp
{
    private const string Pattern = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    /// <summary>UTC の <see cref="DateTime"/> を正規形文字列にする。</summary>
    public static string Format(DateTime utc)
    {
        var value = utc.Kind == DateTimeKind.Utc ? utc : utc.ToUniversalTime();
        return value.ToString(Pattern, CultureInfo.InvariantCulture);
    }

    /// <summary>正規形文字列を UTC の <see cref="DateTime"/> に戻す。</summary>
    public static DateTime Parse(string iso)
    {
        return DateTime.ParseExact(
            iso,
            Pattern,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }
}
