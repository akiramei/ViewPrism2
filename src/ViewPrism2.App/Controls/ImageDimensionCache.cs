using System.Collections.Concurrent;

namespace ViewPrism2.App.Controls;

/// <summary>
/// セッション内 画像寸法キャッシュ(ビューア縦スクロールの高さ予約用・GF-V2/①-lite)。
/// 縦スクロールは仮想化下で item が「未ロード時 ~64px → フルデコード完了で実寸」へ高さジャンプし、
/// VirtualizingStackPanel の extent 推定が揺れてスクロールが暴走する。これを断つため、
/// フルデコードより先に SKCodec ヘッダ読み(<see cref="ThumbnailService.GetDimensionsAsync"/>)で
/// 寸法だけを取り、<see cref="ViewerImage"/> がデコード前に枠高を予約する。
///
/// 値は path → (Width, Height)。セッション内のみ(プロセス内)で、寸法は不変なので破棄不要。
/// 将来 DB へ持つ場合はこのキャッシュを永続化層へ昇格できる(reader を差し替えるだけ)。
/// 寸法読みは <see cref="ThumbnailService"/> 同様 Task.Run でデコードスレッド外。
/// </summary>
public sealed class ImageDimensionCache
{
    private readonly Func<string, Task<(int Width, int Height)?>> _reader;
    private readonly ConcurrentDictionary<string, (int Width, int Height)> _cache = new(StringComparer.Ordinal);

    // single-flight: 未キャッシュ path の進行中読みを 1 本へ集約する。速いスクロールで
    // Prewarm / ApplyDimensions / 再アタッチ が同一 path を同時要求しても reader は 1 回だけ走る。
    private readonly ConcurrentDictionary<string, Task<(int Width, int Height)?>> _inflight = new(StringComparer.Ordinal);

    public ImageDimensionCache(Func<string, Task<(int Width, int Height)?>> reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _reader = reader;
    }

    /// <summary>キャッシュ済みなら即返す(同期・UI スレッドの Measure から使う)。</summary>
    public bool TryGet(string path, out (int Width, int Height) dims) => _cache.TryGetValue(path, out dims);

    /// <summary>キャッシュ or ヘッダ読みで寸法を得る。失敗は null。成功はキャッシュへ格納。
    /// 同一 path の同時要求は single-flight で 1 読みに集約する(全 caller が同じ Task を待つ)。</summary>
    public Task<(int Width, int Height)?> GetOrReadAsync(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return Task.FromResult<(int Width, int Height)?>(null);
        }

        if (_cache.TryGetValue(path, out var cached))
        {
            return Task.FromResult<(int Width, int Height)?>(cached);
        }

        // 先に in-flight を登録してから読む(reader が同期完了しても登録前に除去が走らない)。
        var tcs = new TaskCompletionSource<(int Width, int Height)?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var existing = _inflight.GetOrAdd(path, tcs.Task);
        if (!ReferenceEquals(existing, tcs.Task))
        {
            return existing; // 別 caller が既に読んでいる: その結果を共有
        }

        _ = RunReadAsync(path, tcs);
        return tcs.Task;
    }

    private async Task RunReadAsync(string path, TaskCompletionSource<(int Width, int Height)?> tcs)
    {
        try
        {
            var read = await _reader(path).ConfigureAwait(false);
            (int Width, int Height)? result = read is { } d && d.Width > 0 && d.Height > 0 ? d : null;
            if (result is { } v)
            {
                _cache[path] = v; // 成功のみ格納(失敗は次回再試行)
            }

            _inflight.TryRemove(path, out _); // 完了は cache が真実: 除去後の caller は hit(成功)/再読み(失敗)
            tcs.SetResult(result);
        }
        catch (Exception ex)
        {
            // reader は失敗を null で返す契約(例外は想定外)。安全側で in-flight を残さない。
            _inflight.TryRemove(path, out _);
            tcs.SetException(ex);
        }
    }

    /// <summary>
    /// 一覧分の寸法を背景で先読みする(ビューア起動時)。スクロール前にキャッシュを温め、
    /// item 実体化時に最初から正しい高さを予約できるようにする。並列度は控えめに制限する。
    /// </summary>
    public async Task PrewarmAsync(IEnumerable<string> paths, int concurrency = 3, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        using var gate = new SemaphoreSlim(Math.Max(1, concurrency));
        var tasks = new List<Task>();
        try
        {
            foreach (var path in paths)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break; // ビューアを閉じた等: 残りは温めない
                }

                if (string.IsNullOrEmpty(path) || _cache.ContainsKey(path))
                {
                    continue;
                }

                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await GetOrReadAsync(path).ConfigureAwait(false);
                    }
                    finally
                    {
                        gate.Release();
                    }
                }));
            }
        }
        catch (OperationCanceledException)
        {
            // キャンセルは正常系(残り spawn を止めるだけ)
        }

        await Task.WhenAll(tasks).ConfigureAwait(false); // 起動済みの軽量読みは待って後始末
    }
}
