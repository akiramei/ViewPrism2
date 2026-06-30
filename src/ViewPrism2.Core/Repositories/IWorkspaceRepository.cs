using ViewPrism2.Core.Models;

namespace ViewPrism2.Core.Repositories;

/// <summary>
/// 作業スペースの永続化(ECO-020 / M-DB-007。インターフェースは Core、実装は Infrastructure)。
/// 原子性が要る操作(デフォルト回転=INV-W1 / 移動=INV-W5)はリポジトリが単一トランザクションで担保する。
/// </summary>
public interface IWorkspaceRepository
{
    /// <summary>全作業スペースを seq 降順(新しいほど上=スタックのトップ・デフォルトが最上段)で、所属 normal 画像数つきで返す(INV-W2)。</summary>
    Task<IReadOnlyList<WorkspaceWithCount>> GetAllWithNormalCountsAsync();

    Task<Workspace?> GetByIdAsync(string id);

    /// <summary>デフォルト作業スペース(is_default=1)を返す。未シードなら null。</summary>
    Task<Workspace?> GetDefaultAsync();

    /// <summary>現在の最大 seq。空なら 0。</summary>
    Task<int> GetMaxSeqAsync();

    /// <summary>1 件追加する(回転を伴わない単純追加=初回シード用)。</summary>
    Task AddAsync(Workspace workspace);

    /// <summary>名前を更新する(サービス層で非デフォルト・空フォールバックを検証済みの前提)。</summary>
    Task RenameAsync(string id, string name);

    /// <summary>
    /// 作業スペースを削除する: 単一トランザクションで所属(workspace_images)を除去し
    /// workspaces 行を削除する。画像自体は物理非破壊(INV-W4)。
    /// サービス層で非デフォルト・存在を検証済みの前提。
    /// </summary>
    Task DeleteAsync(string id);

    /// <summary>
    /// デフォルト回転(ACT-0074・INV-W1): 単一トランザクションで、旧デフォルトを
    /// <paramref name="oldDefaultNewName"/> へ改名し is_default=0 へ降格し、新デフォルト
    /// <paramref name="newDefault"/>(is_default=1)を追加する。常にデフォルトが厳密に 1 つを保つ。
    /// </summary>
    Task CreateRotatingDefaultAsync(Workspace newDefault, string oldDefaultId, string oldDefaultNewName);

    /// <summary>作業スペースの所属画像のうち status=normal のものを安定順(added_at 昇順・同値 id 昇順)で返す(INV-W2)。</summary>
    Task<IReadOnlyList<ImageRecord>> GetNormalImagesAsync(string workspaceId);

    /// <summary>作業スペースの所属画像のうち status=deleted のものを file_name 昇順で返す(ECO-021/β-4: ゴミ箱 popup・件数バッジ用)。</summary>
    Task<IReadOnlyList<ImageRecord>> GetDeletedImagesAsync(string workspaceId);

    /// <summary>画像を作業スペースへ和集合追加する(重複は無視=集合・INV-W2)。物理非破壊(INV-W4)。</summary>
    Task AddImagesAsync(string workspaceId, IReadOnlyList<string> imageIds, string addedAt);

    /// <summary>
    /// 画像を作業スペース間で移動する(ACT-0077・INV-W5): 単一トランザクションで、
    /// 元スペースから除去し移動先へ和集合追加する。物理非破壊(INV-W4)。
    /// </summary>
    Task MoveImagesAsync(string fromWorkspaceId, string toWorkspaceId, IReadOnlyList<string> imageIds, string addedAt);
}
