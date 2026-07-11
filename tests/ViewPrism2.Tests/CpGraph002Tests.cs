using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-GRAPH-002: NodeGraph 構築(OC-2)・パス→条件変換(OC-3)・ホームタグ解決が仕様 §2.4 と一致する。
/// 期待ノード木(型・表示名・親子・順序)と期待条件列の完全一致。
/// </summary>
[Trait("cp", "CP-GRAPH-002")]
public sealed class CpGraph002Tests
{
    private static readonly NodeGraphBuilder Builder = new();
    private static readonly PathConditionConverter Converter = new();

    private static readonly Tag SimpleTag = new() { Id = "tag-s", Name = "Favorite", Type = TagType.Simple };
    private static readonly Tag TextualTag = new() { Id = "tag-t", Name = "Color", Type = TagType.Textual };
    private static readonly Tag NumericTag = new() { Id = "tag-n", Name = "Rating", Type = TagType.Numeric };

    private static IReadOnlyDictionary<string, Tag> Tags(params Tag[] tags)
        => tags.ToDictionary(t => t.Id, StringComparer.Ordinal);

    private static HierarchyNode Node(
        string id,
        string tagId,
        string? parentId = null,
        int position = 0,
        string? alias = null,
        HierarchyConditionType? conditionType = null,
        string? conditionValue = null)
        => new()
        {
            Id = id,
            ViewId = "view-1",
            TagId = tagId,
            ParentId = parentId,
            Position = position,
            Alias = alias,
            ConditionType = conditionType,
            ConditionValue = conditionValue,
        };

    /// <summary>タグ id → distinct 値(Normal 限定の供給は ITagValueSource 契約)。</summary>
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

    // ---- OC-2: 構築 ----

