namespace ViewPrism2.Core.Services.Viewer;

/// <summary>
/// タグ制御ナビゲーション計算器(OC-25・仕様 §2.12.4)。タグ制御 ON の見開きナビゲーションを
/// プラン見開き単位で行う(±1 クランプ)。モード復元の画像↔見開き解決を供給する。
/// ECO-022 新設・純粋計算 Core(M-TAGCTRL-028)。
/// </summary>
/// <remarks>
/// SHIFT/pageTurnMode は固定プランに概念上適用しない(仕様 §2.12.4 M-2)。常にプラン見開き 1 つ分送る。
/// 矢印キーの右開き反転・クリック半面は画面方向の規則であり §2.9 と同一(プランに非依存)のため
/// 「次へ/前へ」の論理操作のみをここで扱う。
/// </remarks>
public static class TagControlNavigator
{
    /// <summary>次へ(プラン見開き index を +1・末尾でクランプ=変化なし)。</summary>
    public static int Next(int curSpread, int spreadCount)
    {
        if (spreadCount <= 0)
        {
            return 0;
        }

        var clamped = Math.Clamp(curSpread, 0, spreadCount - 1);
        return Math.Min(clamped + 1, spreadCount - 1);
    }

    /// <summary>前へ(プラン見開き index を -1・先頭でクランプ=変化なし)。</summary>
    public static int Prev(int curSpread, int spreadCount)
    {
        if (spreadCount <= 0)
        {
            return 0;
        }

        var clamped = Math.Clamp(curSpread, 0, spreadCount - 1);
        return Math.Max(clamped - 1, 0);
    }

    /// <summary>
    /// 画像 index を含むプラン見開き index を解決する(モード復元用。仕様 §2.12.4 画像→見開き)。
    /// 対応が無い(skip 等)場合は 0 へフォールバック(空プランも 0)。
    /// </summary>
    public static int SpreadOfImage(TagControlPlan plan, int imageIndex)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.ImageToSpread.TryGetValue(imageIndex, out var spread) ? spread : 0;
    }

    /// <summary>
    /// プラン見開きの canonical 現在画像 index を返す(仕様 §2.12.4 M-1: 先読み面優先)。
    /// プランが空、または範囲外の場合は -1(現在画像なし)。
    /// </summary>
    public static int CanonicalImage(TagControlPlan plan, int curSpread)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Spreads.Count == 0)
        {
            return -1;
        }

        var clamped = Math.Clamp(curSpread, 0, plan.Spreads.Count - 1);
        return plan.Spreads[clamped].CanonicalImage;
    }
}
