using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;

namespace ViewPrism2.Core.Services.Repair;

/// <summary>
/// criteria 条件検索サービス(M-CRITERIA-024 / REQ-068、仕様 §2.11.1)。
/// 同一コレクション(sync_folder)の画像を取得し <see cref="CriteriaMatcher"/> で絞り込む。
/// 用途別 statusTargets(単体検索={Normal} / relink 候補探索={Pending,Normal})は呼び出し側が渡す。
/// 結果は安定順(relative_path 昇順・同値 id 昇順)。空条件は空列(全件を返さない)。
/// </summary>
public sealed class CriteriaSearchService
{
    private readonly IImageRepository _images;

    public CriteriaSearchService(IImageRepository images)
    {
        _images = images;
    }

    /// <summary>
    /// コレクション内を条件検索し、一致した画像レコードを安定順で返す。
    /// </summary>
    public async Task<IReadOnlyList<ImageRecord>> SearchAsync(
        string collectionId,
        SearchCriteria c,
        IReadOnlySet<ImageStatus> statusTargets,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(collectionId);
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(statusTargets);

        ct.ThrowIfCancellationRequested();

        var images = await _images.GetByFolderAsync(collectionId).ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();

        var matchedIds = CriteriaMatcher.Match(images, c, statusTargets);

        // CriteriaMatcher が返した id 列(安定順)を保ったままレコードへ写像する
        var byId = images.ToDictionary(i => i.Id, StringComparer.Ordinal);
        return matchedIds.Select(id => byId[id]).ToList();
    }
}
