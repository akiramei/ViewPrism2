using ViewPrism2.Core.Common;

namespace ViewPrism2.Tests;

/// <summary>テスト用の差し替え時計(M-CORE-001: IClock はテストで差し替え可能に)。</summary>
internal sealed class FakeClock : IClock
{
    private DateTime _utcNow;

    public FakeClock(DateTime startUtc)
    {
        _utcNow = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc);
    }

    public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);

    public string UtcNowIso() => IsoTimestamp.Format(_utcNow);
}
