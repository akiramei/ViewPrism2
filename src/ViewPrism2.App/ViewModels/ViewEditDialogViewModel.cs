using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;

namespace ViewPrism2.App.ViewModels;

/// <summary>
/// ビュー作成/編集ダイアログ(仕様 §2.6 v1.2: 名前(必須)+説明 / お気に入り REQ-030/033)+
/// ECO-025 α: 表示列の構成。列ピッカーは共通部品 <see cref="ColumnPickerViewModel"/>(SC-COLUMN-PICKER-001)へ委譲し、
/// 本 VM は名前/説明/お気に入りと保存(検証・modified_at は ViewService)を担う。保存時に <see cref="ColumnPickerViewModel.Serialize"/>
/// で display_columns を書き戻す(VE-003)。α は追加元 2 カラム表示(BasicSources/TagSources)・保存で反映(β-2 のライブ編集とは導線のみ差)。
/// </summary>
public sealed partial class ViewEditDialogViewModel : ObservableObject
{
    private readonly ViewService _views;
    private readonly LocalizationService _localization;
    private readonly View? _existing;

    public ViewEditDialogViewModel(
        View? existing,
        IReadOnlyList<Tag> viewTags,
        ViewService views,
        LocalizationService localization)
    {
        _existing = existing;
        _views = views;
        _localization = localization;
        Columns = new ColumnPickerViewModel(existing?.DisplayColumns, viewTags, localization);

        Loc = new LocalizationProxy(localization);
        localization.CultureChanged += (_, _) =>
        {
            // DF-3: Loc 差し替えで全文言バインディングを再評価させる(K-AVALONIA の罠対策)。列ピッカーの行ラベルも作り直す。
            Loc = new LocalizationProxy(localization);
            OnPropertyChanged(nameof(Loc));
            Columns.RefreshLabels();
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

    /// <summary>表示列の列ピッカー(SC-COLUMN-PICKER-001・ファイル一覧「表示列」ポップオーバーと共通)。</summary>
    public ColumnPickerViewModel Columns { get; }

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
        var displayColumns = Columns.Serialize();

        if (_existing is null)
        {
            var created = await _views.CreateAsync(
                Name, IsFavorite, displayColumns: displayColumns, description: description);
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
                DisplayColumns = displayColumns,
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

/// <summary>選択済み列の 1 行(SC-COLUMN-PICKER-001・列ピッカー)。name は固定(移動/削除不可・「固定」バッジ)。</summary>
public sealed class SelectedColumnRow
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required string SourceLabel { get; init; }
    public string? Color { get; init; }
    public bool IsTag { get; init; }
    public bool IsNameLocked { get; init; }
    public bool CanMoveUp { get; init; }
    public bool CanMoveDown { get; init; }

    /// <summary>色ドット表示(タグ列で色ありのとき)。</summary>
    public bool ShowColorDot => IsTag && Color is not null;
}

/// <summary>追加元カードの 1 件(基本情報=破線 / ビュー内タグ=実線・種別チップ+色ドット)。</summary>
public sealed class AddSourceRow
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public string? KindLabel { get; init; }
    public string? Color { get; init; }
    public bool IsTag { get; init; }

    /// <summary>色ドット表示(タグ列で色ありのとき)。</summary>
    public bool ShowColorDot => IsTag && Color is not null;
}
