using System.Globalization;
using ViewPrism2.Core.Common;

namespace ViewPrism2.App.ViewModels;

/// <summary>
/// ロケール書式の表示整形(REQ-043: 日時はロケール書式。格納は常に ISO 8601 UTC — INV-002)。
/// 表示はローカル時刻へ変換する。
/// </summary>
public static class LocaleFormats
{
    public static CultureInfo GetCulture(string locale)
    {
        try
        {
            return CultureInfo.GetCultureInfo(
                locale.StartsWith("ja", StringComparison.OrdinalIgnoreCase) ? "ja-JP" : "en-US");
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }

    /// <summary>ISO 8601 UTC 文字列をロケール書式(短い日付+時刻)で整形する。不正値は原文を返す。</summary>
    public static string FormatTimestamp(string iso, string locale)
    {
        try
        {
            return IsoTimestamp.Parse(iso).ToLocalTime().ToString("g", GetCulture(locale));
        }
        catch (FormatException)
        {
            return iso; // INV-008: 破損値で停止しない
        }
    }
}
