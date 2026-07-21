using ViewPrism2.Core.Models;

namespace ViewPrism2.Core.Services.Repair;

/// <summary>
/// トラッシュ復元の状態遷移判断(M-TRASH-026 / OC-21、仕様 §2.11.3、T6'/T7)。
/// 純粋関数(固定オラクルが直接呼ぶ)。物理ファイルの存在有無のみで復元後 status を決める。
/// </summary>
public static class TrashTransition
{
    /// <summary>
    /// deleted 画像の復元後 status を決める(ECO-128・INV-013 v5.0):
    /// 物理ファイルが存在すれば Pending(T6'・復元だけで normal に戻さず未裁定へ倒す。
    /// pending 由来は <see cref="PendingOrigin.Restored"/>=TrashService が付与)、
    /// 不在なら Missing(T7・幽霊 normal 防止)。
    /// </summary>
    public static ImageStatus ResolveRestore(bool fileExists)
        => fileExists ? ImageStatus.Pending : ImageStatus.Missing;
}