    [Fact]
    public void Simpleタグは単一ノードで階層子が配下に接続される()
    {
        var hierarchy = new[]
        {
            Node("h1", SimpleTag.Id),
            Node("h2", NumericTag.Id, parentId: "h1"),
        };
        var result = Builder.BuildGraph(hierarchy, Tags(SimpleTag, NumericTag), new FakeValueSource());

        var root = result.Root;
        Assert.Equal(NodeKind.Root, root.Kind);
        var simple = Assert.Single(root.Children);
        Assert.Equal(NodeKind.TagName, simple.Kind);
        Assert.Equal("Favorite", simple.DisplayName);
        Assert.Equal("h1", simple.HierarchyNodeId);
        var child = Assert.Single(simple.Children);
        Assert.Equal(NodeKind.TagName, child.Kind);
        Assert.Equal("Rating", child.DisplayName);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Textual値0件はタグ名ノードのみ()
    {
        var hierarchy = new[] { Node("h1", TextualTag.Id) };
        var result = Builder.BuildGraph(hierarchy, Tags(TextualTag), new FakeValueSource());

        var node = Assert.Single(result.Root.Children);
        Assert.Equal(NodeKind.TagName, node.Kind);
        Assert.Equal("Color", node.DisplayName);
        Assert.Empty(node.Children); // 値ノードなし
    }

    [Fact]
    public void Textual値0件のノード選択はexists条件として評価される()
    {
        var hierarchy = new[] { Node("h1", TextualTag.Id) };
        var result = Builder.BuildGraph(hierarchy, Tags(TextualTag), new FakeValueSource());
        var node = Assert.Single(result.Root.Children);

        var conditions = Converter.BuildConditions([result.Root, node]);

        var condition = Assert.Single(conditions);
        Assert.Equal(ConditionOperator.Exists, condition.Operator);
        Assert.Equal(TextualTag.Id, condition.TagId);
    }

    [Fact]
    public void Textual値1件は一体型ノードで階層子はその配下()
    {
        var hierarchy = new[]
        {
            Node("h1", TextualTag.Id),
            Node("h2", SimpleTag.Id, parentId: "h1"),
        };
        var source = new FakeValueSource().With(TextualTag.Id, "red");
        var result = Builder.BuildGraph(hierarchy, Tags(TextualTag, SimpleTag), source);

        var combined = Assert.Single(result.Root.Children);
        Assert.Equal(NodeKind.Combined, combined.Kind);
        Assert.Equal("Color: red", combined.DisplayName); // 表示名「タグ名: 値」
        Assert.Equal("red", combined.Value);
        var child = Assert.Single(combined.Children);
        Assert.Equal("Favorite", child.DisplayName);
    }

    [Fact]
    public void Textual値2件は序数昇順の値ノードで階層子は各値ノード配下に複製()
    {
        var hierarchy = new[]
        {
            Node("h1", TextualTag.Id),
            Node("h2", SimpleTag.Id, parentId: "h1"),
        };
        var source = new FakeValueSource().With(TextualTag.Id, "red", "blue");
        var result = Builder.BuildGraph(hierarchy, Tags(TextualTag, SimpleTag), source);

        var tagName = Assert.Single(result.Root.Children);
        Assert.Equal(NodeKind.TagName, tagName.Kind);
        Assert.Equal("Color", tagName.DisplayName);
        Assert.Equal(2, tagName.Children.Count);

        // 序数昇順: "blue" < "red"
        Assert.Equal(["blue", "red"], tagName.Children.Select(c => c.DisplayName));
        Assert.All(tagName.Children, c => Assert.Equal(NodeKind.Value, c.Kind));

        // 階層上の子ノードは各値ノードの配下に複製される
        foreach (var valueNode in tagName.Children)
        {
            var child = Assert.Single(valueNode.Children);
            Assert.Equal("Favorite", child.DisplayName);
            Assert.Equal(NodeKind.TagName, child.Kind);
        }
    }

    [Fact]
    public void 値抽出はNormal限定_TagValueIndexがmissing_pendingの値を含めない()
    {
        // INV-010: missing/pending 画像のみに付いた値は現れない
        var images = new[]
        {
            new ImageWithTags("img1", ImageStatus.Normal, [new EvalTagValue(TextualTag.Id, TagType.Textual, "red")]),
            new ImageWithTags("img2", ImageStatus.Missing, [new EvalTagValue(TextualTag.Id, TagType.Textual, "ghost-m")]),
            new ImageWithTags("img3", ImageStatus.Pending, [new EvalTagValue(TextualTag.Id, TagType.Textual, "ghost-p")]),
        };
        var source = TagValueIndex.Build(images);
        var result = Builder.BuildGraph([Node("h1", TextualTag.Id)], Tags(TextualTag), source);

        // 値 1 件(red)→ 一体型ノード。ghost-* は現れない
        var combined = Assert.Single(result.Root.Children);
        Assert.Equal(NodeKind.Combined, combined.Kind);
        Assert.Equal("Color: red", combined.DisplayName);
    }

    [Fact]
    public void Predefined外の自由入力値も値ノードに現れる()
    {
        // REQ-035: distinct 値は predefined_values 外の自由入力値も含む(値はタグ付け状態のみが典拠)
        var source = new FakeValueSource().With(TextualTag.Id, "free-input", "listed");
        var result = Builder.BuildGraph([Node("h1", TextualTag.Id)], Tags(TextualTag), source);

        var tagName = Assert.Single(result.Root.Children);
        Assert.Equal(["free-input", "listed"], tagName.Children.Select(c => c.Value));
    }

    [Fact]
    public void ConditionTypeValuesの制限内の値のみ展開される()
    {
        var hierarchy = new[]
        {
            Node("h1", TextualTag.Id, conditionType: HierarchyConditionType.Values,
                conditionValue: """{"values":["red","blue"]}"""),
        };
        var source = new FakeValueSource().With(TextualTag.Id, "red", "blue", "green");
        var result = Builder.BuildGraph(hierarchy, Tags(TextualTag), source);

        var tagName = Assert.Single(result.Root.Children);
        Assert.Equal(["blue", "red"], tagName.Children.Select(c => c.Value)); // green は制限外
    }

    [Fact]
    public void Aliasがあれば表示名に使われる()
    {
        var hierarchy = new[] { Node("h1", TextualTag.Id, alias: "色") };
        var source = new FakeValueSource().With(TextualTag.Id, "red");
        var result = Builder.BuildGraph(hierarchy, Tags(TextualTag), source);

        var combined = Assert.Single(result.Root.Children);
        Assert.Equal("色: red", combined.DisplayName);
    }

    [Fact]
    public void 同一親内はposition昇順で並ぶ()
    {
        var hierarchy = new[]
        {
            Node("h2", NumericTag.Id, position: 1),
            Node("h1", SimpleTag.Id, position: 0),
        };
        var result = Builder.BuildGraph(hierarchy, Tags(SimpleTag, NumericTag), new FakeValueSource());

        Assert.Equal(["Favorite", "Rating"], result.Root.Children.Select(c => c.DisplayName));
    }

    // ---- OC-3: パス→条件 ----

    [Fact]
    public void パス変換_root_simple_値ノード_はexistsとequalsのAND列()
    {
        var root = new GraphNode { Kind = NodeKind.Root, DisplayName = string.Empty };
        var simple = new GraphNode
        {
            Kind = NodeKind.TagName, DisplayName = "Favorite",
            HierarchyNodeId = "h1", TagId = SimpleTag.Id, TagType = TagType.Simple,
        };
        var value = new GraphNode
        {
            Kind = NodeKind.Value, DisplayName = "red",
            HierarchyNodeId = "h2", TagId = TextualTag.Id, TagType = TagType.Textual, Value = "red",
        };

        var conditions = Converter.BuildConditions([root, simple, value]);

        Assert.Equal(2, conditions.Count);
        Assert.Equal(ConditionOperator.Exists, conditions[0].Operator);
        Assert.Equal(SimpleTag.Id, conditions[0].TagId);
        Assert.Null(conditions[0].Value);
        Assert.Equal(ConditionOperator.Equals, conditions[1].Operator);
        Assert.Equal(TextualTag.Id, conditions[1].TagId);
        Assert.Equal("red", conditions[1].Value);
    }

    [Fact]
    public void パス変換_一体型ノードはequals()
    {
        var combined = new GraphNode
        {
            Kind = NodeKind.Combined, DisplayName = "Color: red",
            HierarchyNodeId = "h1", TagId = TextualTag.Id, TagType = TagType.Textual, Value = "red",
        };
        var conditions = Converter.BuildConditions([combined]);

        var condition = Assert.Single(conditions);
        Assert.Equal(ConditionOperator.Equals, condition.Operator);
        Assert.Equal("red", condition.Value);
    }

    [Fact]
    public void パス変換_numericノードのrangeはbetweenを生成する()
    {
        var hierarchy = new[]
        {
            Node("h1", NumericTag.Id, conditionType: HierarchyConditionType.Range,
                conditionValue: """{"valueFrom":1,"valueTo":5}"""),
        };
        var result = Builder.BuildGraph(hierarchy, Tags(NumericTag), new FakeValueSource());
        var node = Assert.Single(result.Root.Children);

        var conditions = Converter.BuildConditions([result.Root, node]);

        var condition = Assert.Single(conditions);
        Assert.Equal(ConditionOperator.Between, condition.Operator);
        Assert.Equal(NumericTag.Id, condition.TagId);
        Assert.Equal("1", condition.Value);
        Assert.Equal("5", condition.Value2);
    }

    [Fact]
    public void パス変換_numericノードのequalsはequalsを生成する()
    {
        var hierarchy = new[]
        {
            Node("h1", NumericTag.Id, conditionType: HierarchyConditionType.Equals,
                conditionValue: """{"value":3}"""),
        };
        var result = Builder.BuildGraph(hierarchy, Tags(NumericTag), new FakeValueSource());

        var conditions = Converter.BuildConditions([Assert.Single(result.Root.Children)]);

        var condition = Assert.Single(conditions);
        Assert.Equal(ConditionOperator.Equals, condition.Operator);
        Assert.Equal("3", condition.Value);
    }

    // ---- 異常系(FMEA-008 / INV-004 / INV-008) ----

    [Fact]
    public void 循環階層入力は無限ループせず警告付きで枝を打ち切る_FMEA008()
    {
        // h-a ⇔ h-b の相互参照循環+正常な h-c
        var hierarchy = new[]
        {
            Node("h-a", SimpleTag.Id, parentId: "h-b"),
            Node("h-b", NumericTag.Id, parentId: "h-a"),
            Node("h-c", TextualTag.Id),
        };
        var result = Builder.BuildGraph(
            hierarchy, Tags(SimpleTag, NumericTag, TextualTag), new FakeValueSource());

        var node = Assert.Single(result.Root.Children); // 正常枝のみ
        Assert.Equal("Color", node.DisplayName);
        Assert.Equal(2, result.Warnings.Count(w => w.Kind == GraphWarningKind.CircularHierarchy));
    }

    [Fact]
    public void 自己親ノードは打ち切られる()
    {
        var hierarchy = new[] { Node("h-self", SimpleTag.Id, parentId: "h-self") };
        var result = Builder.BuildGraph(hierarchy, Tags(SimpleTag), new FakeValueSource());

        Assert.Empty(result.Root.Children);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal(GraphWarningKind.CircularHierarchy, warning.Kind);
    }

    [Fact]
    public void 参照切れタグのノードはスキップされる()
    {
        // INV-008: tags 辞書に無いタグを指すノード(+その配下)はスキップ
        var hierarchy = new[]
        {
            Node("h1", "tag-deleted"),
            Node("h2", SimpleTag.Id, parentId: "h1"),
            Node("h3", TextualTag.Id, position: 1),
        };
        var result = Builder.BuildGraph(hierarchy, Tags(SimpleTag, TextualTag), new FakeValueSource());

        var node = Assert.Single(result.Root.Children);
        Assert.Equal("Color", node.DisplayName);
        Assert.Contains(result.Warnings, w => w.Kind == GraphWarningKind.MissingTag && w.HierarchyNodeId == "h1");
    }

    // ---- ホームタグ解決(REQ-037) ----

    [Fact]
    public void ホームタグ_解決可なら該当ノード_不能ならnull_未設定ならnull()
    {
        var hierarchy = new[]
        {
            Node("h1", SimpleTag.Id),
            Node("h2", NumericTag.Id, parentId: "h1"),
        };
        var result = Builder.BuildGraph(hierarchy, Tags(SimpleTag, NumericTag), new FakeValueSource());

        var resolved = Builder.ResolveHome(result.Root, "h2");
        Assert.NotNull(resolved);
        Assert.Equal("Rating", resolved.DisplayName);

        Assert.Null(Builder.ResolveHome(result.Root, "h-deleted")); // 参照切れ → null(エラーにしない)
        Assert.Null(Builder.ResolveHome(result.Root, null));        // 未設定 → null
    }

    [Fact]
    public void ECO063_ホームpathはrootを除く祖先列を返し複製nodeはDFS先頭で決定する()
    {
        var hierarchy = new[]
        {
            Node("h1", TextualTag.Id),
            Node("h2", SimpleTag.Id, parentId: "h1"),
        };
        var source = new FakeValueSource().With(TextualTag.Id, "red", "blue");
        var result = Builder.BuildGraph(hierarchy, Tags(TextualTag, SimpleTag), source);

        var path = Builder.ResolveHomePath(result.Root, "h2");

        // h2 は blue/red の各値枝に複製される。既存 ResolveHome と同じ DFS 先頭(blue)を採用。
        Assert.Equal(["Color", "blue", "Favorite"], path.Select(node => node.DisplayName));
        Assert.Empty(Builder.ResolveHomePath(result.Root, "h-deleted"));
        Assert.Empty(Builder.ResolveHomePath(result.Root, null));
    }
}
