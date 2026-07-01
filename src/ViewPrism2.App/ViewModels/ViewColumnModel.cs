using System.Text.Json;
using ViewPrism2.Core.Models;

namespace ViewPrism2.App.ViewModels;

/// <summary>表示列の由来(ECO-025/REQ-079)。basic=基本情報 / tag=ビュー内タグ。</summary>
public enum ColumnSource
{
    Basic,
    Tag,
}

/// <summary>
/// 表示列 1 件(編集モデル・ECO-025/REQ-079)。
/// name(c_name)は固定列(先頭・削除/移動不可・VE-001)。
/// <see cref="TagType"/> は tag 列の kind(num/text/simple)導出用(β のセル描画/ソートが参照)。
/// </summary>
public sealed record ViewColumn(
    string Key,
    ColumnSource Source,
    double Star,
    string? Color = null,
    TagType? TagType = null)
{
    /// <summary>名前固定列か(先頭固定・削除/移動不可・「固定」バッジ・VE-001)。</summary>
    public bool IsNameLocked => Source == ColumnSource.Basic && Key == ViewColumnModel.NameKey;
}

/// <summary>
/// ビューの表示列 <c>display_columns</c> の進化モデル(ECO-025/REQ-079・純粋/決定論・unit 検査可能)。
///
/// 不変条件:
/// - VE-001 先頭は常に name(c_name)。削除/移動不可(<see cref="Remove"/>/<see cref="MoveUp"/>/<see cref="MoveDown"/> は name を動かさない)。
/// - VE-002 列数は 1..<see cref="MaxColumns"/>(既定 5)。上限で <see cref="AtLimit"/>=true・追加は無視。
/// - VE-003 columns はビュー定義所有。本モデルは <see cref="Serialize"/> で display_columns(JSON)へ書き戻す。
/// - タグ列候補の母集合は当該ビューのタグ階層メンバーシップに限る(<see cref="AvailableTags"/>)。
/// - kind は basic=name/size/modified_date、tag は タグ型→num/text/simple。Ver1「種類」列は無い(basic/tag のみ)。
///
/// 永続 JSON は既存スキーマ <c>{type,key,label?,width}</c>(<see cref="DisplayColumnParser"/> 互換)。
/// kind は tag 型から描画時に導出するため直列化しない(前方互換・DB マイグレーション不要)。
/// </summary>
public sealed class ViewColumnModel
{
    public const int MaxColumns = 5;
    public const string NameKey = "name";
    public const string SizeKey = "size";
    public const string ModifiedDateKey = "modified_date";

    /// <summary>基本情報キー(表示順)。name は固定列(VE-001)。</summary>
    public static readonly IReadOnlyList<string> BasicKeys = new[] { NameKey, SizeKey, ModifiedDateKey };

    private readonly List<ViewColumn> _selected;
    private readonly List<ViewColumn> _tagPool;

    private ViewColumnModel(List<ViewColumn> selected, List<ViewColumn> tagPool)
    {
        _selected = selected;
        _tagPool = tagPool;
    }

    /// <summary>現在の選択済み列(順序どおり・先頭は常に name)。</summary>
    public IReadOnlyList<ViewColumn> Selected => _selected;

    /// <summary>列数の上限に達しているか(VE-002)。追加元カードの不活性・件数バッジのアンバー化に使う。</summary>
    public bool AtLimit => _selected.Count >= MaxColumns;

    /// <summary>まだ選択されていない基本情報列(追加元・破線カード)。name は常に選択済みのため出ない。</summary>
    public IReadOnlyList<ViewColumn> AvailableBasics =>
        BasicColumns()
            .Where(c => !ContainsKey(c.Key))
            .ToList();

    /// <summary>まだ選択されていないビュー内タグ列(追加元・実線カード)。母集合=ビューのタグ階層メンバー。</summary>
    public IReadOnlyList<ViewColumn> AvailableTags =>
        _tagPool
            .Where(c => !ContainsKey(c.Key))
            .ToList();

    /// <summary>
    /// 既存 display_columns(JSON)+ ビューのタグ母集合から編集モデルを構築する。
    /// 正規化: 不明/削除タグ列・非 basic キーを捨て、重複を除き、name を先頭へ固定し、上限で切り詰める(VE-001/002)。
    /// </summary>
    /// <param name="json">views.display_columns(null/空/破損は既定=name/size/modified_date)。</param>
    /// <param name="viewTags">当該ビューのタグ階層メンバー(重複可・出現順を保持)。</param>
    public static ViewColumnModel Create(string? json, IReadOnlyList<Tag> viewTags)
    {
        ArgumentNullException.ThrowIfNull(viewTags);

        // タグ母集合(出現順で distinct・色/型を保持)
        var tagPool = new List<ViewColumn>();
        var poolKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tag in viewTags)
        {
            if (poolKeys.Add(tag.Id))
            {
                tagPool.Add(new ViewColumn(tag.Id, ColumnSource.Tag, 1, tag.Color, tag.Type));
            }
        }

        var tagByKey = tagPool.ToDictionary(c => c.Key, StringComparer.Ordinal);

