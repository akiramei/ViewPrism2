using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using ViewPrism2.Core.Services.Package;
using ViewPrism2.Infrastructure.Database;

namespace ViewPrism2.App.ViewModels;

/// <summary>B-3 タグ競合 1 行(4 択: 中止=要対応へ戻す/スキップ/別名で取込/既存へ割当)。</summary>
public sealed partial class TagConflictRowViewModel(
    TagPlanItem item, IReadOnlyList<Tag> compatibleTargets, Action onChanged) : ObservableObject
{
    public string Name => item.Source.Name;

    public string Detail => item.Detail ?? "";

    /// <summary>「既存へ割当」候補=値の型が互換なタグのみ(CAD 実装契約)。</summary>
    public IReadOnlyList<Tag> Targets { get; } = compatibleTargets;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsResolved), nameof(SummaryText), nameof(IsRename), nameof(IsMap), nameof(IsSkip))]
    private TagImportDecision? _choice;

    [ObservableProperty]
    private string _renameTo = $"{item.Source.Name} (取込)";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsResolved), nameof(SummaryText))]
    private Tag? _mapTarget;

    public bool IsRename => Choice == TagImportDecision.ResolvedRename;

    public bool IsMap => Choice == TagImportDecision.ResolvedManualMap;

    public bool IsSkip => Choice == TagImportDecision.ResolvedSkip;

    /// <summary>mock B-3 のタグカラードット(GF-073-07)。未定義色は非表示。</summary>
    public string? Color => item.Source.Color;

    public bool IsResolved => Choice switch
    {
        TagImportDecision.ResolvedSkip => true,
        TagImportDecision.ResolvedRename => !string.IsNullOrWhiteSpace(RenameTo),
        TagImportDecision.ResolvedManualMap => MapTarget is not null,
        _ => false,
    };

    public string SummaryText => Choice switch
    {
        TagImportDecision.ResolvedSkip => "→ スキップ",
        TagImportDecision.ResolvedRename => $"→ 別名 {RenameTo}",
        TagImportDecision.ResolvedManualMap when MapTarget is not null => $"→ {MapTarget.Name} へ割当",
        _ => "",
    };

    public TagConflictResolution? ToResolution() => Choice switch
    {
        TagImportDecision.ResolvedSkip => new(TagImportDecision.ResolvedSkip),
        TagImportDecision.ResolvedRename => new(TagImportDecision.ResolvedRename, RenameTo: RenameTo),
        TagImportDecision.ResolvedManualMap when MapTarget is not null =>
            new(TagImportDecision.ResolvedManualMap, MapToLocalTagId: MapTarget.Id),
        _ => null,
    };

    public string SourceId => item.Source.SourceId;

    [RelayCommand]
    private void Abort() { Choice = null; onChanged(); }          // 中止=要対応へ戻す(実行不可のまま)

    [RelayCommand]
    private void Skip() { Choice = TagImportDecision.ResolvedSkip; onChanged(); }

    [RelayCommand]
    private void Rename() { Choice = TagImportDecision.ResolvedRename; onChanged(); }

    [RelayCommand]
    private void Map() { Choice = TagImportDecision.ResolvedManualMap; onChanged(); }

    partial void OnRenameToChanged(string value) => onChanged();

    partial void OnMapTargetChanged(Tag? value) => onChanged();
}

/// <summary>
/// B-2〜B-4 取り込みウィザード(ECO-073・CAD snapshot_export_import)。
/// stepper: 1 ファイル → 2 検証 → 3 プレビュー&競合解決 → 4 完了。
/// マージは追加型(削除しない=M4)。タグ競合が未解決の間は実行不可。未解決画像は missing 登録(gate①)。
/// </summary>
public sealed partial class CollectionImportViewModel : ObservableObject
{
    private readonly CollectionPackageImporter _importer;
    private readonly SyncFolder _collection;
    private readonly LocalizationService _localization;
    private readonly Func<string, Task<string?>> _pickOpenFile;
    private readonly Func<Task<IReadOnlyList<Tag>>> _loadLocalTags;

    public CollectionImportViewModel(
        CollectionPackageImporter importer,
        SyncFolder collection,
        LocalizationService localization,
        Func<string, Task<string?>> pickOpenFile,
        Func<Task<IReadOnlyList<Tag>>> loadLocalTags)
    {
        _importer = importer;
        _collection = collection;
        _localization = localization;
        _pickOpenFile = pickOpenFile;
        _loadLocalTags = loadLocalTags;
        Loc = new LocalizationProxy(localization);
        localization.CultureChanged += (_, _) =>
        {
            Loc = new LocalizationProxy(localization);
            OnPropertyChanged(nameof(Loc));
            OnPropertyChanged(nameof(WindowTitle));
        };
    }

    public LocalizationProxy Loc { get; private set; }

    public string TargetRootPath => _collection.Path;

