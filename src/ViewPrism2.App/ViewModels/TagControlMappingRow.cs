using CommunityToolkit.Mvvm.Input;
using ViewPrism2.Core.Services.Viewer;

namespace ViewPrism2.App.ViewModels;

/// <summary>
/// タグ制御マッピング picker の 1 選択肢(作成済みタグ。色ドット+ラベル)。ECO-022・§2.12.6。
/// <paramref name="IsSelected"/>=この行(アクション)に現在割当済(選択✓ GF-TAGCTRL-05 D5)。
/// <paramref name="UsedElsewhere"/>=他アクションが同タグを使用中(「使用中」バッジ D4)。
/// いずれも行ごとに算出されるため、供給時(SetAvailableTags)は既定 false で、RebuildTagActionRows が行別に補正する。
/// </summary>
public sealed record TagPickerOption(
    string Id,
    string Name,
    string? Color,
    bool IsSelected = false,
    bool UsedElsewhere = false);

/// <summary>
/// タグ制御マッピングモーダルの 1 行(予約アクション)。表示契約(§2.12.6・M-UI-018 tagctrl_ui):
/// [グリフ / アクション名+英名+説明 / 割り当てるタグ picker(色ドット+ラベル or 未割り当て)]。
/// picker メニュー=作成済みタグ一覧(現存のみ・major-1 補正)+「未割り当て(クリア)」。
/// </summary>
public sealed partial class TagControlMappingRow
{
    private readonly ViewerViewModel _owner;

    public TagControlMappingRow(
        ViewerViewModel owner,
        ViewerTagAction action,
        string glyph,
        string iconBg,
        string iconFg,
        string name,
        string englishName,
        string description,
        TagPickerOption? assigned,
        IReadOnlyList<TagPickerOption> options,
        string unassignedLabel)
    {
        _owner = owner;
        Action = action;
        Glyph = glyph;
        IconBg = iconBg;
        IconFg = iconFg;
        Name = name;
        EnglishName = englishName;
        Description = description;
        Assigned = assigned;
        Options = options;
        UnassignedLabel = unassignedLabel;
    }

    public ViewerTagAction Action { get; }

    /// <summary>アクションのグリフ(視覚記号)。</summary>
    public string Glyph { get; }

    /// <summary>アクションアイコンバッジの地色(モック準拠・アクション固定の淡色。hex)。</summary>
    public string IconBg { get; }

    /// <summary>アクションアイコンバッジのグリフ色(hex)。</summary>
    public string IconFg { get; }

    /// <summary>アクション名(ローカライズ済み)。</summary>
    public string Name { get; }

    /// <summary>英名(monospace 表示・予約アクション ID)。</summary>
    public string EnglishName { get; }

    /// <summary>アクションの説明(ローカライズ済み)。</summary>
    public string Description { get; }

    /// <summary>現在割り当て中のタグ(null=未割り当て)。</summary>
    public TagPickerOption? Assigned { get; }

    /// <summary>picker メニューの選択肢(作成済みの現存タグのみ)。</summary>
    public IReadOnlyList<TagPickerOption> Options { get; }

    /// <summary>未割り当ての表示ラベル。</summary>
    public string UnassignedLabel { get; }

    /// <summary>picker の表示テキスト(割当タグ名 or 未割り当て)。</summary>
    public string PickerText => Assigned?.Name ?? UnassignedLabel;

    /// <summary>picker の色ドット色(未割り当て時 null)。</summary>
    public string? PickerColor => Assigned?.Color;

    public bool HasAssignment => Assigned is not null;

    /// <summary>タグを割り当てる(picker のタグ選択)。即時保存→plan 再計算(REQ-077/078)。</summary>
    [RelayCommand]
    private void Assign(TagPickerOption? option) => _owner.SetTagActionMapping(Action, option?.Id);

    /// <summary>割り当てをクリア(未割り当てへ)。</summary>
    [RelayCommand]
    private void Clear() => _owner.SetTagActionMapping(Action, null);
}
