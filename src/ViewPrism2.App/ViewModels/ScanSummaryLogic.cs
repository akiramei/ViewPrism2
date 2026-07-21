namespace ViewPrism2.App.ViewModels;

/// <summary>missing 率の 3 色注意段(REQ-100。情報表示のみ=操作は制限しない)。</summary>
public enum MissingRateTier
{
    Green,
    Yellow,
    Red,
}

/// <summary>確認強度の段(REQ-100/CAD SCAN-002)。</summary>
public enum ScanConfirmTier
{
    /// <summary>変更合計 &lt; 100: 通常の適用操作(破棄+適用の 2 ボタン)。</summary>
    Normal,

    /// <summary>100 ≦ 変更合計 &lt; 1,000: [変更内容を確認] を追加した 3 ボタン。</summary>
    WithDetail,

    /// <summary>1,000 ≦ 変更合計: 適用前に確認ダイアログ(CMP-011・primary)。</summary>
    ConfirmDialog,
}

/// <summary>
/// スキャン結果確認の決定論ロジック(ECO-130・E-UI-SCANSTAGE-048)。描画から独立して unit 検査可能。
/// 閾値は CAD scan_summary.md SCAN-002 の裁定値(グリーン &lt;1% ≦ イエロー &lt;50% ≦ レッド/確認強度 100/1,000)。
/// </summary>
public static class ScanSummaryLogic
{
    /// <summary>グリーン &lt;1% ≦ イエロー &lt;50% ≦ レッド。分母 0(空コレクション)はグリーンへ縮退。</summary>
    public static MissingRateTier RateTier(int missingTotal, int managedTotal)
    {
        if (managedTotal <= 0 || missingTotal <= 0)
        {
            return MissingRateTier.Green;
        }

        // 百分率の閾値比較は整数演算で行う(浮動小数の境界ぶれを避ける)
        var percent100 = missingTotal * 100L;
        if (percent100 < managedTotal * 1L)
        {
            return MissingRateTier.Green;
        }

        return percent100 < managedTotal * 50L ? MissingRateTier.Yellow : MissingRateTier.Red;
    }

    /// <summary>&lt;100=通常/&lt;1,000=内訳確認導線を追加/以上=適用前確認ダイアログ(SCAN-002)。</summary>
    public static ScanConfirmTier ConfirmTier(int totalChanges)
        => totalChanges switch
        {
            < 100 => ScanConfirmTier.Normal,
            < 1000 => ScanConfirmTier.WithDetail,
            _ => ScanConfirmTier.ConfirmDialog,
        };
}
