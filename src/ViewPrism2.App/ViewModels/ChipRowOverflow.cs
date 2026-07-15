using Avalonia;

namespace ViewPrism2.App.ViewModels;

/// <summary>
/// チップ行の容量・overflow の状態計算(ECO-091・IMG-023A=A-b/B=B-a 裁定・CAD VC-IMG-9/VC-WORK-2)。
/// 画像タブ/作業タブは同一意味論の同期実装(E-BOM 双方向宣言=ECO-090)— 折畳みの意味論は
/// 本クラスに単一実装し、実測供給(実描画矩形)は各 View の責務(ECO-027 流儀)。
/// UI 部品の共有(LabeledChipStrip 級)は golden 後の DRY 判断(裁定 §7)= ここでは行わない。
/// </summary>
public static class ChipRowOverflow
{
    /// <summary>通常表示の最大行数(IMG-023A=A-b: 固定クロムの高さ有界・行内スクロールなし)。</summary>
    public const int MaxRows = 2;

    /// <summary>
    /// 行判定の許容差。「ほか N 件」ボタンはチップと高さ僅差で同一行でも Y が数 px ずれる
    /// (CAD mock の実測折畳みで実証済みの罠 — 丸め判定だと 3 行目と誤認し過剰折畳みする)。
    /// </summary>
    public const double RowTolerance = 6;

    /// <summary>チップ間ギャップ(mock gap:8px の転写・WrapPanel LineSpacing と同値)。</summary>
    public const double Gap = 8;

    /// <summary>全チップ表示時の実描画矩形から、2 行に収める可視件数を返す。null=折畳み不要(2 行以内)。</summary>
    public static int? ComputeVisibleCount(IReadOnlyList<Rect> chipRects, double panelWidth, double moreButtonWidth)
    {
        if (chipRects.Count == 0) return null;
        var tops = RowTops(chipRects.Select(r => r.Y));
        if (tops.Count <= MaxRows) return null;

        int RowOf(Rect r) => RowIndex(tops, r.Y);
        var k = 0;
        for (; k < chipRects.Count; k++)
        {
            if (RowOf(chipRects[k]) >= MaxRows) break;
        }
        // 2 行目末尾に「ほか N 件」ボタンの席を確保(収まらない間は 1 つずつ手前へ)
        while (k > 1 && RowOf(chipRects[k - 1]) == MaxRows - 1 &&
               chipRects[k - 1].Right + Gap + moreButtonWidth > panelWidth)
        {
            k--;
        }
        return Math.Max(1, k);
    }

    /// <summary>
    /// 折畳み後の実描画(チップ+「ほか N 件」)が契約(最大 2 行)を満たすか検証し、
    /// 超過していれば 1 減らした可視件数を返す(収束パス)。null=収束済み。
    /// </summary>
    public static int? VerifyFolded(IReadOnlyList<Rect> chipRects, Rect? moreButtonRect, int currentVisible)
    {
        if (currentVisible <= 1) return null;
        var ys = chipRects.Select(r => r.Y).ToList();
        if (moreButtonRect is { } m) ys.Add(m.Y);
        return RowTops(ys).Count > MaxRows ? currentVisible - 1 : null;
    }

    /// <summary>
    /// 折畳み時の優先配置(IMG-023A: active と「クリア」は通常領域から消えない。優先配置後は元順=定義順)。
    /// 非折畳み時は呼ばない(表示順は元のまま=1 行時の視覚不変)。
    /// </summary>
    public static IReadOnlyList<ChipVM> Prioritize(IReadOnlyList<ChipVM> chips)
    {
        var clear = chips.Where(c => c.IsNeutral).ToList();
        var active = chips.Where(c => !c.IsNeutral && c.IsActive).ToList();
        var rest = chips.Where(c => !c.IsNeutral && !c.IsActive).ToList();
        return [.. clear, .. active, .. rest];
    }

    private static List<double> RowTops(IEnumerable<double> ys)
    {
        var tops = new List<double>();
        foreach (var y in ys.OrderBy(v => v))
        {
            if (tops.Count == 0 || y - tops[^1] > RowTolerance) tops.Add(y);
        }
        return tops;
    }

    private static int RowIndex(List<double> tops, double y)
    {
        for (var i = tops.Count - 1; i >= 0; i--)
        {
            if (y >= tops[i] - RowTolerance) return i;
        }
        return 0;
    }
}

/// <summary>「ほか N 件」エントリ(チップ行 WrapPanel 内の末尾要素・ECO-091・VC-IMG-9)。</summary>
public sealed class ChipMoreVM(int hiddenCount, string label)
{
    public int HiddenCount { get; } = hiddenCount;
    public string Label { get; } = label;
}
