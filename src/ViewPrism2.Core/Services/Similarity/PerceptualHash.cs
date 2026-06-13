namespace ViewPrism2.Core.Services.Similarity;

/// <summary>
/// 知覚ハッシュ(DCT-pHash)の純粋計算(M-PHASH-020 / E-PHASH-031 / OC-14)。
/// レシピは仕様 §2.10.1 / K-PHASH / ADR-0008 に固定(決定性=INV-012 の生命線):
///   ①32×32 BGRA → グレースケール(y=0.299R+0.587G+0.114B を (int)(y+0.5) で 8bit 化。ToEven 不使用)
///   ②orthonormal 2 次元 DCT-II(行→列 2 パス・cos 事前計算固定表)
///   ③左上 8×8=64 係数 ④DC[0,0] を除く 63 係数の中央値 m
///   ⑤各係数 c に bit=(c&gt;m)?1:0 を行優先で 64bit ⑥最上位ビットから 16hex 小文字
///
/// Core 配置=BCL のみ(SkiaSharp 型を一切持たない)。32×32 BGRA バイト列の取得は
/// Infrastructure 側(PHashImageReader)が担う(ADR-0002 層規律 / M-PHASH-020 interface_contract)。
/// </summary>
public static class PerceptualHash
{
    /// <summary>入力画像の一辺(32×32 にリサイズ済みであること)。</summary>
    public const int Size = 32;

    /// <summary>DCT 出力から取り出す低周波ブロックの一辺(左上 8×8)。</summary>
    public const int HashSize = 8;

    /// <summary>1 ピクセルあたりのバイト数(BGRA)。</summary>
    private const int BytesPerPixel = 4;

    /// <summary>
    /// 1 次元 DCT-II の cos 固定表(K-PHASH: cos 値は (n,k) ごとに事前計算)。
    /// <c>CosTable[k, n] = cos(π(2n+1)k/(2N))</c>。毎回 Math.Cos を呼ばない(性能+決定性)。
    /// </summary>
    private static readonly double[,] CosTable = BuildCosTable();

    /// <summary>orthonormal DCT-II の正規化係数 α(k): α(0)=√(1/N)、α(k&gt;0)=√(2/N)。</summary>
    private static readonly double[] Alpha = BuildAlpha();

    /// <summary>
    /// 32×32 の BGRA バイト列から 64bit pHash を計算し 16 桁小文字 16 進で返す(OC-14)。
    /// </summary>
    /// <param name="bgra32x32">32×32×4=4096 バイトの BGRA ピクセル列(行優先・先頭ピクセルが左上)。</param>
    public static string Compute(ReadOnlySpan<byte> bgra32x32)
    {
        if (bgra32x32.Length != Size * Size * BytesPerPixel)
        {
            throw new ArgumentException(
                $"BGRA バイト列は {Size}x{Size}x{BytesPerPixel}={Size * Size * BytesPerPixel} バイトである必要があります。",
                nameof(bgra32x32));
        }

        return Hex(ComputeBits(bgra32x32));
    }

