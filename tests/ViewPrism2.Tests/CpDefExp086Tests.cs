using System.Reflection;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-DEFEXP-086: 定義値展開モード(ECO-086)。
/// R5 プローブ(ECO-056/084 様式=最終形 API をリフレクション解決し、導線の不在を実行時不合格として実測する):
/// ①階層ノードの展開モード ②タグ定義の値集合の意味 ③NodeGraphBuilder の定義値供給の受け口。
/// 是正後は同じ検査が緑転し、挙動検査(定義順・0 件安定・numeric 定義域・未定義値検出)を直接 API で追記する。
/// </summary>
[Trait("cp", "CP-DEFEXP-086")]
public sealed class CpDefExp086Tests
{
    [Fact]
    public void HierarchyNodeに展開モードが存在する()
    {
        // ECO-086 裁定 a: Manual/ObservedValues/DefinedValues/DefinedAndObservedValues(既定=ObservedValues)
        var prop = typeof(HierarchyNode).GetProperty("ExpansionMode", BindingFlags.Public | BindingFlags.Instance);
        Assert.True(prop is not null, "HierarchyNode.ExpansionMode が存在しない(定義値展開の導線不在=R5 プローブ)");
    }

    [Fact]
    public void HierarchyNodeに0件値ノードを隠すオプションが存在する()
    {
        // ECO-086 裁定 d: 既定=0 件も表示+ノード単位の「隠す」オプション
        var prop = typeof(HierarchyNode).GetProperty("HideEmptyValues", BindingFlags.Public | BindingFlags.Instance);
        Assert.True(prop is not null, "HierarchyNode.HideEmptyValues が存在しない(裁定 d の導線不在=R5 プローブ)");
    }

    [Fact]
    public void TextualTagSettingsに値集合の意味が存在する()
    {
        // ECO-086 裁定 b: 入力補助(既定)/閉じた値集合。閉集合の候補外値は未定義値として検出(付与は拒否しない)
        var prop = typeof(TextualTagSettings).GetProperty("ValueDomain", BindingFlags.Public | BindingFlags.Instance);
        Assert.True(prop is not null, "TextualTagSettings.ValueDomain が存在しない(閉じた値集合の導線不在=R5 プローブ)");
    }

    [Fact]
    public void NodeGraphBuilderが定義値供給を受理する()
    {
        // ECO-086 §3: ITagValueSource は観測値専用契約(INV-010)のため、定義値は別契約で供給する
        var overload = typeof(NodeGraphBuilder)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Any(m => m.Name == "BuildGraph" && m.GetParameters().Length >= 4);
        Assert.True(overload, "NodeGraphBuilder.BuildGraph に定義値供給の受け口が無い(定義値展開の導線不在=R5 プローブ)");
    }

    [Fact]
    public void GraphNodeが未定義値の検出を表現できる()
    {
        // ECO-086 裁定 c: 未定義値(閉集合の定義外の値)は通常ノードと区別して検出表示する
        var prop = typeof(GraphNode).GetProperty("IsUndefinedValue", BindingFlags.Public | BindingFlags.Instance);
        Assert.True(prop is not null, "GraphNode.IsUndefinedValue が存在しない(未定義値検出の導線不在=R5 プローブ)");
    }

    // ==== 挙動検査(是正後に直接 API で固定・REQ-095/096) ====

    private static readonly NodeGraphBuilder Builder = new();
    private static readonly PathConditionConverter Converter = new();

    private static readonly string[] Prefectures =
    [
        "北海道", "青森県", "岩手県", "宮城県", "秋田県", "山形県", "福島県",
        "茨城県", "栃木県", "群馬県", "埼玉県", "千葉県", "東京都", "神奈川県",
        "新潟県", "富山県", "石川県", "福井県", "山梨県", "長野県", "岐阜県",
        "静岡県", "愛知県", "三重県", "滋賀県", "京都府", "大阪府", "兵庫県",
        "奈良県", "和歌山県", "鳥取県", "島根県", "岡山県", "広島県", "山口県",
        "徳島県", "香川県", "愛媛県", "高知県", "福岡県", "佐賀県", "長崎県",
        "熊本県", "大分県", "宮崎県", "鹿児島県", "沖縄県",
    ];

    private static readonly Tag PrefTag = new() { Id = "tag-pref", Name = "都道府県", Type = TagType.Textual };
    private static readonly Tag RatingTag = new() { Id = "tag-rating", Name = "評価", Type = TagType.Numeric };

    private static IReadOnlyDictionary<string, Tag> Tags(params Tag[] tags)
        => tags.ToDictionary(t => t.Id, StringComparer.Ordinal);

    private static HierarchyNode Node(
        string id, string tagId, string? parentId = null, int position = 0,
        HierarchyExpansionMode mode = HierarchyExpansionMode.Observed)
        => new()
        {
            Id = id,
            ViewId = "view-1",
            TagId = tagId,
            ParentId = parentId,
            Position = position,
            ExpansionMode = mode,
        };

    private sealed class FakeValueSource : ITagValueSource
    {
        private readonly Dictionary<string, IReadOnlyList<string>> _values = new(StringComparer.Ordinal);

