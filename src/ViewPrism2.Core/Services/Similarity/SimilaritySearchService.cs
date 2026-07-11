using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;

namespace ViewPrism2.Core.Services.Similarity;

/// <summary>
/// 類似検索エンジン+特徴量/類似度キャッシュ協調(M-SIMSEARCH-021 / E-SIMSEARCH-032 / OC-16・OC-18)。
/// 基準画像 1 枚に対し、同一コレクション内(REQ-053)の status=Normal 画像のみを候補とし
/// (基準自身は除外)、各候補との pHash 類似度を算出して閾値以上(≧)を Score 降順・同値 id 昇順で返す。
/// 候補の status フィルタ(Normal 限定)はキャッシュ参照より先に適用する(仕様 §2.10.4)。
/// 無効化は内容ベース(file_size/modified_date/hash)+adapter 世代(P-09)+変種欠落(REQ-084)で再計算
/// +特徴量再計算で関与ペア連鎖削除。ペア距離は小 id の identity × 大 id の全変種の最小(REQ-084/ECO-048)。
/// </summary>
public sealed class SimilaritySearchService
{
    private readonly ISyncFolderRepository _folders;
    private readonly IImageRepository _images;
    private readonly IImageFeatureRepository _features;
    private readonly IImageSimilarityRepository _similarities;
    private readonly IPHashImageReader _reader;
    private readonly IClock _clock;

    public SimilaritySearchService(
        ISyncFolderRepository folders,
        IImageRepository images,
        IImageFeatureRepository features,
        IImageSimilarityRepository similarities,
        IPHashImageReader reader,
        IClock clock)
    {
        _folders = folders;
        _images = images;
        _features = features;
        _similarities = similarities;
        _reader = reader;
        _clock = clock;
    }

