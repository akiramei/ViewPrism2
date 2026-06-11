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

/// <summary>再リンク候補の表示行(REQ-017: 相対パス・ファイルサイズ・更新日時を表示)。</summary>
public sealed record RelinkCandidateViewModel(RelinkCandidate Candidate, string SizeText, string ModifiedText)
{
    public string RelativePath => Candidate.RelativePath;
}

/// <summary>リンク切れ画像の表示行。</summary>
public sealed record MissingImageViewModel(ImageRecord Record)
{
    public string RelativePath => Record.RelativePath;
}

/// <summary>
/// 再リンクダイアログ(REQ-017)。missing 画像を選択 → 同一フォルダ・同ハッシュの pending 候補を
/// relative_path 昇順で提示 → 確認(タグ・ノートが missing 側として引き継がれる旨)→ 確定。
/// </summary>
public sealed partial class RelinkViewModel : ObservableObject
{
    private readonly string _folderId;
    private readonly IImageRepository _images;
    private readonly RelinkService _relink;
    private readonly LocalizationService _localization;
    private readonly IWindowService _windows;

    public RelinkViewModel(
        string folderId,
        IImageRepository images,
        RelinkService relink,
        LocalizationService localization,
        IWindowService windows)
    {
        _folderId = folderId;
        _images = images;
        _relink = relink;
        _localization = localization;
        _windows = windows;
        Loc = new LocalizationProxy(localization);
    }

    public LocalizationProxy Loc { get; }

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

        var records = await _images.GetByFolderAsync(_folderId);
        foreach (var record in records.Where(r => r.Status == ImageStatus.Missing)
                     .OrderBy(r => r.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            MissingImages.Add(new MissingImageViewModel(record));
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

    private async Task LoadCandidatesAsync(MissingImageViewModel? missing)
    {
        Candidates.Clear();
        SelectedCandidate = null;
        if (missing is not null)
        {
            // 候補列挙は relative_path 昇順(REQ-017。RelinkService 側で整列済み)
            foreach (var candidate in await _relink.GetCandidatesAsync(missing.Record.Id))
            {
                Candidates.Add(new RelinkCandidateViewModel(
                    candidate,
                    ByteSizeFormatter.Format(candidate.FileSize),
                    LocaleFormats.FormatTimestamp(candidate.ModifiedDate, _localization.CurrentLocale)));
            }
        }

        OnPropertyChanged(nameof(HasNoCandidates));
    }
}