        // 既存 JSON から有効列(basic name/size/modified_date + 母集合内タグ)だけを順に取り出す。
        // 不明 basic キー・削除/範囲外タグは捨てる(DisplayColumnParser と同方針)。
        var valid = new List<ViewColumn>();
        foreach (var (type, key) in ParseKeys(json))
        {
            if (type == ColumnSource.Basic && (key == NameKey || key == SizeKey || key == ModifiedDateKey))
            {
                valid.Add(new ViewColumn(key, ColumnSource.Basic, DefaultStar(key)));
            }
            else if (type == ColumnSource.Tag && tagByKey.TryGetValue(key, out var tagCol))
            {
                valid.Add(tagCol);
            }
        }

        List<ViewColumn> selected;
        if (valid.Count == 0)
        {
            // null/破損/有効列なし → 既定 3 列(name/size/modified_date・DisplayColumnParser.Defaults と一致)
            selected =
            [
                new ViewColumn(NameKey, ColumnSource.Basic, DefaultStar(NameKey)),
                new ViewColumn(SizeKey, ColumnSource.Basic, DefaultStar(SizeKey)),
                new ViewColumn(ModifiedDateKey, ColumnSource.Basic, DefaultStar(ModifiedDateKey)),
            ];
        }
        else
        {
            // name は先頭固定(VE-001)。既存位置に関わらず先頭へ。以降は出現順で重複除去し上限で切り詰め(VE-002)。
            selected = [new ViewColumn(NameKey, ColumnSource.Basic, DefaultStar(NameKey))];
            var seen = new HashSet<string>(StringComparer.Ordinal) { NameKey };
            foreach (var column in valid)
            {
                if (selected.Count >= MaxColumns)
                {
                    break;
                }

                if (seen.Add(column.Key))
                {
                    selected.Add(column);
                }
            }
        }

        return new ViewColumnModel(selected, tagPool);
    }

    /// <summary>列を末尾に追加する(上限到達・重複は無視・VE-002)。追加できたら true。</summary>
    public bool Add(ViewColumn column)
    {
        ArgumentNullException.ThrowIfNull(column);
        if (AtLimit || ContainsKey(column.Key))
        {
            return false;
        }

        _selected.Add(column);
        return true;
    }

    /// <summary>列を除去する(name は不可・VE-001)。除去できたら true。</summary>
    public bool Remove(string key)
    {
        var index = IndexOf(key);
        if (index <= 0)
        {
            return false; // 見つからない(-1)か name(0)
        }

        _selected.RemoveAt(index);
        return true;
    }

    /// <summary>直前の列と入れ替える(先頭・name の直後=index 1 は不可・VE-001)。動いたら true。</summary>
    public bool MoveUp(string key)
    {
        var index = IndexOf(key);
        if (index <= 1)
        {
            return false; // name(0)は動かさない・index 1 は name の直後で上へ動けない
        }

        (_selected[index - 1], _selected[index]) = (_selected[index], _selected[index - 1]);
        return true;
    }

    /// <summary>直後の列と入れ替える(末尾・name は不可・VE-001)。動いたら true。</summary>
    public bool MoveDown(string key)
    {
        var index = IndexOf(key);
        if (index <= 0 || index >= _selected.Count - 1)
        {
            return false; // name(0)・末尾は動かせない
        }

        (_selected[index + 1], _selected[index]) = (_selected[index], _selected[index + 1]);
        return true;
    }

    /// <summary>
    /// display_columns(JSON)へ直列化する(既存スキーマ <c>{type,key,label?,width}</c>・DisplayColumnParser 互換)。
    /// kind は tag 型から描画時に導出するため書き出さない(前方互換)。label は省略(basic=UI ローカライズ / tag=tag.Name フォールバック)。
    /// </summary>
    public string Serialize()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var column in _selected)
            {
                writer.WriteStartObject();
                writer.WriteString("type", column.Source == ColumnSource.Basic ? "basic" : "tag");
                writer.WriteString("key", column.Key);
                writer.WriteNumber("width", column.Star);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static IEnumerable<ViewColumn> BasicColumns() =>
        BasicKeys.Select(k => new ViewColumn(k, ColumnSource.Basic, DefaultStar(k)));

    private bool ContainsKey(string key) => IndexOf(key) >= 0;

    private int IndexOf(string key)
    {
        for (var i = 0; i < _selected.Count; i++)
        {
            if (string.Equals(_selected[i].Key, key, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>列幅の star(M-BOM: name=2*・他=1*・DisplayColumnParser と一致)。</summary>
    private static double DefaultStar(string key) => key == NameKey ? 2 : 1;

    /// <summary>display_columns の (type, key) 列だけを順に取り出す(破損・非配列は空)。</summary>
    private static List<(ColumnSource Type, string Key)> ParseKeys(string? json)
    {
        var result = new List<(ColumnSource, string)>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return result;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object ||
                    !TryGetString(element, "type", out var type) ||
                    !TryGetString(element, "key", out var key))
                {
                    continue;
                }

                if (type == "basic")
                {
                    result.Add((ColumnSource.Basic, key));
                }
                else if (type == "tag")
                {
                    result.Add((ColumnSource.Tag, key));
                }
            }
        }
        catch (JsonException)
        {
            return []; // 破損データで停止しない(INV-008)
        }

        return result;
    }

    private static bool TryGetString(JsonElement element, string name, out string value)
    {
        value = string.Empty;
        if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            value = prop.GetString()!;
            return true;
        }

        return false;
    }
}
