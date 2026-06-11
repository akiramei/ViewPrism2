using System.Globalization;

namespace ViewPrism2.Core.Common;

/// <summary>
/// ファイルサイズ整形(REQ-043、OC-7)。1024 進・小数 1 桁・単位 B/KB/MB/GB。
/// 単位は GB が上限(1024GB 以上も GB 表示 — 仕様 §4)。
/// </summary>
public static class ByteSizeFormatter
{
    private const double Kilo = 1024d;

    public static string Format(long bytes)
    {
        if (bytes < 1024)
        {
            return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        }

        var kb = bytes / Kilo;
        if (kb < 1024)
        {
            return kb.ToString("0.0", CultureInfo.InvariantCulture) + " KB";
        }

        var mb = kb / Kilo;
        if (mb < 1024)
        {
            return mb.ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        }

        var gb = mb / Kilo;
        return gb.ToString("0.0", CultureInfo.InvariantCulture) + " GB";
    }
}
