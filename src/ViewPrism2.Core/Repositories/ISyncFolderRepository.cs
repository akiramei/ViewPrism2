using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;

namespace ViewPrism2.Core.Repositories;

/// <summary>同期フォルダの永続化(M-DB-007。インターフェースは Core、実装は Infrastructure)。</summary>
public interface ISyncFolderRepository
{
    /// <summary>登録する。path の重複(大文字小文字無視)は <see cref="ErrorCode.DuplicateFolderPath"/>(REQ-010)。</summary>
    Task<Result> AddAsync(SyncFolder folder);

    Task<SyncFolder?> GetByIdAsync(string id);

    /// <summary>path の case-insensitive 一致で取得する(INV-005)。</summary>
    Task<SyncFolder?> GetByPathAsync(string path);

    Task<IReadOnlyList<SyncFolder>> GetAllAsync();

    Task UpdateAsync(SyncFolder folder);

    /// <summary>削除する。配下 images は FK CASCADE で連鎖削除(仕様 §2.0)。</summary>
    Task DeleteAsync(string id);

    /// <summary>last_scan を更新する(REQ-015 手順 6)。</summary>
    Task UpdateLastScanAsync(string id, string lastScan);
}