    /// <summary>
    /// GF-073-04: mock の擬似タイトルは面ごとに異なる(B-2=コレクションを取り込む/
    /// B-3=取り込みプレビュー — 名前/B-4=取り込み結果)。GF-073-01 の規約どおり Window.Title が担う。
    /// </summary>
    public string WindowTitle => Step switch
    {
        2 => _localization.T("package.previewTitle", new Dictionary<string, string>
        {
            ["name"] = HeaderCollectionName ?? "",
        }),
        3 => _localization.T("package.resultTitle"),
        _ => _localization.T("package.importTitle"),
    };

    // ---- stepper(1=ファイル/検証 2=プレビュー 3=完了) ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OnFileStep), nameof(OnPreviewStep), nameof(OnDoneStep), nameof(WindowTitle))]
    private int _step = 1;

    public bool OnFileStep => Step == 1;

    public bool OnPreviewStep => Step == 2;

    public bool OnDoneStep => Step == 3;

    // ---- B-2: ファイル選択+検証 ----

    [ObservableProperty]
    private string? _packagePath;

    public string? PackageFileName => PackagePath is null ? null : Path.GetFileName(PackagePath);

    /// <summary>選択ファイルのサイズ淡色行(GF-073-03・mock B-2 の 2 行構成)。</summary>
    public string? PackageFileSizeText => PackagePath is not null && File.Exists(PackagePath)
        ? Core.Common.ByteSizeFormatter.Format(new FileInfo(PackagePath).Length)
        : null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VerifyOk))]
    private string? _verifyError;

    public bool VerifyOk => Header is not null && VerifyError is null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VerifyOk), nameof(HeaderCollectionName), nameof(HeaderImageCount), nameof(HeaderTagCount), nameof(HeaderCreatedAt), nameof(HeaderAppVersion))]
    private PackageHeader? _header;

    public string? HeaderCollectionName => Header?.Collection.Name;

    public string HeaderImageCount => Header?.ImageCount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) ?? "";

    public string HeaderTagCount => Header?.Tags.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";

    // GF-073-03: 生 ISO を見せない(mock は yyyy/MM/dd HH:mm。A-1 SnapshotItemViewModel と同流儀)
    public string HeaderCreatedAt => Header?.CreatedAt is { Length: > 0 } iso
        && DateTime.TryParse(iso, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
        ? dt.ToLocalTime().ToString("yyyy/MM/dd HH:mm", System.Globalization.CultureInfo.InvariantCulture)
        : Header?.CreatedAt ?? "";

    public string HeaderAppVersion => Header?.AppVersion ?? "";

    [RelayCommand]
    private async Task PickFileAsync()
    {
        var picked = await _pickOpenFile(_localization.T("package.importTitle"));
        if (string.IsNullOrEmpty(picked))
        {
            return;
        }

        PackagePath = picked;
        OnPropertyChanged(nameof(PackageFileName));
        OnPropertyChanged(nameof(PackageFileSizeText));
        var header = _importer.ReadHeader(picked);
        if (header.IsSuccess)
        {
            Header = header.Value;
            VerifyError = null;
        }
        else
        {
            Header = null;
            VerifyError = header.Message;
        }
    }

    // ---- B-3: プレビュー&競合解決 ----

    public ObservableCollection<TagConflictRowViewModel> Conflicts { get; } = [];

    // GF-073-07: mock B-3 のタグ件数チップ(新規作成=緑/既存へ自動対応=青/競合=黄)
    [ObservableProperty]
    private string _tagCreatedChip = "";

    [ObservableProperty]
    private string _tagMappedChip = "";

    [ObservableProperty]
    private string _tagConflictChip = "";

    /// <summary>「競合の解決(未解決 N 件)」見出し(mock B-3・GF-073-07)。</summary>
    public string ConflictHeader => _localization.T("package.conflictHeading", new Dictionary<string, string>
    {
        ["count"] = Conflicts.Count(c => !c.IsResolved).ToString(System.Globalization.CultureInfo.InvariantCulture),
    });

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateSumText))]
    private ImageMatchCounts? _imageCounts;

    /// <summary>mock B-3 の検算行「a + b + c + d + e = 合計(パッケージの画像数と一致)」。</summary>
    public string StateSumText => ImageCounts is not { } c
        ? ""
        : string.Join(" + ", new[] { c.Exact, c.Moved, c.Changed, c.Ambiguous, c.Unresolved }
              .Select(v => v.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)))
          + " = " + c.Total.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)
          + " " + _localization.T("package.stateSumNote");

    public ObservableCollection<string> UnresolvedSamples { get; } = [];

    public bool HasUnresolved => UnresolvedSamples.Count > 0;

    [ObservableProperty]
    private bool _majorityUnresolved;

    /// <summary>過半ガードの確認(EX-002)。警告に同意しない限り実行不可。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExecute))]
    private bool _acceptMajority;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExecute))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _statusMessage;

    public bool CanExecute =>
        !IsBusy && Conflicts.All(c => c.IsResolved) && (!MajorityUnresolved || AcceptMajority);

    public string BlockReason => Conflicts.Count(c => !c.IsResolved) is var n and > 0
        ? _localization.T("package.conflictsBlock", new Dictionary<string, string>
        {
            ["count"] = n.ToString(System.Globalization.CultureInfo.InvariantCulture),
        })
        : "";

    private void OnConflictChanged()
    {
        OnPropertyChanged(nameof(CanExecute));
        OnPropertyChanged(nameof(BlockReason));
        OnPropertyChanged(nameof(ConflictHeader));
    }

    [RelayCommand]
    private async Task ToPreviewAsync()
    {
        if (PackagePath is null || !VerifyOk || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            // GF-073-06: 照合走査は大量画像で長時間かかる。UI スレッドを塞ぐと「応答なし」→
            // ウィンドウが背面に落ちるため、バックグラウンドで実行する
            var preview = await Task.Run(() => _importer.PreviewAsync(PackagePath, _collection.Id));
            if (!preview.IsSuccess)
            {
                VerifyError = preview.Message;
                return;
            }

            var plan = preview.Value!.TagPlan;
            var mapped = plan.Items.Count(i => i.Decision is TagImportDecision.MappedById
                or TagImportDecision.MappedByPersistentMapping or TagImportDecision.MappedBySemantic);
            var created = plan.Items.Count(i => i.Decision == TagImportDecision.CreateNew);
            TagCreatedChip = _localization.T("package.chipCreated", new Dictionary<string, string>
            {
                ["count"] = created.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
            TagMappedChip = _localization.T("package.chipMapped", new Dictionary<string, string>
            {
                ["count"] = mapped.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
            TagConflictChip = _localization.T("package.chipConflict", new Dictionary<string, string>
            {
                ["count"] = plan.UnresolvedConflicts.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });

            var localTags = await _loadLocalTags();
            Conflicts.Clear();
            foreach (var item in plan.UnresolvedConflicts)
            {
                var compatible = localTags.Where(t => t.Type == item.Source.Type).ToList();
                Conflicts.Add(new TagConflictRowViewModel(item, compatible, OnConflictChanged));
            }

            ImageCounts = preview.Value.Images;
            MajorityUnresolved = preview.Value.Images.MajorityUnresolved;
            UnresolvedSamples.Clear();
            foreach (var s in preview.Value.UnresolvedSamples)
            {
                UnresolvedSamples.Add(s);
            }

            OnPropertyChanged(nameof(HasUnresolved));
            Step = 2;
            OnConflictChanged();
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---- 実行+B-4 結果 ----

    [ObservableProperty]
    private ImportResult? _result;

    public string ResultAdded => Result?.AddedAssignments.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) ?? "0";

    public string ResultUnchanged => Result?.UnchangedAssignments.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) ?? "0";

    public string ResultSkipped => (Result?.SkippedAssignments + Result?.ConflictKeptAssignments ?? 0)
        .ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

    public string ResultConflictResolved => Conflicts.Count(c => c.Choice is not null and not TagImportDecision.Conflict)
        .ToString(System.Globalization.CultureInfo.InvariantCulture);

    public string ResultSummaryText => Result is null
        ? ""
        : _localization.T("package.resultSummary", new Dictionary<string, string>
        {
            ["package"] = HeaderCollectionName ?? "",
            ["collection"] = _collection.Name,
        });

    [RelayCommand]
    private async Task ExecuteAsync()
    {
        if (PackagePath is null || !CanExecute)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = null;
        try
        {
            var resolutions = Conflicts
                .Select(c => (c.SourceId, Resolution: c.ToResolution()))
                .Where(x => x.Resolution is not null)
                .ToDictionary(x => x.SourceId, x => x.Resolution!, StringComparer.Ordinal);
            var path = PackagePath;
            var accept = AcceptMajority;
            // GF-073-06: 適用(単一トランザクションの大量 INSERT)も同様にバックグラウンドで実行する
            var result = await Task.Run(() => _importer.ApplyAsync(
                path, _collection.Id, resolutions, acceptMajorityUnresolved: accept));
            if (result.IsSuccess)
            {
                Result = result.Value;
                OnPropertyChanged(nameof(ResultAdded));
                OnPropertyChanged(nameof(ResultUnchanged));
                OnPropertyChanged(nameof(ResultSkipped));
                OnPropertyChanged(nameof(ResultConflictResolved));
                OnPropertyChanged(nameof(ResultSummaryText));
                Step = 3;
            }
            else
            {
                StatusMessage = result.Message; // 鮮度再検証・過半ガード等。DB は変更されていない
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void BackToFile() => Step = 1;
}
