using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;

namespace ViewPrism2.App.ViewModels;

/// <summary>
/// ビュー作成/編集ダイアログ(仕様 §2.6 v1.2: 名前(必須)+説明 / お気に入り REQ-030/033)+
/// ECO-025 α: 表示列の構成(display_columns の進化モデル・列ピッカー)。
/// 検証・modified_at 規則は ViewService(core)に委譲。列モデルの決定論ロジックは <see cref="ViewColumnModel"/>。
/// </summary>
public sealed partial class ViewEditDialogViewModel : ObservableObject
{
    private readonly ViewService _views;
    private readonly LocalizationService _localization;
    private readonly View? _existing;
    private readonly ViewColumnModel _columns;
    private readonly Dictionary<string, Tag> _viewTagById;

    public ViewEditDialogViewModel(
        View? existing,
        IReadOnlyList<Tag> viewTags,
        ViewService views,
        LocalizationService localization)
    {
        _existing = existing;
        _views = views;
        _localization = localization;
        _viewTagById = viewTags.ToDictionary(t => t.Id, StringComparer.Ordinal);
        _columns = ViewColumnModel.Create(existing?.DisplayColumns, viewTags);

        Loc = new LocalizationProxy(localization);
        localization.CultureChanged += (_, _) =>
        {
            // DF-3: Loc 差し替えで全文言バインディングを再評価させる(K-AVALONIA の罠対策)
            Loc = new LocalizationProxy(localization);
            OnPropertyChanged(nameof(Loc));
            RebuildColumns();
        };

        if (existing is not null)
        {
            _name = existing.Name;
            _description = existing.Description ?? string.Empty;
            _isFavorite = existing.IsFavorite;
        }

        RebuildColumns();
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

    // ---- 表示列の構成(ECO-025 α・REQ-080) ----

    /// <summary>選択済み列(順序どおり・先頭は常に name の固定列)。</summary>
    public ObservableCollection<SelectedColumnRow> SelectedColumns { get; } = [];

    /// <summary>追加元の基本情報(破線カード・未選択のみ)。</summary>
    public ObservableCollection<AddSourceRow> BasicSources { get; } = [];

    /// <summary>追加元のビュー内タグ(実線カード・未選択のみ・母集合=ビューのタグ階層メンバー)。</summary>
    public ObservableCollection<AddSourceRow> TagSources { get; } = [];

    /// <summary>件数バッジ「合計 N 列 / 最大 M」。</summary>
    public string ColumnCountText => _localization.T("view.columnCount", new Dictionary<string, string>
    {
        ["count"] = SelectedColumns.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["max"] = ViewColumnModel.MaxColumns.ToString(System.Globalization.CultureInfo.InvariantCulture),
    });

    /// <summary>上限到達(件数バッジのアンバー化・追加元カードの不活性・VE-002)。</summary>
    public bool IsAtLimit => _columns.AtLimit;

    public bool HasBasicSources => BasicSources.Count > 0;

    public bool HasTagSources => TagSources.Count > 0;

    /// <summary>保存成功(ウィンドウが閉じる)。</summary>
    public event EventHandler? Saved;

    [RelayCommand]
    private void AddColumn(AddSourceRow? source)
    {
        if (source is null)
        {
            return;
        }

        var column = source.IsTag && _viewTagById.TryGetValue(source.Key, out var tag)
            ? new ViewColumn(tag.Id, ColumnSource.Tag, 1, tag.Color, tag.Type)
            : new ViewColumn(source.Key, ColumnSource.Basic, source.Key == ViewColumnModel.NameKey ? 2 : 1);

        if (_columns.Add(column))
        {
            RebuildColumns();
        }
    }

    [RelayCommand]
    private void RemoveColumn(SelectedColumnRow? row)
    {
        if (row is not null && _columns.Remove(row.Key))
        {
            RebuildColumns();
        }
    }

    [RelayCommand]
    private void MoveColumnUp(SelectedColumnRow? row)
    {
        if (row is not null && _columns.MoveUp(row.Key))
        {
            RebuildColumns();
        }
    }

    [RelayCommand]
    private void MoveColumnDown(SelectedColumnRow? row)
    {
        if (row is not null && _columns.MoveDown(row.Key))
        {
            RebuildColumns();
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = null;
        var description = string.IsNullOrWhiteSpace(Description) ? null : Description;
        var displayColumns = _columns.Serialize();

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

    /// <summary>モデルから選択済み列・追加元カードの表示コレクションを作り直す(列は最大5+追加元少数=軽量)。</summary>
    private void RebuildColumns()
    {
        SelectedColumns.Clear();
        var selected = _columns.Selected;
        for (var i = 0; i < selected.Count; i++)
        {
            var column = selected[i];
            SelectedColumns.Add(new SelectedColumnRow
            {
                Key = column.Key,
                Label = LabelFor(column),
                SourceLabel = column.Source == ColumnSource.Tag
                    ? _localization.T("view.columnChipTag")
                    : _localization.T("view.columnChipBasic"),
                Color = column.Color,
                IsTag = column.Source == ColumnSource.Tag,
                IsNameLocked = column.IsNameLocked,
                // name(index 0)は動かさない。index 1 は name の直後で上へ動けない(VE-001)。
                CanMoveUp = i > 1,
                CanMoveDown = i > 0 && i < selected.Count - 1,
            });
        }

        BasicSources.Clear();
        foreach (var column in _columns.AvailableBasics)
        {
            BasicSources.Add(new AddSourceRow
            {
                Key = column.Key,
                Label = LabelFor(column),
                IsTag = false,
            });
        }

        TagSources.Clear();
        foreach (var column in _columns.AvailableTags)
        {
            TagSources.Add(new AddSourceRow
            {
                Key = column.Key,
                Label = LabelFor(column),
                KindLabel = KindLabel(column.TagType),
                Color = column.Color,
                IsTag = true,
            });
        }

        OnPropertyChanged(nameof(ColumnCountText));
        OnPropertyChanged(nameof(IsAtLimit));
        OnPropertyChanged(nameof(HasBasicSources));
        OnPropertyChanged(nameof(HasTagSources));
    }

    private string LabelFor(ViewColumn column)
    {
        if (column.Source == ColumnSource.Tag)
        {
            return _viewTagById.TryGetValue(column.Key, out var tag) ? tag.Name : column.Key;
        }

        return column.Key switch
        {
            ViewColumnModel.NameKey => _localization.T("common.name"),
            ViewColumnModel.SizeKey => _localization.T("common.size"),
            ViewColumnModel.ModifiedDateKey => _localization.T("common.modifiedDate"),
            _ => column.Key,
        };
    }

    private string? KindLabel(TagType? type) => type switch
    {
        TagType.Numeric => _localization.T("tag.type.numeric"),
        TagType.Textual => _localization.T("tag.type.textual"),
        TagType.Simple => _localization.T("tag.type.simple"),
        _ => null,
    };
}

/// <summary>選択済み列の 1 行(ECO-025 α・列ピッカー)。name は固定(移動/削除不可・「固定」バッジ)。</summary>
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
