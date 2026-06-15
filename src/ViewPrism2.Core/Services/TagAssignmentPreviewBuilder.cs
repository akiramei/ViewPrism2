using System.Globalization;
using ViewPrism2.Core.Models;

namespace ViewPrism2.Core.Services;

/// <summary>タグ付与プレビューの種別(DC-TAGPREVIEW-001)。</summary>
public enum TagPreviewKind
{
    /// <summary>シンプル: 値なし(色ドット+名前のみ)。</summary>
    Simple,

    /// <summary>テキスト: 候補値チップ(選択強調)。</summary>
    Textual,

    /// <summary>数値・★モード: ★並び(単位=★ かつ span 0..9 整数)。</summary>
    NumericStar,

    /// <summary>数値・プレーン: ±ステッパ+数値ラベル。</summary>
    NumericPlain,
}

/// <summary>テキスト型プレビューの候補値チップ 1 件(選択強調可)。</summary>
public sealed record TagPreviewChip(string Label, bool IsSelected);

/// <summary>
/// タグ作成/編集ダイアログの「画像に付けたときの見え方」プレビュー(DC-TAGPREVIEW-001)。
/// 種別別に、画像タブ実付与 UI(E-UI-TAGASSIGN-029)と一致する付与表現を表す純粋データ。
/// pixel-exact は不採用(視覚は golden)。整形ロジックは TagAssignmentPreviewBuilder。
/// </summary>
public sealed record TagAssignmentPreview
{
    public required TagPreviewKind Kind { get; init; }

    /// <summary>タグ名(空白のみ/null はプレースホルダに置換済み)。</summary>
    public required string Name { get; init; }

    /// <summary>色 hex(null/空は色なし)。色ドットの塗り。</summary>
    public string? Color { get; init; }

    /// <summary>テキスト型: 候補値チップ(predefined_values 順、選択強調)。それ以外は空。</summary>
    public IReadOnlyList<TagPreviewChip> Chips { get; init; } = [];

    /// <summary>★モード: 点灯★の数(min..max のうち representativeValue 以下)。</summary>
    public int FilledStars { get; init; }

    /// <summary>★モード: ★の総数(max - min + 1)。</summary>
    public int TotalStars { get; init; }

    /// <summary>数値型(★/プレーン共通): 値+単位の表示ラベル(例: "4 ★" / "50 %")。</summary>
    public string NumericLabel { get; init; } = string.Empty;
}

/// <summary>
/// タグ作成/編集ダイアログの付与プレビュー整形器(DC-TAGPREVIEW-001・E5、仕様 §2.6)。
/// 「画像に付けたときの見え方」を種別別に純粋計算する。画像タブ実付与 UI(E-UI-TAGASSIGN-029)と
/// 付与表現を一致させる: シンプル=色ドット+名前 / テキスト=候補値チップ(先頭=選択) /
/// 数値=★モード(単位=★ かつ範囲 span 0..9 の整数)または ±ステッパ+数値。
/// pixel-exact は不採用(視覚は golden)。CP-DISPLAY-PARITY-022 / DC-TAGPREVIEW-001 unit 検査対象。
/// </summary>
public static class TagAssignmentPreviewBuilder
{
    /// <summary>名前が空のときのプレースホルダ(モック準拠: 'タグ名')。i18n キーで供給する。</summary>
    public const string NamePlaceholderKey = "tag.preview.namePlaceholder";

    /// <summary>★モード判定の単位文字列。</summary>
    public const string StarUnit = "★";

