using System.Globalization;
using System.Numerics;

namespace ViewPrism2.Core.Services.Similarity;

/// <summary>
/// 2 つの pHash のハミング距離(M-PHASH-020 / OC-14)。
/// 64bit XOR の立ちビット数(popcount)。0〜64。小さいほど似ている(仕様 §2.10.2)。
/// </summary>
public static class HammingDistance
{
    /// <summary>2 つの 64bit pHash のハミング距離(popcount(h1 XOR h2))。0〜64。</summary>
    public static int Between(ulong h1, ulong h2) => BitOperations.PopCount(h1 ^ h2);

    /// <summary>16hex 文字列で表された 2 つの pHash のハミング距離。</summary>
    public static int Between(string hex1, string hex2)
    {
        ArgumentNullException.ThrowIfNull(hex1);
        ArgumentNullException.ThrowIfNull(hex2);
        return Between(Parse(hex1), Parse(hex2));
    }

    private static ulong Parse(string hex)
        => ulong.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
}
