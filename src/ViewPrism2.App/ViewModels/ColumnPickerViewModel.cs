using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;

namespace ViewPrism2.App.ViewModels;

/// <summary>
/// 表示列ポップオーバーの列ピッカー(ECO-025 β-2・ファイル一覧「表示列」)。
/// ビュー編集モーダル(α)と<b>同一の決定論モデル</b> <see cref="ViewColumnModel"/> を消費し、
/// 追加/削除/並べ替えは即時にモデルへ反映して <see cref="Changed"/> を発火する(VE-003=ビュー定義へ書き戻し・ライブ編集)。
///
/// α との差(mock 権威 file_list.md):
/// - 追加元は<b>単一リスト</b>(基本情報+タグ混在・各カードに種別チップ「基本/数値/テキスト/シンプル」)。α は 2 カラム分割。
/// - 保存/キャンセルの<b>フッターを持たない</b>(編集は即反映)。
/// 名前固定(VE-001)・上限5(VE-002)はモデルが担保する。
///
/// DRY(SC-COLUMN-PICKER-001)は α/β-2 が golden 後に本 VM と <see cref="ViewEditDialogViewModel"/> の
/// 列ピッカー部を共通化する予定(行 DTO <see cref="SelectedColumnRow"/>/<see cref="AddSourceRow"/> は既に共有)。
/// </summary>
public sealed partial class ColumnPickerViewModel : ObservableObject
{
    private readonly LocalizationService _localization;
    private readonly ViewColumnModel _columns;
    private readonly Dictionary<string, Tag> _viewTagById;

    public ColumnPickerViewModel(
        string? displayColumns,
        IReadOnlyList<Tag> viewTags,
        LocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(viewTags);
        _localization = localization;
        _viewTagById = viewTags.ToDictionary(t => t.Id, StringComparer.Ordinal);
        _columns = ViewColumnModel.Create(displayColumns, viewTags);
        Rebuild();
    }

    /// <summary>選択済み列(順序どおり・先頭は name の固定列・上/下/削除)。</summary>
    public ObservableCollection<SelectedColumnRow> SelectedColumns { get; } = [];

    /// <summary>追加元(単一リスト・基本情報→タグの順・各カードに種別チップ+タグ色ドット)。β-2 は α と違い1列。</summary>
    public ObservableCollection<AddSourceRow> AvailableColumns { get; } = [];

    // ---- ローカライズ済みラベル(ポップオーバーは開くたびに再生成される短命 VM のため getter で解決) ----
    public string SectionLabel => _localization.T("view.displayColumns");
    public string ToFileListNote => _localization.T("view.displayColumnsToFileList");
    public string AddColumnLabel => _localization.T("filelist.addColumn");
    public string NoAvailableLabel => _localization.T("filelist.noAvailableColumns");
    public string FixedLabel => _localization.T("view.columnFixed");

    /// <summary>件数バッジ「合計 N 列 / 最大 M」。</summary>
    public string ColumnCountText => _localization.T("view.columnCount", new Dictionary<string, string>
    {
        ["count"] = SelectedColumns.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["max"] = ViewColumnModel.MaxColumns.ToString(System.Globalization.CultureInfo.InvariantCulture),
    });

    /// <summary>上限到達(件数バッジのアンバー化・追加元カードの不活性・アンバー警告・VE-002)。</summary>
    public bool IsAtLimit => _columns.AtLimit;

    /// <summary>上限アンバー警告文「最大 N 列です。追加するには列を削除してください。」。</summary>
    public string AtMaxNote => _localization.T("filelist.atMaxNote", new Dictionary<string, string>
    {
        ["max"] = ViewColumnModel.MaxColumns.ToString(System.Globalization.CultureInfo.InvariantCulture),
    });

    /// <summary>追加できる列があるか(なければ「追加できる列はありません」)。</summary>
    public bool HasAvailable => AvailableColumns.Count > 0;

    /// <summary>上限未達 かつ 追加元なし=「追加できる列はありません」を出す(上限時はアンバー警告を優先)。</summary>
    public bool ShowNoAvailable => !IsAtLimit && AvailableColumns.Count == 0;

    /// <summary>編集(追加/削除/並べ替え)が起きたら発火(ホストが display_columns を書き戻す・VE-003)。</summary>
    public event EventHandler? Changed;

    /// <summary>現在の列構成を display_columns(JSON)へ直列化(DisplayColumnParser 互換)。</summary>
    public string Serialize() => _columns.Serialize();

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
            Rebuild();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    private void RemoveColumn(SelectedColumnRow? row)
    {
        if (row is not null && _columns.Remove(row.Key))
        {
            Rebuild();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    private void MoveColumnUp(SelectedColumnRow? row)
    {
        if (row is not null && _columns.MoveUp(row.Key))
        {
            Rebuild();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    private void MoveColumnDown(SelectedColumnRow? row)
    {
        if (row is not null && _columns.MoveDown(row.Key))
        {
            Rebuild();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>モデルから選択済み列・追加元カードの表示コレクションを作り直す(列は最大5+追加元少数=軽量)。</summary>
    private void Rebuild()
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

        // 追加元=単一リスト。基本情報→タグの順(α の 2 カラムを縦一列へ)。基本情報にも種別チップ「基本」を付ける。
        AvailableColumns.Clear();
        foreach (var column in _columns.AvailableBasics)
        {
            AvailableColumns.Add(new AddSourceRow
            {
                Key = column.Key,
                Label = LabelFor(column),
                KindLabel = _localization.T("view.columnChipBasic"),
                IsTag = false,
            });
        }

        foreach (var column in _columns.AvailableTags)
        {
            AvailableColumns.Add(new AddSourceRow
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
        OnPropertyChanged(nameof(AtMaxNote));
        OnPropertyChanged(nameof(HasAvailable));
        OnPropertyChanged(nameof(ShowNoAvailable));
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