    /// <summary>
    /// プレビューを組み立てる。
    /// <paramref name="name"/> が空白のみ/null のときは <paramref name="namePlaceholder"/> を用いる。
    /// </summary>
    /// <param name="type">タグ種別。</param>
    /// <param name="name">タグ名(空はプレースホルダへ)。</param>
    /// <param name="color">色 hex(null/空は色なし)。</param>
    /// <param name="predefinedValues">textual の候補値(順序保持)。</param>
    /// <param name="numeric">numeric 設定(min/max/step/unit)。</param>
    /// <param name="namePlaceholder">名前が空のときの代替表示(i18n 解決済み)。</param>
    public static TagAssignmentPreview Build(
        TagType type,
        string? name,
        string? color,
        IReadOnlyList<string>? predefinedValues,
        NumericTagSettings? numeric,
        string namePlaceholder)
    {
        ArgumentNullException.ThrowIfNull(namePlaceholder);

        var displayName = string.IsNullOrWhiteSpace(name) ? namePlaceholder : name.Trim();
        var displayColor = string.IsNullOrWhiteSpace(color) ? null : color.Trim();

        return type switch
        {
            TagType.Textual => BuildTextual(displayName, displayColor, predefinedValues),
            TagType.Numeric => BuildNumeric(displayName, displayColor, numeric),
            _ => new TagAssignmentPreview
            {
                Kind = TagPreviewKind.Simple,
                Name = displayName,
                Color = displayColor,
            },
        };
    }

    private static TagAssignmentPreview BuildTextual(
        string name, string? color, IReadOnlyList<string>? predefinedValues)
    {
        var values = predefinedValues ?? [];
        var chips = new List<TagPreviewChip>(values.Count);
        for (var i = 0; i < values.Count; i++)
        {
            // モック準拠: 先頭候補を選択強調(画像に付けた既定の見え方)
            chips.Add(new TagPreviewChip(values[i], IsSelected: i == 0));
        }

        return new TagAssignmentPreview
        {
            Kind = TagPreviewKind.Textual,
            Name = name,
            Color = color,
            Chips = chips,
        };
    }

    private static TagAssignmentPreview BuildNumeric(
        string name, string? color, NumericTagSettings? numeric)
    {
        // モック準拠の既定: min=0 / max=100(未設定時)。unit は空文字。
        var min = numeric?.Min ?? 0d;
        var max = numeric?.Max ?? 100d;
        var unit = numeric?.Unit ?? string.Empty;
        var span = max - min;

        // ★モード: 単位=★ かつ span 0..9 の整数(min/max とも整数)。
        var isStar = string.Equals(unit, StarUnit, StringComparison.Ordinal)
            && span >= 0 && span <= 9
            && IsInteger(min) && IsInteger(max);

        // 代表値: 範囲中点(preset と同じ Math.Round)。[min,max] にクランプ。
        var representative = Math.Max(min, Math.Min(max, Math.Round((min + max) / 2)));
        var numericLabel = FormatNumericLabel(representative, unit);

        if (isStar)
        {
            var minI = (int)min;
            var maxI = (int)max;
            var repI = (int)representative;
            var total = maxI - minI + 1;
            // 点灯★: min..max のうち representative 以下(モック: on = v <= numValue)
            var filled = 0;
            for (var v = minI; v <= maxI; v++)
            {
                if (v <= repI)
                {
                    filled++;
                }
            }

            return new TagAssignmentPreview
            {
                Kind = TagPreviewKind.NumericStar,
                Name = name,
                Color = color,
                FilledStars = filled,
                TotalStars = total,
                NumericLabel = numericLabel,
            };
        }

        return new TagAssignmentPreview
        {
            Kind = TagPreviewKind.NumericPlain,
            Name = name,
            Color = color,
            NumericLabel = numericLabel,
        };
    }

    private static bool IsInteger(double v) => Math.Abs(v - Math.Round(v)) < 1e-9;

    /// <summary>数値ラベル: 整数はそのまま・小数は 1 桁 + 単位(非空時 " {unit}")。INV-007 不変表現。</summary>
    private static string FormatNumericLabel(double value, string unit)
    {
        var number = IsInteger(value)
            ? ((long)Math.Round(value)).ToString(CultureInfo.InvariantCulture)
            : value.ToString("0.0", CultureInfo.InvariantCulture);
        return unit.Length > 0 ? $"{number} {unit}" : number;
    }
}
