using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;

namespace ViewPrism2.Core.Services.Similarity;

/// <summary>
/// 類似検索エンジン+特徴量/類似度キャッシュ協調(M-SIMSEARCH-021 / E-SIMSEARCH-032 / OC-16・OC-18)。
/// 基準画像 1 枚に対し、同一コレクション内(REQ-053)の status=Normal 画像のみを候補とし
/// (基準自身は除外)、各候補との pHash 類似度を算出して閾値以上(≧)を Score 降順・同値 id 昇順で返す。
/// 候補の status フィルタ(Normal 限定)はキャッシュ参照より先に適用する(仕様 §2.10.4)。
/// 無効化は内容ベースのみ(file_size/modified_date/hash 変化で再計算)+特徴量再計算で関与ペア連鎖削除。
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

        // 同一コレクション(REQ-053)。フィルタ先行: status=Normal・基準自身除外(仕様 §2.10.4)
        var folderImages = await _images.GetByFolderAsync(baseImage.SyncFolderId).ConfigureAwait(false);
        var candidates = folderImages
            .Where(i => i.Status == ImageStatus.Normal
                && !string.Equals(i.Id, baseImageId, StringComparison.Ordinal))
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

        // 基準画像の pHash(特徴量を取得・必要なら再計算)
        var basePhash = await GetOrComputePhashAsync(baseImage, folder.Path, ct).ConfigureAwait(false);
        if (basePhash is null)
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
                baseImage, basePhash, candidate, folder.Path, ct).ConfigureAwait(false);
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
        ImageRecord baseImage, string basePhash, ImageRecord candidate, string folderPath, CancellationToken ct)
    {
        // キャッシュヒット: 同一ペアの 2 回目は再計算しない
        var cached = await _similarities.GetAsync(baseImage.Id, candidate.Id).ConfigureAwait(false);
        if (cached is not null)
        {
            return cached.SimilarityScore;
        }

        var candidatePhash = await GetOrComputePhashAsync(candidate, folderPath, ct).ConfigureAwait(false);
        if (candidatePhash is null)
        {
            return null; // 候補の pHash が取れない(壊れた画像)→ スキップ
        }

        var distance = HammingDistance.Between(basePhash, candidatePhash);
        var score = SimilarityScore.FromDistance(distance);
        await _similarities.UpsertAsync(baseImage.Id, candidate.Id, score, _clock.UtcNowIso())
            .ConfigureAwait(false);
        return score;
    }

    /// <summary>
    /// 画像の pHash を取得する。特徴量が無い、または内容(file_size/modified_date/hash)が
    /// 記録と異なる場合は再計算→Upsert→関与する類似度ペアを連鎖削除する(内容ベース無効化、OC-18)。
    /// </summary>
    private async Task<string?> GetOrComputePhashAsync(ImageRecord image, string folderPath, CancellationToken ct)
    {
        var feature = await _features.GetAsync(image.Id).ConfigureAwait(false);
        if (feature is not null && IsFresh(feature, image))
        {
            return feature.PHash;
        }

        ct.ThrowIfCancellationRequested();

        var absolutePath = ToAbsolutePath(folderPath, image.RelativePath);
        var phash = await _reader.ComputePHashAsync(absolutePath).ConfigureAwait(false);
        if (phash is null)
        {
            return null;
        }

        await _features.UpsertAsync(new ImageFeature
        {
            ImageId = image.Id,
            PHash = phash,
            FileSize = image.FileSize,
            ModifiedDate = image.ModifiedDate,
            Hash = image.Hash,
            LastCalculated = _clock.UtcNowIso(),
        }).ConfigureAwait(false);

        // 連鎖無効化: 再計算した画像が関与する古い類似度を削除(次回検索で再計算・再保存)
        await _similarities.DeleteInvolvingAsync(image.Id).ConfigureAwait(false);

        return phash;
    }

    /// <summary>記録された特徴量が現行ファイルメタと一致するか(内容ベース無効化、仕様 §2.10.3)。</summary>
    private static bool IsFresh(ImageFeature feature, ImageRecord image)
        => feature.FileSize == image.FileSize
            && string.Equals(feature.ModifiedDate, image.ModifiedDate, StringComparison.Ordinal)
            && string.Equals(feature.Hash, image.Hash, StringComparison.Ordinal);

    private static string ToAbsolutePath(string folderPath, string relativePath)
        => Path.Combine(folderPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
}
