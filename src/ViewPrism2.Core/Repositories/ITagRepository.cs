using ViewPrism2.Core.Models;

namespace ViewPrism2.Core.Repositories;

/// <summary>タグと付与状態の永続化(M-DB-007。インターフェースは Core、実装は Infrastructure)。</summary>
public interface ITagRepository
{
    Task AddAsync(Tag tag);

    Task<Tag?> GetByIdAsync(string id);

    /// <summary>名前の完全一致(case-sensitive、REQ-021)で取得する。</summary>
    Task<Tag?> GetByNameAsync(string name);

    Task<IReadOnlyList<Tag>> GetAllAsync();

    Task UpdateAsync(Tag tag);

    /// <summary>削除する。カスケードは FK で実現(REQ-028。アプリ側で再実装しない)。</summary>
    Task DeleteAsync(string id);

    Task<TextualTagSettings?> GetTextualSettingsAsync(string tagId);

    Task UpsertTextualSettingsAsync(TextualTagSettings settings);

    Task<NumericTagSettings?> GetNumericSettingsAsync(string tagId);

    Task UpsertNumericSettingsAsync(NumericTagSettings settings);

    /// <summary>付与の UPSERT(REQ-026 / INV-003): 再付与は値上書き、行は増えない。</summary>
    Task UpsertImageTagAsync(ImageTag imageTag);

    /// <summary>解除(行削除)。無い行の解除はエラーにしない(冪等、REQ-026)。</summary>
    Task RemoveImageTagAsync(string imageId, string tagId);

    /// <summary>一括付与。単一トランザクション、失敗時全ロールバック(REQ-027 / INV-006)。</summary>
    Task TagImagesAsync(IReadOnlyList<string> imageIds, string tagId, string? value);

    /// <summary>
    /// 画像ごとに異なる値での一括付与(REQ-046 の連番適用)。UPSERT 意味論(REQ-026)。
    /// 単一トランザクション、失敗時全ロールバック(REQ-027 / INV-006)。
    /// </summary>
    Task TagImagesWithValuesAsync(string tagId, IReadOnlyList<(string ImageId, string? Value)> assignments);

    /// <summary>一括解除。単一トランザクション・冪等(REQ-027)。</summary>
    Task UntagImagesAsync(IReadOnlyList<string> imageIds, string tagId);

    Task<IReadOnlyList<ImageTag>> GetImageTagsAsync(string imageId);

    /// <summary>全付与行(条件評価 OC-1 の入力構築用)。</summary>
    Task<IReadOnlyList<ImageTag>> GetAllImageTagsAsync();

    /// <summary>
    /// ECO-064/IMG-019: 選択 collection の画像に付与されたタグ行だけを読む。
    /// 起動時に別 collection の image_tags を materialize しないための content 境界。
    /// </summary>
    Task<IReadOnlyList<ImageTag>> GetImageTagsByFolderAsync(string syncFolderId, CancellationToken ct = default);

    /// <summary>
    /// ECO-140: 統合裁定の保持タグ件数用。DB 境界で対象を pending∪missing に限定する。
    /// normal 限定の表示系 API と混用しない。
    /// </summary>
    Task<IReadOnlyList<ImageTag>> GetIntegrityReviewImageTagsByFolderAsync(
        string syncFolderId, CancellationToken ct = default);

    /// <summary>タグ id → 使用数(COUNT(DISTINCT image_id)、REQ-029)。</summary>
    Task<IReadOnlyDictionary<string, int>> GetUsageCountsAsync();

    /// <summary>
    /// タグの被参照集計(REQ-082 / TAG-008 裁定: 削除可否判定用)。
    /// 子タグ(tags.parent_id)は「使用」でないため含めない(4a 裁定: ルート化 SET NULL 存続)。
    /// </summary>
    Task<TagUsageRefs> GetUsageRefsAsync(string tagId);
}

/// <summary>
/// タグの被参照集計(REQ-082 / TAG-008 裁定)。いずれかが正なら「使用中」=削除拒否。
/// </summary>
public sealed record TagUsageRefs(int ImageTagCount, int HierarchyNodeCount, int ConditionCount)
{
    public bool InUse => ImageTagCount > 0 || HierarchyNodeCount > 0 || ConditionCount > 0;
}
