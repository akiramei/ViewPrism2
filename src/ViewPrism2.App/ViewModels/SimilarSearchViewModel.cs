using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ViewPrism2.App.Services;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using ViewPrism2.Core.Services.Similarity;

namespace ViewPrism2.App.ViewModels;

/// <summary>類似検索結果の 1 件(サムネイル+類似度%)。マージ先/元の選択状態を保持。</summary>
public sealed partial class SimilarResultViewModel : ObservableObject
{
    public SimilarResultViewModel(ImageEntry entry, int score, string selectLabel)
    {
        Entry = entry;
        Score = score;
        SelectLabel = selectLabel;
    }

    /// <summary>「マージ元に選択」のラベル(i18n。生成時に解決)。</summary>
    public string SelectLabel { get; }

    public ImageEntry Entry { get; }

    public ImageRecord Record => Entry.Record;

    public string AbsolutePath => Entry.AbsolutePath;

    public string FileName => Record.FileName;

    /// <summary>類似度%(右上バッジ)。</summary>
    public int Score { get; }

    public string ScoreText => Score.ToString(CultureInfo.InvariantCulture) + "%";

    /// <summary>マージ元(統合)として選択中。</summary>
    [ObservableProperty]
    private bool _isSelectedAsSource;
}

/// <summary>
/// 類似検索 UI の ViewModel(M-UI-SIMILARITY-023 / E-UI-SIMILARITY-035、仕様 §2.10.4)。
/// 基準画像 1 枚に対し閾値スライダー(50〜100・整数・既定 70)で検索し、結果を類似度%降順で表示する。
/// 精度モード UI は出さない(pHash のみ)。結果整列・空状態は ViewModel で unit 検査可能にする。
/// </summary>
public sealed partial class SimilarSearchViewModel : ObservableObject
{
    /// <summary>閾値の下限(仕様 §2.10.4)。</summary>
    public const int MinThreshold = 50;

    /// <summary>閾値の上限。</summary>
    public const int MaxThreshold = 100;

    private readonly ImageEntry _baseImage;
    private readonly IReadOnlyDictionary<string, ImageEntry> _entriesById;
    private readonly SimilaritySearchService _search;
    private readonly LocalizationService _localization;
    private readonly IWindowService _windows;

    public SimilarSearchViewModel(
        ImageEntry baseImage,
        IReadOnlyList<ImageEntry> collectionEntries,
        SimilaritySearchService search,
        LocalizationService localization,
        IWindowService windows)
    {
        ArgumentNullException.ThrowIfNull(baseImage);
        ArgumentNullException.ThrowIfNull(collectionEntries);
        _baseImage = baseImage;
        _entriesById = collectionEntries.ToDictionary(e => e.Record.Id, StringComparer.Ordinal);
        _search = search;
        _localization = localization;
        _windows = windows;
        Loc = new LocalizationProxy(localization);
        localization.CultureChanged += (_, _) =>
        {
            Loc = new LocalizationProxy(localization);
            OnPropertyChanged(nameof(Loc));
        };
    }

    public LocalizationProxy Loc { get; private set; }

    /// <summary>基準画像(マージ先=「保持」の既定候補)。</summary>
    public ImageEntry BaseImage => _baseImage;

    public string BaseFileName => _baseImage.Record.FileName;

    public string BaseAbsolutePath => _baseImage.AbsolutePath;

    /// <summary>類似度%降順の検索結果。</summary>
    public ObservableCollection<SimilarResultViewModel> Results { get; } = [];

    /// <summary>閾値(50〜100・整数・既定 70)。範囲外はクランプ。</summary>
    [ObservableProperty]
    private int _threshold = 70;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private int _progress;

    /// <summary>検索を実行したことがあるか(空状態の表示制御用)。</summary>
    [ObservableProperty]
    private bool _hasSearched;

    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>結果 0 件の空状態(検索実行後かつ結果なし)。</summary>
    public bool IsEmpty => HasSearched && Results.Count == 0 && !IsSearching;

    /// <summary>マージ元に 1 件以上選択済みか(マージ実行の活性条件)。</summary>
    public bool HasMergeSources => Results.Any(r => r.IsSelectedAsSource);

    partial void OnThresholdChanged(int value)
    {
        var clamped = Math.Clamp(value, MinThreshold, MaxThreshold);
        if (clamped != value)
        {
            Threshold = clamped;
        }
    }

    partial void OnIsSearchingChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));

    partial void OnHasSearchedChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (IsSearching)
        {
            return;
        }

        IsSearching = true;
        Progress = 0;
        Results.Clear();
        OnPropertyChanged(nameof(HasMergeSources));
        try
        {
            var progress = new Progress<int>(p => Progress = p);
            var found = await _search.FindSimilarAsync(_baseImage.Record.Id, Threshold, progress);

            foreach (var result in found)
            {
                if (_entriesById.TryGetValue(result.ImageId, out var entry))
                {
                    var vm = new SimilarResultViewModel(entry, result.Score, _localization.T("similar.selectAsSource"));
                    vm.PropertyChanged += (_, e) =>
                    {
                        if (e.PropertyName == nameof(SimilarResultViewModel.IsSelectedAsSource))
                        {
                            OnPropertyChanged(nameof(HasMergeSources));
                        }
                    };
                    Results.Add(vm);
                }
            }
        }
        finally
        {
            IsSearching = false;
            HasSearched = true;
            Progress = 100;
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasMergeSources));
        }
    }

    /// <summary>
    /// マージへ進む: 基準画像=マージ先、選択済みの結果=マージ元としてマージ UI を開く。
    /// マージ元未選択なら何もしない。
    /// </summary>
    [RelayCommand]
    private async Task MergeAsync()
    {
        var sources = Results.Where(r => r.IsSelectedAsSource).Select(r => r.Entry).ToList();
        if (sources.Count == 0)
        {
            return;
        }

        var merged = await _windows.ShowMergeAsync(_baseImage, sources);
        if (merged)
        {
            // マージ済みの元を結果から外す(deleted になったため)
            var mergedIds = sources.Select(s => s.Record.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var r in Results.Where(r => mergedIds.Contains(r.Record.Id)).ToList())
            {
                Results.Remove(r);
            }

            StatusMessage = _localization.T("merge.completed");
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasMergeSources));
        }
    }
}
