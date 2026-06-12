using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;

namespace ViewPrism2.App.ViewModels;

/// <summary>
/// ビュー作成/編集ダイアログ(仕様 §2.6 v1.2: 名前(必須)+説明)。
/// お気に入りフラグも併せて編集する(REQ-030/033)。検証・modified_at 規則は ViewService(core)に委譲。
/// </summary>
public sealed partial class ViewEditDialogViewModel : ObservableObject
{
    private readonly ViewService _views;
    private readonly LocalizationService _localization;
    private readonly View? _existing;

    public ViewEditDialogViewModel(View? existing, ViewService views, LocalizationService localization)
    {
        _existing = existing;
        _views = views;
        _localization = localization;
        Loc = new LocalizationProxy(localization);
        localization.CultureChanged += (_, _) =>
        {
            // DF-3: Loc 差し替えで全文言バインディングを再評価させる(K-AVALONIA の罠対策)
            Loc = new LocalizationProxy(localization);
            OnPropertyChanged(nameof(Loc));
        };

        if (existing is not null)
        {
            _name = existing.Name;
            _description = existing.Description ?? string.Empty;
            _isFavorite = existing.IsFavorite;
        }
    }

    public LocalizationProxy Loc { get; private set; }

    public bool IsCreate => _existing is null;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>保存成功(ウィンドウが閉じる)。</summary>
    public event EventHandler? Saved;

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = null;
        var description = string.IsNullOrWhiteSpace(Description) ? null : Description;

        if (_existing is null)
        {
            var created = await _views.CreateAsync(Name, IsFavorite, description: description);
            if (!created.IsSuccess)
            {
                ErrorMessage = ErrorMessages.Resolve(_localization, created.Error);
                return;
            }
        }
        else
        {
            var updated = await _views.UpdateAsync(_existing with
            {
                Name = Name,
                Description = description,
                IsFavorite = IsFavorite,
            });
            if (!updated.IsSuccess)
            {
                ErrorMessage = ErrorMessages.Resolve(_localization, updated.Error);
                return;
            }
        }

        Saved?.Invoke(this, EventArgs.Empty);
    }
}
