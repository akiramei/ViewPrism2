using ViewPrism2.Core.Services.Similarity;
using Xunit;

namespace ViewPrism2.Oracle;

/// <summary>
/// S-40: 8 オリエンテーション pHash 変種(REQ-084 / ECO-048、EQ-001)。工場非開示。
/// 固定の非対称 32×32 格子に対し、ComputeAll が仕様 §2.10.1a の順序固定 8 変種を返し、
/// [0]=identity が単体 pHash(OC-14 レシピ)と一致・決定的であること、および
/// 各変換(D4・本テスト側の独立実装)を施した格子の単体 pHash が変種 [k] と一致する
/// (=任意の回転・鏡像が距離 0 で検出される)ことを凍結する。
/// </summary>
[Trait("oracle", "S-40")]
public sealed class S40OrientationVariantTests
{
    private const int Size = 32;
    private const int Bpp = 4;

    [Fact]
    public void 変種は8個で先頭はidentityの単体pHashと一致し決定的()
    {
        var grid = StructuredGrid();

        var variants = PHashOrientations.ComputeAll(grid);
        Assert.Equal(8, variants.Count);
        Assert.Equal(PerceptualHash.Compute(grid), variants[0]); // [0]=identity は OC-14 レシピと一致
        Assert.All(variants, v => Assert.Matches("^[0-9a-f]{16}$", v));

        // 決定的(INV-012): 再計算で同値
        Assert.Equal(variants, PHashOrientations.ComputeAll(grid));
    }

    [Fact]
    public void 各変換を施した格子の単体pHashは対応する変種と一致する_距離0検出()
    {
        var grid = StructuredGrid();
        var variants = PHashOrientations.ComputeAll(grid);

        // 仕様 §2.10.1a の順序: [1]=rotate90(時計回り) [2]=rotate180 [3]=rotate270
        // [4]=flipH [5]=flipV [6]=transpose [7]=transverse(独立実装で交差検証)
        for (var kind = 0; kind < 8; kind++)
        {
            var transformed = TransformIndependent(grid, kind);
            var hash = PerceptualHash.Compute(transformed);
            Assert.Equal(variants[kind], hash); // 変種 [k] = 変換 k の pHash → 距離 0 で検出される
            Assert.Equal(0, HammingDistance.Between(hash, variants[kind]));
        }
    }

    /// <summary>固定の非対称格子(水平勾配+左上の明矩形+下端の暗帯)。全 8 変種が意味を持つよう非対称。</summary>
    private static byte[] StructuredGrid()
    {
        var grid = new byte[Size * Size * Bpp];
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var v = (byte)(x * 8 % 256);
                if (x < 12 && y < 6)
                {
                    v = 255;
                }
                else if (y >= 26)
                {
                    v = (byte)(v / 3);
                }

                var offset = ((y * Size) + x) * Bpp;
                grid[offset] = v;     // B
                grid[offset + 1] = v; // G
                grid[offset + 2] = v; // R
                grid[offset + 3] = 255;
            }
        }

        return grid;
    }

    /// <summary>D4 変換の独立実装(製品コードの Transform に依存しない交差検証)。dst(x,y) ← src(sx,sy)。</summary>
    private static byte[] TransformIndependent(byte[] src, int kind)
    {
        var dst = new byte[src.Length];
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var (sx, sy) = kind switch
                {
                    0 => (x, y),                       // identity
                    1 => (y, Size - 1 - x),            // rotate90(時計回り)
                    2 => (Size - 1 - x, Size - 1 - y), // rotate180
                    3 => (Size - 1 - y, x),            // rotate270
                    4 => (Size - 1 - x, y),            // flipH(左右反転)
                    5 => (x, Size - 1 - y),            // flipV(上下反転)
                    6 => (y, x),                       // transpose(主対角転置)
                    _ => (Size - 1 - y, Size - 1 - x), // transverse(反対角転置)
                };
                Array.Copy(src, ((sy * Size) + sx) * Bpp, dst, ((y * Size) + x) * Bpp, Bpp);
            }
        }

        return dst;
    }
}
