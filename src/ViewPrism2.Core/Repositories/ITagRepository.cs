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

    /// <summary>一括解除。単一トランザクション・冪等(REQ-027)。</summary>
    Task UntagImagesAsync(IReadOnlyList<string> imageIds, string tagId);

    Task<IReadOnlyList<ImageTag>> GetImageTagsAsync(string imageId);

    /// <summary>タグ id → 使用数(COUNT(DISTINCT image_id)、REQ-029)。</summary>
    Task<IReadOnlyDictionary<string, int>> GetUsageCountsAsync();
}
