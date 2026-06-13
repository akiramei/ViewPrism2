namespace ViewPrism2.Core.Services.Similarity;

/// <summary>
/// 画像ファイルから pHash を計算する抽象(M-SIMSEARCH-021 / E-SIMSEARCH-032)。
/// Core 抽象。実装(PHashImageReader)は Infrastructure 側で SkiaSharp を用い
/// 32×32 BGRA を取得し <see cref="PerceptualHash.Compute"/> を呼ぶ(SkiaSharp は Infrastructure に閉じる)。
/// ThumbnailService と同じ層分離(ADR-0002 / K-SKIA v3.0)。
/// </summary>
public interface IPHashImageReader
{
    /// <summary>絶対パスの画像から 16hex pHash を計算する。失敗(壊れた画像)は null。</summary>
    Task<string?> ComputePHashAsync(string absoluteImagePath);
}
