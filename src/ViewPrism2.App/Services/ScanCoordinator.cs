using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Infrastructure.Scanning;

namespace ViewPrism2.App.Services;

public enum CollectionScanPhase
{
    Started,
    BatchCommitted,
    Completed,
}

public sealed record CollectionScanUpdate(
    string FolderId,
    CollectionScanPhase Phase,
    IReadOnlyList<ImageRecord> Images,
    bool Succeeded = true);

/// <summary>
/// ECO-060: window-localなスキャン状態をapplication lifetimeへ昇格し、画像タブへ
/// started/batch/completedをfan-outする。UI SynchronizationContextがある場合はそこへ戻す。
/// </summary>
public sealed class ScanCoordinator
{
    private readonly ScanService _scan;
    private readonly object _gate = new();
    private readonly HashSet<string> _scanning = new(StringComparer.Ordinal);

    public ScanCoordinator(ScanService scan)
    {
        _scan = scan;
    }

    public event EventHandler<CollectionScanUpdate>? Updated;

    public bool IsScanning(string folderId)
    {
        lock (_gate)
        {
            return _scanning.Contains(folderId);
        }
    }

    public async Task<Result<ScanSummary>> ScanAsync(
        string folderId,
        IProgress<int>? progress,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(folderId);
        lock (_gate)
        {
            if (!_scanning.Add(folderId))
            {
                return Result<ScanSummary>.Fail(ErrorCode.ScanInProgress, "このフォルダはスキャン実行中です。");
            }
        }

        var context = SynchronizationContext.Current;
        Raise(new CollectionScanUpdate(folderId, CollectionScanPhase.Started, []));
        Result<ScanSummary>? result = null;
        try
        {
            var committed = new ContextProgress<ScanBatchCommitted>(context, batch =>
                Raise(new CollectionScanUpdate(
                    batch.FolderId,
                    CollectionScanPhase.BatchCommitted,
                    batch.Images)));
            result = await _scan.ScanAsync(folderId, progress, ct, committed).ConfigureAwait(true);
            return result;
        }
        finally
        {
            lock (_gate)
            {
                _scanning.Remove(folderId);
            }
            Raise(new CollectionScanUpdate(
                folderId,
                CollectionScanPhase.Completed,
                [],
                result?.IsSuccess == true));
        }
    }

    /// <summary>
    /// ECO-130/REQ-100: 再スキャンの差分計算。DB 完全無変更のため started/batch/completed は raise しない
    /// (段階的公開は初回スキャン専用= REQ-086)。二重起動ガードだけ従来経路と共有する。
    /// </summary>
    public async Task<Result<ScanStaging>> StageAsync(
        string folderId, IProgress<int>? processed, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(folderId);
        lock (_gate)
        {
            if (!_scanning.Add(folderId))
            {
                return Result<ScanStaging>.Fail(ErrorCode.ScanInProgress, "このフォルダはスキャン実行中です。");
            }
        }

        try
        {
            return await _scan.StageAsync(folderId, processed, ct).ConfigureAwait(true);
        }
        finally
        {
            lock (_gate)
            {
                _scanning.Remove(folderId);
            }
        }
    }

    /// <summary>
    /// ECO-130/REQ-100: ステージング適用(一括反映+last_scan 更新)。適用完了後の一覧反映は
    /// 呼び出し側の DataChanged 経路(フォルダ管理→画像タブ再読込)が担う。
    /// </summary>
    public async Task<Result<ScanSummary>> ApplyStagedAsync(
        ScanStaging staging, IProgress<int>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(staging);
        lock (_gate)
        {
            if (!_scanning.Add(staging.FolderId))
            {
                return Result<ScanSummary>.Fail(ErrorCode.ScanInProgress, "このフォルダはスキャン実行中です。");
            }
        }

        try
        {
            return await _scan.ApplyStagedAsync(staging, progress, ct).ConfigureAwait(true);
        }
        finally
        {
            lock (_gate)
            {
                _scanning.Remove(staging.FolderId);
            }
        }
    }

    private void Raise(CollectionScanUpdate update) => Updated?.Invoke(this, update);

    private sealed class ContextProgress<T> : IProgress<T>
    {
        private readonly SynchronizationContext? _context;
        private readonly Action<T> _handler;

        public ContextProgress(SynchronizationContext? context, Action<T> handler)
        {
            _context = context;
            _handler = handler;
        }

        public void Report(T value)
        {
            if (_context is null || ReferenceEquals(_context, SynchronizationContext.Current))
            {
                _handler(value);
                return;
            }

            _context.Post(static state =>
            {
                var (handler, item) = ((Action<T>, T))state!;
                handler(item);
            }, (_handler, value));
        }
    }
}
