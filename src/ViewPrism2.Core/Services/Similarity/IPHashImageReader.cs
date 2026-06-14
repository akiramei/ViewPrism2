namespace ViewPrism2.Core.Services.Similarity;

/// <summary>
/// 画像ファイルから pHash を計算する抽象(M-SIMSEARCH-021 / E-SIMSEARCH-032)。
/// Core 抽象。実装(PHashImageReader)は Infrastructure 側で SkiaSharp を用い
/// 32×32 BGRA を取得し <see cref="PerceptualHash.Compute"/> を呼ぶ(SkiaSharp は Infrastructure に閉じる)。
/// ThumbnailService と同じ層分離(ADR-0002 / K-SKIA v3.0)。
/// </summary>
public interface IPHashImageReader
{
    /// <summary>
    /// この pHash adapter の世代識別子(P-09)。decode 経路/レシピ/SkiaSharp 版が pHash の絶対値を
    /// 動かす変更をしたら必ず変える。永続化された特徴量(image_features.hash_adapter)に記録され、
    /// 現行 adapter と不一致なら stale 扱い=再計算される(adapter をまたいだ pHash 値の混在を防ぐ)。
    /// 例: full-decode=skia-full-decode-v1 / scaled-decode 早期縮小=skia-scaled-decode-v1。
    /// </summary>
    string AdapterId { get; }

    /// <summary>絶対パスの画像から 16hex pHash を計算する。失敗(壊れた画像)は null。</summary>
    Task<string?> ComputePHashAsync(string absoluteImagePath);
}
