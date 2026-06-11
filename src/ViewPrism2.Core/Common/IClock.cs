namespace ViewPrism2.Core.Common;

/// <summary>
/// 時刻抽象(M-CORE-001)。日時文字列は IClock 経由でのみ生成する(DateTime.Now 直呼び禁止)。
/// テストで差し替え可能にする。
/// </summary>
public interface IClock
{
    /// <summary>現在 UTC 時刻を ISO 8601(yyyy-MM-ddTHH:mm:ss.fffZ)で返す(REQ-002 / INV-002)。</summary>
    string UtcNowIso();
}

/// <summary>システム時計による <see cref="IClock"/> 実装。</summary>
public sealed class SystemClock : IClock
{
    public string UtcNowIso() => IsoTimestamp.Format(DateTime.UtcNow);
}
