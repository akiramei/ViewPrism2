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

    /// <summary>
    /// この reader が 8 オリエンテーション変種(REQ-084 / ECO-048)を計算できるか。
    /// 既定 false(default interface method — 既存実装・テスト用 fake を無改変に保つ後方互換。
    /// R6: 固定オラクル側の fake 実装に手を入れないための折り合い。ECO-046 optional 注入と同系)。
    /// true の場合、変種欠落の永続特徴量は stale 扱いで再計算される(仕様 §2.10.3)。
    /// </summary>
    bool SupportsOrientationVariants => false;

    /// <summary>
    /// 8 オリエンテーション変種の pHash(仕様 §2.10.1a の順序・[0]=identity)を計算する。
    /// 失敗(壊れた画像)は null。既定実装は null(変種非対応 — 呼び出し側は identity にフォールバック)。
    /// </summary>
    Task<IReadOnlyList<string>?> ComputePHashVariantsAsync(string absoluteImagePath)
        => Task.FromResult<IReadOnlyList<string>?>(null);
}
