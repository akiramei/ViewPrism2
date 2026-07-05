namespace ViewPrism2.Core.Services.Similarity;

/// <summary>
/// pHash の 8 オリエンテーション変種(REQ-084 / ECO-048・仕様 §2.10.1a)。
/// 32×32 BGRA 格子の添字置換(二面体群 D4)で 8 変種を生成し、それぞれの pHash を算出する。
/// 再デコード・再リサイズを行わないため決定的(INV-012)。順序は仕様固定:
/// [0]=identity [1]=rotate90(時計回り) [2]=rotate180 [3]=rotate270
/// [4]=flipH(左右反転) [5]=flipV(上下反転) [6]=transpose(主対角転置) [7]=transverse(反対角転置)。
/// 変種 [0] は <see cref="PerceptualHash.Compute"/> と常に一致する(OC-14 契約不変・S-40)。
/// Core 配置=BCL のみ(<see cref="PerceptualHash"/> と同じ層規律)。
/// </summary>
public static class PHashOrientations
{
    /// <summary>変種の個数(D4 の位数)。</summary>
    public const int Count = 8;

    private const int Size = PerceptualHash.Size;
    private const int BytesPerPixel = 4;

    /// <summary>
    /// 32×32 BGRA バイト列から 8 変種の pHash(16hex)を仕様 §2.10.1a の順序で返す。
    /// </summary>
    /// <param name="bgra32x32">32×32×4=4096 バイトの BGRA ピクセル列(行優先・先頭ピクセルが左上)。</param>
    public static IReadOnlyList<string> ComputeAll(ReadOnlySpan<byte> bgra32x32)
    {
        if (bgra32x32.Length != Size * Size * BytesPerPixel)
        {
            throw new ArgumentException(
                $"BGRA バイト列は {Size}x{Size}x{BytesPerPixel}={Size * Size * BytesPerPixel} バイトである必要があります。",
                nameof(bgra32x32));
        }

        var results = new string[Count];
        results[0] = PerceptualHash.Compute(bgra32x32); // [0]=identity はレシピ pHash と一致

        var buffer = new byte[bgra32x32.Length];
        for (var kind = 1; kind < Count; kind++)
        {
            Transform(bgra32x32, buffer, kind);
            results[kind] = PerceptualHash.Compute(buffer);
        }

        return results;
    }

    /// <summary>
    /// 格子の添字置換。dst の各ピクセル (x,y) に対応する src 座標 (sx,sy) を仕様 §2.10.1a の定義で写す。
    /// </summary>
    private static void Transform(ReadOnlySpan<byte> src, Span<byte> dst, int kind)
    {
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var (sx, sy) = kind switch
                {
                    1 => (y, Size - 1 - x),            // rotate90(時計回り)
                    2 => (Size - 1 - x, Size - 1 - y), // rotate180
                    3 => (Size - 1 - y, x),            // rotate270
                    4 => (Size - 1 - x, y),            // flipH(左右反転)
                    5 => (x, Size - 1 - y),            // flipV(上下反転)
                    6 => (y, x),                       // transpose(主対角転置)
                    7 => (Size - 1 - y, Size - 1 - x), // transverse(反対角転置)
                    _ => (x, y),                       // identity(防御 — ComputeAll は kind 1〜7 のみ渡す)
                };

                var srcOffset = ((sy * Size) + sx) * BytesPerPixel;
                var dstOffset = ((y * Size) + x) * BytesPerPixel;
                src.Slice(srcOffset, BytesPerPixel).CopyTo(dst.Slice(dstOffset, BytesPerPixel));
            }
        }
    }
}