    /// <summary>32×32 BGRA バイト列から 64bit pHash(ulong)を計算する。</summary>
    public static ulong ComputeBits(ReadOnlySpan<byte> bgra32x32)
    {
        if (bgra32x32.Length != Size * Size * BytesPerPixel)
        {
            throw new ArgumentException(
                $"BGRA バイト列は {Size}x{Size}x{BytesPerPixel}={Size * Size * BytesPerPixel} バイトである必要があります。",
                nameof(bgra32x32));
        }

        // ① グレースケール化(輝度行列 32×32)。BGRA: byte[0]=B, [1]=G, [2]=R, [3]=A(A は無視)。
        var gray = new double[Size, Size];
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var offset = ((y * Size) + x) * BytesPerPixel;
                double b = bgra32x32[offset];
                double g = bgra32x32[offset + 1];
                double r = bgra32x32[offset + 2];

                // (int)(y+0.5) で 0.5 切り上げ(.NET 既定 Math.Round の ToEven=銀行丸めは使わない)
                var luminance = (int)(((0.299 * r) + (0.587 * g) + (0.114 * b)) + 0.5);
                gray[y, x] = luminance;
            }
        }

        // ② orthonormal 2 次元 DCT-II(行方向→列方向の 2 パス。O(N^3))
        var dct = Dct2D(gray);

        // 浮動小数の量子化(決定性のための CHEAT — 仕様 §2.10.1 の縮退規則「全係数が等しい場合 c=m」を成立させる)。
        // cos 固定表の丸めにより、単色画像でも非 DC 係数が数学上の 0 から ±1e-14 程度ぶれ、
        // 中央値比較(c>m)が符号ノイズで暴れて 0x8000000000000000 にならない。
        // 入力はグレースケール 8bit 整数のため有意な係数は O(1) 以上であり、6 桁丸めで
        // サブ ULP ノイズのみを 0 へ畳み込み、実構造には影響しない(縮退で c=m=0・bit=0 が成立)。
        QuantizeBlock(dct);

        // ③ 左上 8×8 の 64 係数 ④ DC[0,0] を除く 63 係数の中央値 m
        var median = MedianExcludingDc(dct);

        // ⑤ 各係数 c に bit=(c>m)?1:0 を行優先で 64bit に並べる(MSB=[0,0])
        ulong bits = 0;
        for (var i = 0; i < HashSize; i++)
        {
            for (var j = 0; j < HashSize; j++)
            {
                bits <<= 1;
                if (dct[i, j] > median)
                {
                    bits |= 1UL;
                }
            }
        }

        return bits;
    }

    /// <summary>
    /// 左上 8×8 ブロックの各係数をサブ ULP ノイズ除去のため固定精度へ丸める(決定性のための CHEAT)。
    /// cos 固定表の丸めで生じる ±1e-14 程度の符号ノイズを 0 へ畳み込み、単色画像の縮退
    /// (c=m=0・bit=0、DC のみ 1)を成立させる。6 桁(1e-6)丸めは整数輝度由来の有意係数に影響しない。
    /// </summary>
    private static void QuantizeBlock(double[,] dct)
    {
        for (var i = 0; i < HashSize; i++)
        {
            for (var j = 0; j < HashSize; j++)
            {
                dct[i, j] = Math.Round(dct[i, j], 6, MidpointRounding.AwayFromZero);
            }
        }
    }

    /// <summary>左上 8×8 のうち DC[0,0] を除いた 63 係数の中央値(昇順 31 番目、0 起点)。</summary>
    private static double MedianExcludingDc(double[,] dct)
    {
        // 63 要素(DC 除外)。偶数個でないため平均分岐は不要(K-PHASH)
        var values = new double[(HashSize * HashSize) - 1];
        var index = 0;
        for (var i = 0; i < HashSize; i++)
        {
            for (var j = 0; j < HashSize; j++)
            {
                if (i == 0 && j == 0)
                {
                    continue; // DC は支配的なため中央値計算から除外(ビットには残す)
                }

                values[index++] = dct[i, j];
            }
        }

        Array.Sort(values);
        return values[values.Length / 2]; // 31 番目(0 起点)
    }

    /// <summary>2 次元 DCT-II(行方向→列方向の 2 パス)。左上 8×8 のみ後段で使うが全 N×N を計算する。</summary>
    private static double[,] Dct2D(double[,] input)
    {
        // パス 1: 各行に 1 次元 DCT-II を適用
        var rows = new double[Size, Size];
        for (var y = 0; y < Size; y++)
        {
            for (var k = 0; k < Size; k++)
            {
                var sum = 0.0;
                for (var n = 0; n < Size; n++)
                {
                    sum += input[y, n] * CosTable[k, n];
                }

                rows[y, k] = Alpha[k] * sum;
            }
        }

        // パス 2: 各列に 1 次元 DCT-II を適用
        var result = new double[Size, Size];
        for (var x = 0; x < Size; x++)
        {
            for (var k = 0; k < Size; k++)
            {
                var sum = 0.0;
                for (var n = 0; n < Size; n++)
                {
                    sum += rows[n, x] * CosTable[k, n];
                }

                result[k, x] = Alpha[k] * sum;
            }
        }

        return result;
    }

    private static double[,] BuildCosTable()
    {
        var table = new double[Size, Size];
        for (var k = 0; k < Size; k++)
        {
            for (var n = 0; n < Size; n++)
            {
                table[k, n] = Math.Cos(Math.PI * ((2 * n) + 1) * k / (2.0 * Size));
            }
        }

        return table;
    }

    private static double[] BuildAlpha()
    {
        var alpha = new double[Size];
        alpha[0] = Math.Sqrt(1.0 / Size);
        var rest = Math.Sqrt(2.0 / Size);
        for (var k = 1; k < Size; k++)
        {
            alpha[k] = rest;
        }

        return alpha;
    }

    /// <summary>64bit を最上位ビットから 16 桁小文字 16 進で表現する。</summary>
    private static string Hex(ulong bits) => bits.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
}
