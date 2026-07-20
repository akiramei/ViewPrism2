using Avalonia.Media;

namespace ViewPrism2.Tests;

/// <summary>
/// ECO-122: UI 部品表(../ViewPrismUI/docs/04_component_registry.md)の契約値写像。
/// **lint・視覚 probe の照合先は本ファイルに一元化する**(maintainer 決定済み方針 2026-07-20・柱5)。
/// 物理形= ECO-122 §4 案b: CAD Markdown の越境パース(脆い・CI 可搬性欠く)と部品表側の機械可読
/// ブロック新設(CAD 改稿=スコープ拡大)を避け、出典コメントつき定数写像で一元化
/// (前例= OrganizeCriteria 共有写像・ECO-055=転写ドリフト防止の同型)。
///
/// 運用契約:
/// - 部品表(CAD リポ成果物・VP2 側で編集しない=恒久運用柱4)を改版したら本ファイルへ転写する。
///   各定数の出典= 部品表の部品 ID+節。値の変更は部品契約の改版(maintainer 裁定)に随伴する時のみ。
/// - **登載基準= 値の一致でなくトークン所掌の一致**: 部品表トークンと同値でも、画面 CAD(VC 行)が
///   独自に所掌する値(例: 並び替えメニューの種別チップ配色= VC-FL-1)は登載しない(意味の混同=
///   トークン誤写像を防ぐ。CMP-010 の教訓= 意味が異なれば同型でも別部品)。
/// - **登載範囲= Standard 部品(CMP-006/007/010)の機械検査可能な基幹値**(ECO-122 decide)。
///   参照者(probe/lint)が未着の値も部品単位で登載する(CMP-010/007 の一部が該当)= 次の probe が
///   生値を再発明せずここから引く。画面 CAD 所掌の値は登載しない(上記基準)。
/// </summary>
internal static class RegistryContract
{
    // ---- 基礎トークン(色)— 部品表「基礎トークン(T)/色」 ----

    /// <summary>color.accent `#2f6bed`: 主要青(主 CTA 塗り・選択リング/チェック・アクティブドット)。</summary>
    public static readonly Color ColorAccent = Color.Parse("#2F6BED");

    /// <summary>color.accent.text-strong `#2459cf`: アクティブ行の太字ラベル(VC-FL-1④)。</summary>
    public static readonly Color ColorAccentTextStrong = Color.Parse("#2459CF");

    /// <summary>color.text.secondary `#5b6473`: 二次文字・アイコン stroke。</summary>
    public static readonly Color ColorTextSecondary = Color.Parse("#5B6473");

    // ---- CMP-006 PopoverMenu(C・Standard= REG-C3 裁定 2026-07-20)----
    // インスタンス契約: 幅は各インスタンスの契約値として固定・同型インスタンスの面間複製は同値必須。

    /// <summary>表示軸メニュー幅(ECO-123 で 260→240 是正済み)。</summary>
    public const double MenuWidthAxis = 240;

    /// <summary>⋯メニュー幅(ECO-123 で作業タブ 200→208 是正済み=画像タブと同値)。</summary>
    public const double MenuWidthMore = 208;

    /// <summary>並び替えメニュー幅(VC-FL-1①)。</summary>
    public const double MenuWidthSort = 252;

    /// <summary>移動先メニュー幅(作業タブ)。</summary>
    public const double MenuWidthMove = 240;

    /// <summary>⋯メニュー行高(契約 42。既存行≒37 は golden 許容済み= As-Built 乖離②・SRC-011 同便で収束)。</summary>
    public const double MenuRowHeightMore = 42;

    /// <summary>並び替えメニュー候補行高(VC-FL-1③)。</summary>
    public const double MenuRowHeightSort = 38;

    // ---- CMP-007 GridSelectionIndicator(C・Standard= REG-C1 裁定 2026-07-20 で転写訂正)----
    // ordered= 選択系モードの既定(両タブ対称)・check= ファイル操作モード限定の裁定例外+ゴミ箱。
    // 塗り= ColorAccent(共有トークン)。以下は check バリアント固有値。

    /// <summary>check ボックス寸法(左上 7px・22×22)。</summary>
    public const double CheckBoxSize = 22;

    /// <summary>check ボックス radius 7。</summary>
    public const double CheckBoxRadius = 7;

    /// <summary>未選択時の枠 `#cbd1da`(白 85% 地とセット)。</summary>
    public static readonly Color CheckUncheckedBorder = Color.Parse("#CBD1DA");

    // ---- CMP-010 DisplayCountBadge(S・Standard= REG-C2 裁定 2026-07-20・実装が原器)----
    // 状態情報としての件数表示(実行系 CMP-003 とは意味が異なる別部品)。

    /// <summary>中立グレーピルの地 `#F4F6FA`。</summary>
    public static readonly Color DisplayBadgeBg = Color.Parse("#F4F6FA");

    /// <summary>文字= color.text.faint `#AAB1BD`。</summary>
    public static readonly Color DisplayBadgeFg = Color.Parse("#AAB1BD");

    /// <summary>radius 9(ピル形。実行系 CMP-003 の radius.badge 6 と視覚区別)。</summary>
    public const double DisplayBadgeRadius = 9;
}
