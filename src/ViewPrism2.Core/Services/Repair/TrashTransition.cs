using ViewPrism2.Core.Models;

namespace ViewPrism2.Core.Services.Repair;

/// <summary>
/// トラッシュ復元の状態遷移判断(M-TRASH-026 / OC-21、仕様 §2.11.3、T6/T7)。
/// 純粋関数(固定オラクルが直接呼ぶ)。物理ファイルの存在有無のみで復元後 status を決める。
/// </summary>
public static class TrashTransition
{
    /// <summary>
    /// deleted 画像の復元後 status を決める(INV-013 幽霊 normal 防止):
    /// 物理ファイルが存在すれば Normal(T6)、不在なら Missing(T7)。
    /// </summary>
    public static ImageStatus ResolveRestore(bool fileExists)
        => fileExists ? ImageStatus.Normal : ImageStatus.Missing;
}
