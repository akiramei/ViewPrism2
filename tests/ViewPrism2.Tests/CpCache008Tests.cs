using ViewPrism2.Core.Services;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>CP-CACHE-008: 画像メモリキャッシュの LRU・TTL・上限が REQ-045 と一致する(OC-6)。</summary>
[Trait("cp", "CP-CACHE-008")]
public sealed class CpCache008Tests
{
    private static FakeClock NewClock() => new(new DateTime(2026, 6, 11, 0, 0, 0, DateTimeKind.Utc));

    [Fact]
    public async Task 同一キー2回でロードは1回()
    {
        var cache = new ImageMemoryCache(NewClock());

        var first = await cache.GetOrAddAsync("k", () => Task.FromResult("payload"));
        var second = await cache.GetOrAddAsync("k", () => Task.FromResult("other"));

        Assert.Equal(1, cache.LoadCount);
        Assert.Equal("payload", first);
        Assert.Equal("payload", second); // キャッシュヒット(2 回目のローダーは実行されない)
    }

    [Fact]
    public async Task 上限51個目で最終アクセス最古の1個が破棄される_FMEA007()
    {
        var cache = new ImageMemoryCache(NewClock());

        for (var i = 0; i <= 50; i++)
        {
            var key = $"k{i:D2}";
            await cache.GetOrAddAsync(key, () => Task.FromResult(key));
        }

        Assert.Equal(51, cache.LoadCount);
        Assert.Equal(ImageMemoryCache.Capacity, cache.Keys.Count);
        Assert.DoesNotContain("k00", cache.Keys); // 最終アクセス最古
        Assert.Contains("k01", cache.Keys);
        Assert.Contains("k50", cache.Keys);

        // 破棄済みキーの再取得は再ロードになる
        await cache.GetOrAddAsync("k00", () => Task.FromResult("k00"));
        Assert.Equal(52, cache.LoadCount);
    }

    [Fact]
    public async Task アクセスでLRU順が更新される()
    {
        var cache = new ImageMemoryCache(NewClock());

        for (var i = 0; i < 50; i++)
        {
            var key = $"k{i:D2}";
            await cache.GetOrAddAsync(key, () => Task.FromResult(key));
        }

        // k00 に触れて最古を k01 にする
        await cache.GetOrAddAsync("k00", () => Task.FromResult("reload"));
        Assert.Equal(50, cache.LoadCount); // ヒット(再ロードなし)

        await cache.GetOrAddAsync("k50", () => Task.FromResult("k50"));

        Assert.Contains("k00", cache.Keys);
        Assert.DoesNotContain("k01", cache.Keys);
    }

    [Fact]
    public async Task TTL3分経過後の取得は再ロード()
    {
        var clock = NewClock();
        var cache = new ImageMemoryCache(clock);

        await cache.GetOrAddAsync("x", () => Task.FromResult(1));
        Assert.Equal(1, cache.LoadCount);

        clock.Advance(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(59));
        await cache.GetOrAddAsync("x", () => Task.FromResult(2));
        Assert.Equal(1, cache.LoadCount); // 期限内はキャッシュヒット

        clock.Advance(TimeSpan.FromSeconds(2)); // 計 3 分 1 秒
        var value = await cache.GetOrAddAsync("x", () => Task.FromResult(3));
        Assert.Equal(2, cache.LoadCount); // 期限切れ → 再ロード
        Assert.Equal(3, value);
    }
}