    /// <summary>
    /// 基準画像と同一コレクション内の Normal 候補から閾値以上の類似画像を返す(OC-16)。
    /// </summary>
    /// <param name="baseImageId">基準画像 id。</param>
    /// <param name="threshold">閾値(整数・50〜100 を想定、≧ で含める)。</param>
    public async Task<IReadOnlyList<SimilarResult>> FindSimilarAsync(
        string baseImageId,
        int threshold,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        var baseImage = await _images.GetByIdAsync(baseImageId).ConfigureAwait(false);
        if (baseImage is null || baseImage.Status != ImageStatus.Normal)
        {
            return [];
        }

        // OC-16 後方互換: 明示 scope のない呼び出しは同一コレクション全体。
        var folderImages = await _images.GetByFolderAsync(baseImage.SyncFolderId).ConfigureAwait(false);
        return await FindSimilarCoreAsync(baseImage, threshold, folderImages, progress, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// ECO-062/REQ-087: 検索 surface が確定した閲覧コンテキストだけを候補として検索する。
    /// scope 外・非 Normal・別コレクション・基準自身は feature/cache 参照より前に除外する。
    /// </summary>
    public async Task<IReadOnlyList<SimilarResult>> FindSimilarInScopeAsync(
        string baseImageId,
        int threshold,
        IReadOnlyCollection<ImageRecord> scopeCandidates,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        var baseImage = await _images.GetByIdAsync(baseImageId).ConfigureAwait(false);
        if (baseImage is null || baseImage.Status != ImageStatus.Normal)
        {
            return [];
        }

        return await FindSimilarCoreAsync(baseImage, threshold, scopeCandidates, progress, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<SimilarResult>> FindSimilarCoreAsync(
        ImageRecord baseImage,
        int threshold,
        IReadOnlyCollection<ImageRecord> scopeCandidates,
        IProgress<int>? progress,
        CancellationToken ct)
    {
        // フィルタ先行(REQ-087): scope 候補にも Core 境界を再適用し、重複 id も 1 回だけ処理する。
        var candidates = scopeCandidates
            .Where(i => i.Status == ImageStatus.Normal
                && string.Equals(i.SyncFolderId, baseImage.SyncFolderId, StringComparison.Ordinal)
                && !string.Equals(i.Id, baseImage.Id, StringComparison.Ordinal))
            .DistinctBy(i => i.Id, StringComparer.Ordinal)
            .ToList();

        if (candidates.Count == 0)
        {
            progress?.Report(100);
            return [];
        }

        var folder = await _folders.GetByIdAsync(baseImage.SyncFolderId).ConfigureAwait(false);
        if (folder is null)
        {
            return [];
        }

        // 基準画像の特徴量(取得・必要なら再計算)
        var baseFeature = await GetOrComputeFeatureAsync(baseImage, folder.Path, ct).ConfigureAwait(false);
        if (baseFeature is null)
        {
            return []; // 基準の pHash が取れない(壊れた画像)→ 候補なし
        }

        var results = new List<SimilarResult>();
        var total = candidates.Count;
        var done = 0;
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();

            var score = await ComputePairScoreAsync(
                baseImage, baseFeature, candidate, folder.Path, ct).ConfigureAwait(false);
            if (score is { } s && s >= threshold)
            {
                results.Add(new SimilarResult { ImageId = candidate.Id, Score = s });
            }

            done++;
            progress?.Report(total == 0 ? 100 : done * 100 / total);
        }

        // Score 降順・同値は id 昇順(序数)で安定
        return results
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.ImageId, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>基準と候補のペア類似度。キャッシュ参照→無ければ計算し保存(フィルタは呼び出し側で適用済み)。</summary>
    private async Task<int?> ComputePairScoreAsync(
        ImageRecord baseImage, ImageFeature baseFeature, ImageRecord candidate, string folderPath, CancellationToken ct)
    {
        // キャッシュヒット: 同一ペアの 2 回目は再計算しない
        var cached = await _similarities.GetAsync(baseImage.Id, candidate.Id).ConfigureAwait(false);
        if (cached is not null)
        {
            return cached.SimilarityScore;
        }

        var candidateFeature = await GetOrComputeFeatureAsync(candidate, folderPath, ct).ConfigureAwait(false);
        if (candidateFeature is null)
        {
            return null; // 候補の pHash が取れない(壊れた画像)→ スキップ
        }

        // ペア距離(REQ-084・仕様 §2.10.4): 序数比較で小さい id の identity pHash × 大きい id の
        // 全変種の最小距離。役割が id 順で決まるため探索方向によらず対称=ペア正規化キャッシュと整合。
        var (lo, hi) = string.CompareOrdinal(baseImage.Id, candidate.Id) <= 0
            ? (baseFeature, candidateFeature)
            : (candidateFeature, baseFeature);
        var distance = PairDistance(lo.PHash, hi);
        var score = SimilarityScore.FromDistance(distance);
        await _similarities.UpsertAsync(baseImage.Id, candidate.Id, score, _clock.UtcNowIso())
            .ConfigureAwait(false);
        return score;
    }

    /// <summary>
    /// identity pHash と相手特徴量の全変種(なければ identity)の最小ハミング距離(REQ-084)。
    /// 変種 [0]=identity を含むため identity 同士の距離を上回らない(単調拡張=既存検出の純増)。
    /// </summary>
    private static int PairDistance(string loPhash, ImageFeature hi)
    {
        var best = HammingDistance.Between(loPhash, hi.PHash);
        if (hi.PhashVariants is { Length: > 0 } joined)
        {
            foreach (var variant in joined.Split(','))
            {
                if (variant.Length == 16)
                {
                    best = Math.Min(best, HammingDistance.Between(loPhash, variant));
                }
            }
        }

        return best;
    }

    /// <summary>
    /// 画像の特徴量を取得する。特徴量が無い、または stale(内容変化/adapter 世代不一致/変種欠落 —
    /// 仕様 §2.10.3)の場合は再計算→Upsert→関与する類似度ペアを連鎖削除する(OC-18)。
    /// </summary>
    private async Task<ImageFeature?> GetOrComputeFeatureAsync(ImageRecord image, string folderPath, CancellationToken ct)
    {
        var feature = await _features.GetAsync(image.Id).ConfigureAwait(false);
        if (feature is not null && IsFresh(feature, image))
        {
            return feature;
        }

        ct.ThrowIfCancellationRequested();

        var absolutePath = ToAbsolutePath(folderPath, image.RelativePath);
        string? phash;
        string? variantsJoined = null;
        if (_reader.SupportsOrientationVariants)
        {
            // 変種対応 reader: 1 回のデコードで 8 変種([0]=identity)を得る(REQ-084)
            var variants = await _reader.ComputePHashVariantsAsync(absolutePath).ConfigureAwait(false);
            if (variants is null || variants.Count == 0)
            {
                return null;
            }

            phash = variants[0];
            variantsJoined = string.Join(',', variants);
        }
        else
        {
            phash = await _reader.ComputePHashAsync(absolutePath).ConfigureAwait(false);
            if (phash is null)
            {
                return null;
            }
        }

        var refreshed = new ImageFeature
        {
            ImageId = image.Id,
            PHash = phash,
            HashAdapter = _reader.AdapterId, // P-09: どの adapter 世代で計算したかを刻む
            FileSize = image.FileSize,
            ModifiedDate = image.ModifiedDate,
            Hash = image.Hash,
            LastCalculated = _clock.UtcNowIso(),
            PhashVariants = variantsJoined,
        };
        await _features.UpsertAsync(refreshed).ConfigureAwait(false);

        // 連鎖無効化: 再計算した画像が関与する古い類似度を削除(次回検索で再計算・再保存)
        await _similarities.DeleteInvolvingAsync(image.Id).ConfigureAwait(false);

        return refreshed;
    }

    /// <summary>
    /// 記録された特徴量が再利用可能か。①内容ベース(file_size/modified_date/hash・仕様 §2.10.3)に加え、
    /// ②adapter 世代一致(P-09)を要求する。現行 reader と異なる adapter で計算された pHash は
    /// 内容が同じでも stale 扱い=再計算し、adapter をまたいだ値の混在を防ぐ(旧 DB の空 adapter も再計算)。
    /// ③現行 reader が変種対応の場合は変種の存在も要求する(REQ-084 — 旧レコードの自動アップグレード)。
    /// </summary>
    private bool IsFresh(ImageFeature feature, ImageRecord image)
        => string.Equals(feature.HashAdapter, _reader.AdapterId, StringComparison.Ordinal)
            && feature.FileSize == image.FileSize
            && string.Equals(feature.ModifiedDate, image.ModifiedDate, StringComparison.Ordinal)
            && string.Equals(feature.Hash, image.Hash, StringComparison.Ordinal)
            && (!_reader.SupportsOrientationVariants || feature.PhashVariants is not null);

    private static string ToAbsolutePath(string folderPath, string relativePath)
        => Path.Combine(folderPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
}
