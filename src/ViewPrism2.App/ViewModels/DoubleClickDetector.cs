namespace ViewPrism2.App.ViewModels;

/// <summary>
/// ダブルクリック判定のフォールバック(REQ-041 / v1.3/ECO-002 DF-4)。
/// Avalonia の PointerPressedEventArgs.ClickCount はポインタ経路・ウィンドウ活性化・
/// 微小なポインタ移動(システムの 4px 矩形超え)で 2 に達しないことがある。
/// 「同一アイテムへの修飾なし連続クリックがダブルクリック時間内」を独自に検出して補完する。
/// 純粋ロジック(タイムスタンプ注入)として unit 検査可能にする。
/// </summary>
public sealed class DoubleClickDetector
{
    private object? _lastTarget;
    private long _lastTimestampMs;

    /// <summary>
    /// クリック 1 回を観測し、直前のクリックと合わせてダブルクリックかを返す。
    /// 修飾キー付きクリック(Ctrl/Shift)は選択操作のため対象外(状態をリセットする)。
    /// ダブルクリック成立後は状態をリセットし、3 連打が 2 回目のダブルクリックにならないようにする。
    /// </summary>
    /// <param name="target">クリックされたアイテム(参照同一性で比較)。</param>
    /// <param name="timestampMs">クリック時刻(ミリ秒。単調増加であれば基準は任意)。</param>
    /// <param name="doubleClickTimeMs">ダブルクリック許容間隔(プラットフォーム設定値)。</param>
    /// <param name="hasModifiers">Ctrl/Shift 等の修飾キー付きか。</param>
    public bool ObserveClick(object target, long timestampMs, double doubleClickTimeMs, bool hasModifiers = false)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (hasModifiers)
        {
            _lastTarget = null;
            return false;
        }

        var isDouble = ReferenceEquals(target, _lastTarget) &&
                       timestampMs - _lastTimestampMs <= doubleClickTimeMs;

        _lastTarget = isDouble ? null : target;
        _lastTimestampMs = timestampMs;
        return isDouble;
    }
}
