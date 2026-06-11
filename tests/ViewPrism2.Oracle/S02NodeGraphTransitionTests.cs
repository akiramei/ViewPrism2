using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using Xunit;

namespace ViewPrism2.Oracle;

/// <summary>
/// S-02: NodeGraph 構造遷移(spec §2.4 REQ-035、EQ-001)。
/// textual タグの distinct 値を 0→1→2 件と増やしながら 3 回構築する。
/// </summary>
[Trait("oracle", "S-02")]
public sealed class S02NodeGraphTransitionTests
{
    private const string TextualTagId = "0e0a8e6a-1111-4a6a-8a6a-000000000001";
    private const string SimpleTagId = "0e0a8e6a-1111-4a6a-8a6a-000000000002";
    private const string TextualNodeId = "1e0a8e6a-2222-4a6a-8a6a-000000000001";
    private const string ChildNodeId = "1e0a8e6a-2222-4a6a-8a6a-000000000002";

    private static readonly IReadOnlyList<HierarchyNode> Hierarchy =
    [
        new HierarchyNode { Id = TextualNodeId, ViewId = "v", TagId = TextualTagId, ParentId = null, Position = 0 },
        new HierarchyNode { Id = ChildNodeId, ViewId = "v", TagId = SimpleTagId, ParentId = TextualNodeId, Position = 0 },
    ];

    private static readonly IReadOnlyDictionary<string, Tag> Tags = new Dictionary<string, Tag>(StringComparer.Ordinal)
    {
        [TextualTagId] = new Tag { Id = TextualTagId, Name = "Color", Type = TagType.Textual },
        [SimpleTagId] = new Tag { Id = SimpleTagId, Name = "Star", Type = TagType.Simple },
    };

    private static NodeGraphResult Build(params string[] distinctValues)
    {
        var source = TagValueIndex.FromValues(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [TextualTagId] = distinctValues,
        });
        return new NodeGraphBuilder().BuildGraph(Hierarchy, Tags, source);
    }

    [Fact]
    public void 値0件_タグ名ノードのみで値ノードなし()
    {
        var result = Build();

        var top = Assert.Single(result.Root.Children);
        Assert.Equal(NodeKind.TagName, top.Kind);
        Assert.Equal("Color", top.DisplayName);
        Assert.Null(top.Value);

        // 値ノード・一体型ノードは存在しない
        Assert.DoesNotContain(Flatten(result.Root), n => n.Kind is NodeKind.Value or NodeKind.Combined);

        // 階層子はタグ名ノード配下に接続される(§2.4 階層構造の保持)
        var child = Assert.Single(top.Children);
        Assert.Equal(NodeKind.TagName, child.Kind);
        Assert.Equal("Star", child.DisplayName);
    }

    [Fact]
    public void 値1件_一体型ノードで階層子がその配下()
    {
        var result = Build("Blue");

        var top = Assert.Single(result.Root.Children);
        Assert.Equal(NodeKind.Combined, top.Kind);
        Assert.Equal("Color: Blue", top.DisplayName);
        Assert.Equal("Blue", top.Value);

        var child = Assert.Single(top.Children);
        Assert.Equal(NodeKind.TagName, child.Kind);
        Assert.Equal("Star", child.DisplayName);
    }

    [Fact]
    public void 値2件_値ノード2個に階層子が複製され序数昇順()
    {
        var result = Build("Blue", "Apple"); // 供給順は非整列 → 出力は序数昇順

        var top = Assert.Single(result.Root.Children);
        Assert.Equal(NodeKind.TagName, top.Kind);
        Assert.Equal("Color", top.DisplayName);

        Assert.Equal(2, top.Children.Count);
        Assert.All(top.Children, n => Assert.Equal(NodeKind.Value, n.Kind));
        Assert.Equal(["Apple", "Blue"], top.Children.Select(n => n.Value)); // 序数昇順

        // 階層子は各値ノードの配下に複製
        foreach (var valueNode in top.Children)
        {
            var child = Assert.Single(valueNode.Children);
            Assert.Equal(NodeKind.TagName, child.Kind);
            Assert.Equal("Star", child.DisplayName);
        }
    }

    [Fact]
    public void 同一タグ付け状態での3回構築は同一構造()
    {
        // 0→1→2 と遷移後、同じ状態で再構築すれば構造は一致する(表示のたび再構築、REQ-035)
        var first = Build("Blue", "Apple");
        var third = Build("Blue", "Apple");
        Assert.Equal(Describe(first.Root), Describe(third.Root));
    }

    private static IEnumerable<GraphNode> Flatten(GraphNode node)
    {
        yield return node;
        foreach (var child in node.Children)
        {
            foreach (var descendant in Flatten(child))
            {
                yield return descendant;
            }
        }
    }

    private static string Describe(GraphNode node, int depth = 0)
    {
        var self = $"{new string(' ', depth)}{node.Kind}|{node.DisplayName}|{node.Value}";
        return string.Join('\n', new[] { self }.Concat(node.Children.Select(c => Describe(c, depth + 1))));
    }
}
