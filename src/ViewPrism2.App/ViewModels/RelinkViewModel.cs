using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ViewPrism2.App.Services;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;
using ViewPrism2.Core.Services;
using ViewPrism2.Infrastructure.Scanning;

namespace ViewPrism2.App.ViewModels;

/// <summary>
/// 再リンク候補の表示行(REQ-017 + GF-V4-04: サムネイル・ファイル名・相対パス・サイズ・更新日時)。
/// <paramref name="AbsolutePath"/> はサムネイル描画用の物理パス(collection root + relative)。原典
/// AdvancedRepairModal の候補カードは「サムネイル+ファイル名+パス+サイズ+更新日時」でユーザーが再リンク可否を
/// 判断できる情報を提供する(§2.11.5 表示パリティ契約)。旧 RelinkWindow(サムネイル非表示)は既定 null のまま。
/// </summary>
public sealed record RelinkCandidateViewModel(
    RelinkCandidate Candidate, string SizeText, string ModifiedText, string? AbsolutePath = null)
{
    public string RelativePath => Candidate.RelativePath;

    /// <summary>候補のファイル名(正規形パスの末尾要素。INV-005 によりスラッシュ区切り)。</summary>
    public string FileName => Candidate.RelativePath[(Candidate.RelativePath.LastIndexOf('/') + 1)..];
}

/// <summary>リンク切れ画像の表示行。<paramref name="AbsolutePath"/> はサムネイル描画用(欠損時は原則プレースホルダ)。</summary>
public sealed record MissingImageViewModel(ImageRecord Record, string? AbsolutePath = null)
{
    public string RelativePath => Record.RelativePath;

    public string FileName => Record.FileName;
}

/// <summary>
/// 再リンクダイアログ(REQ-017)。missing 画像を選択 → 同一フォルダ・同ハッシュの pending 候補を
/// relative_path 昇順で提示 → 確認(タグ・ノートが missing 側として引き継がれる旨)→ 確定。
/// </summary>
public sealed partial class RelinkViewModel : ObservableObject
{
    private readonly string _folderId;
    private readonly IImageRepository _images;
    private readonly ISyncFolderRepository _folders;
    private readonly RelinkService _relink;
    private readonly LocalizationService _localization;
    private readonly IWindowService _windows;

    /// <summary>collection の物理ルート(サムネイル絶対パス解決用)。LoadAsync で解決する(RepairViewModel と同パターン)。</summary>
    private string? _rootPath;

    public RelinkViewModel(
        string folderId,
        IImageRepository images,
        ISyncFolderRepository folders,
        RelinkService relink,
        LocalizationService localization,
        IWindowService windows)
    {
        _folderId = folderId;
        _images = images;
        _folders = folders;
        _relink = relink;
        _localization = localization;
        _windows = windows;
        Loc = new LocalizationProxy(localization);
        localization.CultureChanged += (_, _) =>
        {
            // DF-3: Loc 差し替えで全文言バインディングを再評価させる(K-AVALONIA の罠対策)
            Loc = new LocalizationProxy(localization);
            OnPropertyChanged(nameof(Loc));
        };
    }

    public LocalizationProxy Loc { get; private set; }

    public ObservableCollection<MissingImageViewModel> MissingImages { get; } = [];

    public ObservableCollection<RelinkCandidateViewModel> Candidates { get; } = [];

    [ObservableProperty]
    private MissingImageViewModel? _selectedMissing;

    [ObservableProperty]
    private RelinkCandidateViewModel? _selectedCandidate;

    [ObservableProperty]
    private string? _statusMessage;

    public bool HasNoMissing => MissingImages.Count == 0;

    public bool HasNoCandidates => Candidates.Count == 0 && SelectedMissing is not null;

    public async Task LoadAsync()
    {
        MissingImages.Clear();
        Candidates.Clear();
        SelectedMissing = null;
        SelectedCandidate = null;

        // collection 物理ルートを解決(候補/missing 行のサムネイル絶対パス用、DC-RELINK-001)。RepairViewModel と同パターン。
        var folder = await _folders.GetByIdAsync(_folderId);
        _rootPath = folder?.Path;

        var records = await _images.GetByFolderAsync(_folderId);
        foreach (var record in records.Where(r => r.Status == ImageStatus.Missing)
                     .OrderBy(r => r.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            MissingImages.Add(new MissingImageViewModel(record, ResolveAbsolute(record.RelativePath)));
        }

        OnPropertyChanged(nameof(HasNoMissing));
        OnPropertyChanged(nameof(HasNoCandidates));
    }

    [RelayCommand]
    private async Task CommitAsync()
    {
        if (SelectedMissing is not { } missing || SelectedCandidate is not { } candidate)
        {
            return;
        }

        // 確定前の確認(REQ-017: タグ・ノートが missing 側の画像として引き継がれる旨)
        if (!await _windows.ConfirmAsync(_localization.T("relink.title"), _localization.T("relink.confirmMessage")))
        {
            return;
        }

        var result = await _relink.CommitRelinkAsync(missing.Record.Id, candidate.Candidate.ImageId);
        if (result.IsSuccess)
        {
            StatusMessage = _localization.T("relink.success");
            await LoadAsync();
        }
        else
        {
            StatusMessage = _localization.T("relink.failed") + ": " + ErrorMessages.Resolve(_localization, result.Error);
        }
    }

    partial void OnSelectedMissingChanged(MissingImageViewModel? value)
    {
        _ = LoadCandidatesAsync(value);
    }

    /// <summary>選択中の missing に対する候補を再読込する(UI バインド・unit 検査の双方から呼べる awaitable 経路)。</summary>
    public Task RefreshCandidatesAsync() => LoadCandidatesAsync(SelectedMissing);

    private async Task LoadCandidatesAsync(MissingImageViewModel? missing)
    {
        Candidates.Clear();
        SelectedCandidate = null;
        if (missing is not null)
        {
            // 候補列挙は relative_path 昇順(REQ-017。RelinkService 側で整列済み)。
            // DC-RELINK-001(ECO-004): 候補カードはサムネイル+ファイル名+パス+サイズ+更新日時を提示する
            // (RepairWindow と同型。AbsolutePath は collection root + relative。物理 I/O なしの文字列結合)。
            foreach (var candidate in await _relink.GetCandidatesAsync(missing.Record.Id))
            {
                Candidates.Add(new RelinkCandidateViewModel(
                    candidate,
                    ByteSizeFormatter.Format(candidate.FileSize),
                    LocaleFormats.FormatTimestamp(candidate.ModifiedDate, _localization.CurrentLocale),
                    ResolveAbsolute(candidate.RelativePath)));
            }
        }

        OnPropertyChanged(nameof(HasNoCandidates));
    }

    /// <summary>
    /// 正規形(スラッシュ区切り)の相対パスを物理絶対パスへ解決する(サムネイル描画用、DC-RELINK-001)。
    /// ルート未解決なら null(ThumbnailImage はプレースホルダ表示)。物理 I/O はしない純粋な文字列結合。
    /// </summary>
    private string? ResolveAbsolute(string relativePath)
        => _rootPath is null
            ? null
            : System.IO.Path.Combine(_rootPath, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
}
