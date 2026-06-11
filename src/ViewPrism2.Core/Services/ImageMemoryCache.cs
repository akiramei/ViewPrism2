using ViewPrism2.Core.Common;

namespace ViewPrism2.Core.Services;

/// <summary>
/// フルサイズ表示用メモリキャッシュ(OC-6、REQ-045)。
/// LRU・上限 50 枚・有効期限 3 分(ロード時刻起点)。IClock 注入で TTL をテスト可能にする。
/// 上限超過時は最終アクセスが最古のものから破棄する(E-CACHE-011)。
/// </summary>
public sealed class ImageMemoryCache
{
    /// <summary>保持上限(REQ-045)。</summary>
    public const int Capacity = 50;

    /// <summary>有効期限(REQ-045)。経過した取得は再ロードになる。</summary>
    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(3);

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IClock _clock;
    private long _accessSequence;
    private int _loadCount;

    public ImageMemoryCache(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
    }

    /// <summary>ローダー実行回数(受入用メトリクス、M-CACHE-009)。</summary>
    public int LoadCount
    {
        get
        {
            _gate.Wait();
            try
            {
                return _loadCount;
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    /// <summary>現在保持しているキー集合のスナップショット(受入用)。</summary>
    public IReadOnlyCollection<string> Keys
    {
        get
        {
            _gate.Wait();
            try
            {
                return [.. _entries.Keys];
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    public async Task<T> GetOrAddAsync<T>(string key, Func<Task<T>> loader)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(loader);

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var now = IsoTimestamp.Parse(_clock.UtcNowIso());

            if (_entries.TryGetValue(key, out var entry) && now - entry.LoadedAtUtc < Ttl)
            {
                entry.AccessSequence = ++_accessSequence;
                return (T)entry.Value!;
            }

            _loadCount++;
            var value = await loader().ConfigureAwait(false);
            _entries[key] = new Entry
            {
                Value = value,
                LoadedAtUtc = now,
                AccessSequence = ++_accessSequence,
            };

            EvictIfOverCapacity();
            return value;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void EvictIfOverCapacity()
    {
        while (_entries.Count > Capacity)
        {
            string? oldestKey = null;
            var oldestSequence = long.MaxValue;
            foreach (var (key, entry) in _entries)
            {
                if (entry.AccessSequence < oldestSequence)
                {
                    oldestSequence = entry.AccessSequence;
                    oldestKey = key;
                }
            }

            _entries.Remove(oldestKey!);
        }
    }

    private sealed class Entry
    {
        public object? Value { get; init; }
        public DateTime LoadedAtUtc { get; init; }
        public long AccessSequence { get; set; }
    }
}
