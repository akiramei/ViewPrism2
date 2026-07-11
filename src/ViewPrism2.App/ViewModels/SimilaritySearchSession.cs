using CommunityToolkit.Mvvm.ComponentModel;
using ViewPrism2.Core.Services.Similarity;

namespace ViewPrism2.App.ViewModels;

public enum SimilaritySearchSessionState
{
    Idle,
    Preparing,
    Comparing,
    Cancelling,
    Completed,
}

/// <summary>
/// ECO-066/REQ-089: 画像/作業タブが共有する類似検索session状態機械。
/// tokenによる処理停止とgenerationによる遅延結果拒否を分離する。
/// </summary>
public sealed class SimilaritySearchSession : ObservableObject
{
    private long _generation;
    private Run? _active;
    private SimilaritySearchSessionState _state;
    private int _completed;
    private int _total;

    public SimilaritySearchSessionState State => _state;
    public bool IsActive => _state is SimilaritySearchSessionState.Preparing
        or SimilaritySearchSessionState.Comparing
        or SimilaritySearchSessionState.Cancelling;
    public bool Preparing => _state == SimilaritySearchSessionState.Preparing;
    public bool Comparing => _state == SimilaritySearchSessionState.Comparing;
    public bool Cancelling => _state == SimilaritySearchSessionState.Cancelling;
    public bool ShowProgress => IsActive;
    public bool ProgressIndeterminate => Preparing || Cancelling;
    public int ProgressValue => _total <= 0 ? 0 : Math.Clamp(_completed * 100 / _total, 0, 100);
    public string ProgressLabel => _state switch
    {
        SimilaritySearchSessionState.Preparing => "基準画像を準備しています…",
        SimilaritySearchSessionState.Comparing => $"画像を比較中 {_completed} / {_total}（{ProgressValue}%）",
        SimilaritySearchSessionState.Cancelling => "停止しています…",
        _ => string.Empty,
    };

    public Run Start()
    {
        Invalidate();
        var run = new Run(++_generation);
        _active = run;
        _completed = 0;
        _total = 0;
        SetState(SimilaritySearchSessionState.Preparing);
        return run;
    }

    public IProgress<SimilaritySearchProgress> CreateProgress(Run run)
        => new Progress<SimilaritySearchProgress>(value => ApplyProgress(run, value));

    public void Cancel()
    {
        if (_active is null || !IsCurrent(_active) || _state == SimilaritySearchSessionState.Cancelling)
        {
            return;
        }

        SetState(SimilaritySearchSessionState.Cancelling);
        _active.Cancel();
    }

    public void Invalidate()
    {
        _generation++;
        var old = _active;
        _active = null;
        old?.Cancel();
        _completed = 0;
        _total = 0;
        SetState(SimilaritySearchSessionState.Idle);
    }

    public bool TryComplete(Run run)
    {
        if (!IsCurrent(run) || run.Token.IsCancellationRequested)
        {
            return false;
        }

        SetState(SimilaritySearchSessionState.Completed);
        return true;
    }

    public void Finish(Run run)
    {
        if (IsCurrent(run))
        {
            _active = null;
            if (_state != SimilaritySearchSessionState.Completed)
            {
                _completed = 0;
                _total = 0;
                SetState(SimilaritySearchSessionState.Idle);
            }
        }

        run.Dispose();
    }

    private void ApplyProgress(Run run, SimilaritySearchProgress value)
    {
        if (!IsCurrent(run) || run.Token.IsCancellationRequested)
        {
            return;
        }

        _completed = Math.Clamp(value.Completed, 0, Math.Max(0, value.Total));
        _total = Math.Max(0, value.Total);
        SetState(value.Phase == SimilaritySearchPhase.Preparing
            ? SimilaritySearchSessionState.Preparing
            : SimilaritySearchSessionState.Comparing);
    }

    private bool IsCurrent(Run run)
        => _active is not null
            && ReferenceEquals(_active, run)
            && run.Generation == _generation;

    private void SetState(SimilaritySearchSessionState state)
    {
        _state = state;
        OnPropertyChanged(string.Empty);
    }

    public sealed class Run : IDisposable
    {
        private readonly CancellationTokenSource _cts = new();

        internal Run(long generation) => Generation = generation;
        internal long Generation { get; }
        public CancellationToken Token => _cts.Token;
        internal void Cancel() => _cts.Cancel();
        public void Dispose() => _cts.Dispose();
    }
}