        public FakeValueSource With(string tagId, params string[] values)
        {
            _values[tagId] = values;
            return this;
        }

        public IReadOnlyList<string> GetDistinctValues(string tagId)
            => _values.TryGetValue(tagId, out var v) ? v : [];
    }

    private static TagDefinedValueIndex PrefIndex(TagValueDomain domain = TagValueDomain.Suggest)
        => TagDefinedValueIndex.Build(
            new Dictionary<string, TextualTagSettings>(StringComparer.Ordinal)
            {
                [PrefTag.Id] = new() { TagId = PrefTag.Id, PredefinedValues = Prefectures, ValueDomain = domain },
            },
            new Dictionary<string, NumericTagSettings>(StringComparer.Ordinal)
            {
                [RatingTag.Id] = new() { TagId = RatingTag.Id, Min = 1, Max = 5, Step = 1 },
            });

    [Fact]
    public void 定義値展開は付与0件でも47都道府県を定義順で生成する()
    {
        // REQ-096: 定義順(序数ソートしない=京都が北海道より先に来ない)・0 件でも構造安定
        var hierarchy = new[] { Node("h1", PrefTag.Id, mode: HierarchyExpansionMode.Defined) };
        var result = Builder.BuildGraph(hierarchy, Tags(PrefTag), new FakeValueSource(), PrefIndex());

        var tagName = Assert.Single(result.Root.Children);
        Assert.Equal(NodeKind.TagName, tagName.Kind);
        Assert.Equal(47, tagName.Children.Count);
        Assert.Equal(Prefectures, tagName.Children.Select(c => c.Value));
        Assert.All(tagName.Children, c =>
        {
            Assert.Equal(NodeKind.Value, c.Kind);
            Assert.True(c.IsDefinedExpansion);
            Assert.False(c.IsUndefinedValue);
        });
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void 多段の定義値リーフ選択は親equalsと子equalsのANDになる()
    {
        // REQ-096: 都道府県(defined)→評価(defined 1..5)。北海道→評価5 = 都道府県=北海道 AND 評価=5
        var hierarchy = new[]
        {
            Node("h1", PrefTag.Id, mode: HierarchyExpansionMode.Defined),
            Node("h2", RatingTag.Id, parentId: "h1", mode: HierarchyExpansionMode.Defined),
        };
        var result = Builder.BuildGraph(hierarchy, Tags(PrefTag, RatingTag), new FakeValueSource(), PrefIndex());

        var tagName = Assert.Single(result.Root.Children);
        var hokkaido = tagName.Children[0];
        Assert.Equal("北海道", hokkaido.Value);
        var ratingName = Assert.Single(hokkaido.Children);
        Assert.Equal(NodeKind.TagName, ratingName.Kind);
        Assert.Equal(new[] { "1", "2", "3", "4", "5" }, ratingName.Children.Select(c => c.Value));

        var leaf5 = ratingName.Children[4];
        var conditions = Converter.BuildConditions([result.Root, tagName, hokkaido, ratingName, leaf5]);
        // タグ名ノード=exists・値ノード=equals(REQ-036 不変)。numeric equals は評価器で数値比較(REQ-031)
        Assert.Collection(conditions,
            c => { Assert.Equal(ConditionOperator.Exists, c.Operator); Assert.Equal(PrefTag.Id, c.TagId); },
            c => { Assert.Equal(ConditionOperator.Equals, c.Operator); Assert.Equal("北海道", c.Value); },
            c => { Assert.Equal(ConditionOperator.Exists, c.Operator); Assert.Equal(RatingTag.Id, c.TagId); },
            c => { Assert.Equal(ConditionOperator.Equals, c.Operator); Assert.Equal("5", c.Value); Assert.Equal(RatingTag.Id, c.TagId); });
    }

    [Fact]
    public void 閉じた値集合の定義外付与値はdefinedでも末尾に未定義値として検出される()
    {
        // REQ-095/096 裁定 b/c: 蝦夷は 48 番目の通常ノードでなく検出ノード(到達性は保つ)
        var hierarchy = new[] { Node("h1", PrefTag.Id, mode: HierarchyExpansionMode.Defined) };
        var observed = new FakeValueSource().With(PrefTag.Id, "蝦夷", "北海道");
        var result = Builder.BuildGraph(
            hierarchy, Tags(PrefTag), observed, PrefIndex(TagValueDomain.Closed));

        var tagName = Assert.Single(result.Root.Children);
        Assert.Equal(48, tagName.Children.Count);
        var last = tagName.Children[^1];
        Assert.Equal("蝦夷", last.Value);
        Assert.True(last.IsUndefinedValue);
        Assert.False(tagName.Children[0].IsUndefinedValue); // 北海道は定義値
    }

    [Fact]
    public void 開集合のdefinedは定義外の付与値を出さずdefined_and_observedは末尾に通常ノードで加える()
    {
        // REQ-096 裁定 f: 定義順先+観測のみ値は末尾序数昇順。suggest では未定義マークなし
        var observed = new FakeValueSource().With(PrefTag.Id, "蝦夷", "琉球");
        var defined = Builder.BuildGraph(
            [Node("h1", PrefTag.Id, mode: HierarchyExpansionMode.Defined)],
            Tags(PrefTag), observed, PrefIndex());
        Assert.Equal(47, Assert.Single(defined.Root.Children).Children.Count);

        var both = Builder.BuildGraph(
            [Node("h1", PrefTag.Id, mode: HierarchyExpansionMode.DefinedAndObserved)],
            Tags(PrefTag), observed, PrefIndex());
        var children = Assert.Single(both.Root.Children).Children;
        Assert.Equal(49, children.Count);
        Assert.Equal(Prefectures, children.Take(47).Select(c => c.Value));
        Assert.Equal(new[] { "琉球", "蝦夷" }, children.Skip(47).Select(c => c.Value)); // 序数昇順
        Assert.All(children, c => Assert.False(c.IsUndefinedValue));
    }

    [Fact]
    public void 定義不能は警告つきで観測値へフォールバックする()
    {
        // REQ-096 裁定 e: numeric の定義域欠落(step なし)→ DefinedValuesUnavailable+observed 挙動(単一ノード)
        var index = TagDefinedValueIndex.Build(
            new Dictionary<string, TextualTagSettings>(StringComparer.Ordinal),
            new Dictionary<string, NumericTagSettings>(StringComparer.Ordinal)
            {
                [RatingTag.Id] = new() { TagId = RatingTag.Id, Min = 1, Max = 5 }, // step 欠落
            });
        var result = Builder.BuildGraph(
            [Node("h1", RatingTag.Id, mode: HierarchyExpansionMode.Defined)],
            Tags(RatingTag), new FakeValueSource(), index);

        var single = Assert.Single(result.Root.Children);
        Assert.Equal(NodeKind.TagName, single.Kind);
        Assert.Empty(single.Children);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal(GraphWarningKind.DefinedValuesUnavailable, warning.Kind);
        Assert.Equal("h1", warning.HierarchyNodeId);
    }

    [Fact]
    public void 生成数が上限を超えるnumericは定義不能としてフォールバックする()
    {
        // REQ-096 裁定 e: 上限 256。min=0,max=1000000,step=0.001 で百万ノードを作らない
        var index = TagDefinedValueIndex.Build(
            new Dictionary<string, TextualTagSettings>(StringComparer.Ordinal),
            new Dictionary<string, NumericTagSettings>(StringComparer.Ordinal)
            {
                [RatingTag.Id] = new() { TagId = RatingTag.Id, Min = 0, Max = 1_000_000, Step = 0.001 },
            });
        Assert.Null(index.GetDefinedValues(RatingTag.Id));
    }

    [Fact]
    public void 既定モードと3引数BuildGraphは観測値展開のまま不変()
    {
        // REQ-096: 既存ビュー(expansion_mode 未指定)・既存呼び出し(3 引数)は REQ-035 の挙動と同値
        var observed = new FakeValueSource().With(PrefTag.Id, "京都府", "北海道");
        var legacy = Builder.BuildGraph([Node("h1", PrefTag.Id)], Tags(PrefTag), observed);

        var tagName = Assert.Single(legacy.Root.Children);
        // 観測値展開=序数昇順(定義順ではない)・IsDefinedExpansion なし
        Assert.Equal(new[] { "京都府", "北海道" }, tagName.Children.Select(c => c.Value));
        Assert.All(tagName.Children, c => Assert.False(c.IsDefinedExpansion));
    }

    [Fact]
    public void 値制限はモードを問わず生成後の値集合へ適用される()
    {
        // REQ-096 裁定 a: condition_type は展開モードと直交(defined の 47 件を values 制限で絞れる)
        var node = new HierarchyNode
        {
            Id = "h1",
            ViewId = "view-1",
            TagId = PrefTag.Id,
            ExpansionMode = HierarchyExpansionMode.Defined,
            ConditionType = HierarchyConditionType.Values,
            ConditionValue = """{"values":["東京都","北海道"]}""",
        };
        var result = Builder.BuildGraph([node], Tags(PrefTag), new FakeValueSource(), PrefIndex());

        var tagName = Assert.Single(result.Root.Children);
        // 定義順を保ったまま制限(北海道が先・東京都が後)
        Assert.Equal(new[] { "北海道", "東京都" }, tagName.Children.Select(c => c.Value));
    }

    [Fact]
    public void hide_empty_valuesは展開結果のノードへ伝搬する()
    {
        // REQ-096 裁定 d: 構築は常に行い、表示側が件数 0 の定義値ノードを隠すためのフラグを運ぶ
        var node = Node("h1", PrefTag.Id, mode: HierarchyExpansionMode.Defined) with { HideEmptyValues = true };
        var result = Builder.BuildGraph([node], Tags(PrefTag), new FakeValueSource(), PrefIndex());

        var tagName = Assert.Single(result.Root.Children);
        Assert.All(tagName.Children, c => Assert.True(c.HideEmptyValues));
    }
}
