using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ViewPrism2.Core.Models;

namespace ViewPrism2.App.ViewModels;

/// <summary>
/// NodeGraph ツリーの表示ノード(M-UI-013、E-UI-NODEGRAPH-025)。
/// OC-2 出力の GraphNode を包み、選択時のパス→条件変換(OC-3)用にルートからのパスを保持する。
/// </summary>
public sealed class GraphNodeViewModel : ObservableObject
{
    public GraphNodeViewModel(GraphNode node, GraphNodeViewModel? parent, string? displayNameOverride = null)
    {
        Node = node;
        DisplayName = displayNameOverride ?? node.DisplayName;

        var path = new List<GraphNode>();
        if (parent is not null)
        {
            path.AddRange(parent.PathFromRoot);
        }

        path.Add(node);
        PathFromRoot = path;

        foreach (var child in node.Children)
        {
            Children.Add(new GraphNodeViewModel(child, this));
        }
    }

    public GraphNode Node { get; }

    public string DisplayName { get; }

    /// <summary>ルートから自ノードまでのパス(OC-3 の入力)。</summary>
    public IReadOnlyList<GraphNode> PathFromRoot { get; }

    public ObservableCollection<GraphNodeViewModel> Children { get; } = [];

    /// <summary>ツリー内から GraphNode の同一性(階層ノード id+値)で探す(選択復元用)。</summary>
    public GraphNodeViewModel? Find(string? hierarchyNodeId, string? value)
    {
        if (string.Equals(Node.HierarchyNodeId, hierarchyNodeId, StringComparison.Ordinal) &&
            string.Equals(Node.Value, value, StringComparison.Ordinal))
        {
            return this;
        }

        foreach (var child in Children)
        {
            if (child.Find(hierarchyNodeId, value) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
