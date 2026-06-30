using System.Collections.Concurrent;
using ViewPrism2.App.Controls;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// ビューア寸法キャッシュ(GF-V2/①-lite): 縦スクロールの高さ予約に使うセッション内 path→(W,H) キャッシュ。
/// ヘッダ読みは一度だけ(キャッシュ命中で再読みしない)・失敗は格納しない(次回再試行)・先読みで温まる。
/// </summary>
[Trait("cp", "CP-VIEWER-DIMCACHE")]
public sealed class CpViewerDimensionCacheTests
{
    [Fact]
    public async Task 一度読んだらキャッシュし再読みしない()
    {
        var calls = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        var cache = new ImageDimensionCache(p =>
        {
            calls.AddOrUpdate(p, 1, (_, n) => n + 1);
            return Task.FromResult<(int, int)?>((800, 600));
        });

        var first = await cache.GetOrReadAsync("a.png");
        var second = await cache.GetOrReadAsync("a.png");

        Assert.Equal((800, 600), first);
        Assert.Equal((800, 600), second);
        Assert.Equal(1, calls["a.png"]); // 2 回目はキャッシュ命中で reader を呼ばない
        Assert.True(cache.TryGet("a.png", out var dims));
        Assert.Equal((800, 600), dims);
    }

    [Fact]
    public async Task 読み取り失敗は格納しない_次回再試行()
    {
        var attempts = 0;
        var cache = new ImageDimensionCache(_ =>
        {
            attempts++;
            return Task.FromResult<(int, int)?>(attempts >= 2 ? (100, 100) : null); // 1 回目失敗・2 回目成功
        });

        Assert.Null(await cache.GetOrReadAsync("x.png")); // 失敗
        Assert.False(cache.TryGet("x.png", out _));        // 格納されない
        Assert.Equal((100, 100), await cache.GetOrReadAsync("x.png")); // 再試行で成功
        Assert.True(cache.TryGet("x.png", out _));
    }

    [Fact]
    public void 未読はTryGetがfalse()
    {
        var cache = new ImageDimensionCache(_ => Task.FromResult<(int, int)?>((1, 1)));
        Assert.False(cache.TryGet("none.png", out _));
    }

    [Fact]
    public async Task 同一pathの同時要求はsingle_flightで1読みに集約()
    {
        var calls = 0;
        var gate = new TaskCompletionSource<(int, int)?>();
        var cache = new ImageDimensionCache(_ =>
        {
            Interlocked.Increment(ref calls);
            return gate.Task; // reader を保留して同時 in-flight を作る
        });

        // 読み取り完了前に同一 path を 5 並列要求
        var t1 = cache.GetOrReadAsync("a.png");
        var t2 = cache.GetOrReadAsync("a.png");
        var t3 = cache.GetOrReadAsync("a.png");
        var t4 = cache.GetOrReadAsync("a.png");
        var t5 = cache.GetOrReadAsync("a.png");

        gate.SetResult((640, 480)); // 全 caller を解放
        var results = await Task.WhenAll(t1, t2, t3, t4, t5);

        Assert.Equal(1, calls); // single-flight: reader は 1 回だけ
        Assert.All(results, r => Assert.Equal((640, 480), r));
        Assert.True(cache.TryGet("a.png", out _)); // 集約後はキャッシュ命中
    }

    [Fact]
    public async Task 先読みで一覧分が温まる_重複や空は無視()
    {
        var cache = new ImageDimensionCache(p =>
            Task.FromResult<(int, int)?>((p.Length * 10, 480)));

        await cache.PrewarmAsync(
            new[] { "a.png", "bb.png", "a.png", "", "ccc.png" },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(cache.TryGet("a.png", out var a));
        Assert.Equal((50, 480), a);
        Assert.True(cache.TryGet("bb.png", out _));
        Assert.True(cache.TryGet("ccc.png", out _));
        Assert.False(cache.TryGet("", out _));
    }
}
