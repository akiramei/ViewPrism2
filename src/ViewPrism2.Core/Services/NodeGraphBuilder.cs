using System.Text.RegularExpressions;
using ViewPrism2.Core.Models;

namespace ViewPrism2.Core.Services;

/// <summary>
/// NodeGraph 構築の値供給契約(M-GRAPH-003)。
/// status=Normal の画像に付与された distinct 値のみ返すこと(INV-010)。
/// </summary>
public interface ITagValueSource
{
    /// <summary>当該タグが status=Normal の画像に付与された distinct 値(順序は問わない)。</summary>
    IReadOnlyList<string> GetDistinctValues(string tagId);
}

/// <summary>NodeGraph 構築中に発生した警告の種別。</summary>
public enum GraphWarningKind
{
    /// <summary>階層に循環があり、当該枝を打ち切った(INV-004 / FMEA-008)。</summary>
    CircularHierarchy,

    /// <summary>参照切れタグのノードをスキップした(INV-008)。</summary>
    MissingTag,

    /// <summary>condition_value が不正で値制限を適用できなかった(制限結果は 0 件扱い)。</summary>
    InvalidConditionValue,
}

/// <summary>NodeGraph 構築の警告(UI 通知用)。</summary>
public sealed record GraphWarning(string? HierarchyNodeId, GraphWarningKind Kind, string Message);

/// <summary>NodeGraph 構築の出力: ルートノード+警告列(OC-2)。</summary>
public sealed record NodeGraphResult(GraphNode Root, IReadOnlyList<GraphWarning> Warnings);

/// <summary>
/// NodeGraph 構築器(OC-2、REQ-035)。階層ノードを position 順に走査し、タグ type 別に展開する。
/// 値の抽出対象は常に status=Normal の画像のみ(ITagValueSource 契約、INV-010)。
/// 循環入力でも無限ループしない(検出して当該枝を打ち切り+警告、INV-004)。
/// 参照切れタグのノードはスキップする(INV-008)。
/// </summary>
public sealed class NodeGraphBuilder
{
    private static readonly TimeSpan PatternTimeout = TimeSpan.FromSeconds(1);

    public NodeGraphResult BuildGraph(
        IReadOnlyList<HierarchyNode> hierarchy,
        IReadOnlyDictionary<string, Tag> tags,
        ITagValueSource values)
    {
        ArgumentNullException.ThrowIfNull(hierarchy);
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(values);

        var warnings = new List<GraphWarning>();
        var root = new GraphNode { Kind = NodeKind.Root, DisplayName = string.Empty };

        var byParent = hierarchy
            .GroupBy(n => n.ParentId ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => OrderSiblings(g), StringComparer.Ordinal);

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var path = new HashSet<string>(StringComparer.Ordinal);
        ExpandChildren(root.Children, parentHierarchyId: string.Empty, byParent, tags, values, path, visited, warnings);

        // ルートから到達できないノード = 循環(自己親・相互参照)に巻き込まれた枝。打ち切り+警告(FMEA-008)
        foreach (var node in hierarchy)
        {
            if (!visited.Contains(node.Id))
            {
                warnings.Add(new GraphWarning(
                    node.Id, GraphWarningKind.CircularHierarchy, "階層に循環があるためノードを打ち切りました。"));
            }
        }

        return new NodeGraphResult(root, warnings);
    }

    /// <summary>ホームタグ解決(REQ-037)。解決不能なら null(呼び出し側がルートへ)。エラーにしない。</summary>
    public GraphNode? ResolveHome(GraphNode root, string? homeTagId)
    {
        var path = ResolveHomePath(root, homeTagId);
        return path.Count == 0 ? null : path[^1];
    }

    /// <summary>
    /// ECO-063/REQ-037: 画像タブの初期ナビに使う root 除外の home path。
    /// 解決不能/null は空列(root fallback)。複製ノードは従来 ResolveHome と同じ DFS 先頭を採る。
    /// </summary>
    public IReadOnlyList<GraphNode> ResolveHomePath(GraphNode root, string? homeTagId)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (homeTagId is null)
        {
            return [];
        }

        var path = new List<GraphNode>();
        return TryFindPath(root, homeTagId, path) ? path : [];
    }

    private static bool TryFindPath(GraphNode node, string hierarchyNodeId, List<GraphNode> path)
    {
        var include = node.Kind != NodeKind.Root;
        if (include)
        {
            path.Add(node);
        }

        if (string.Equals(node.HierarchyNodeId, hierarchyNodeId, StringComparison.Ordinal))
        {
            return true;
        }

        foreach (var child in node.Children)
        {
            if (TryFindPath(child, hierarchyNodeId, path))
            {
                return true;
            }
        }

        if (include)
        {
            path.RemoveAt(path.Count - 1);
        }

        return false;
    }

    private static List<HierarchyNode> OrderSiblings(IEnumerable<HierarchyNode> siblings)
    {
        // position 0 起点昇順(REQ-034)。同値は id 昇順で安定させる(INV-002 の安定順序と同方針)
        return siblings
            .OrderBy(n => n.Position)
            .ThenBy(n => n.Id, StringComparer.Ordinal)
            .ToList();
    }

    private void ExpandChildren(
        List<GraphNode> target,
        string parentHierarchyId,
        IReadOnlyDictionary<string, List<HierarchyNode>> byParent,
        IReadOnlyDictionary<string, Tag> tags,
        ITagValueSource values,
        HashSet<string> path,
        HashSet<string> visited,
        List<GraphWarning> warnings)
    {
        if (!byParent.TryGetValue(parentHierarchyId, out var children))
        {
            return;
        }

        foreach (var node in children)
        {
            ExpandNode(target, node, byParent, tags, values, path, visited, warnings);
        }
    }

    private void ExpandNode(
        List<GraphNode> target,
        HierarchyNode node,
        IReadOnlyDictionary<string, List<HierarchyNode>> byParent,
        IReadOnlyDictionary<string, Tag> tags,
        ITagValueSource values,
        HashSet<string> path,
        HashSet<string> visited,
        List<GraphWarning> warnings)
    {
        // 防御: 展開経路上に同一ノードが再出現したら打ち切り(INV-004)
        if (!path.Add(node.Id))
        {
            warnings.Add(new GraphWarning(
                node.Id, GraphWarningKind.CircularHierarchy, "階層に循環があるためノードを打ち切りました。"));
            return;
        }

        visited.Add(node.Id);
        try
        {
            // INV-008: 参照切れタグのノードはスキップ(配下の枝ごと)
            if (!tags.TryGetValue(node.TagId, out var tag))
            {
                warnings.Add(new GraphWarning(
                    node.Id, GraphWarningKind.MissingTag, "参照先タグが存在しないためノードをスキップしました。"));
                return;
            }

            var displayBase = node.Alias ?? tag.Name;
            if (tag.Type != TagType.Textual)
            {
                // simple/numeric タグ → 1 ノード(配下に階層上の子ノードを接続)(REQ-035)
                var graphNode = NewNode(NodeKind.TagName, displayBase, node, tag, value: null);
                ExpandChildren(graphNode.Children, node.Id, byParent, tags, values, path, visited, warnings);
                target.Add(graphNode);
                return;
            }

            var distinct = ExtractValues(node, tag, values, warnings);
            switch (distinct.Count)
            {
                case 0:
                    // タグ名ノードのみ(値ノードなし)。選択時は exists 条件として評価される(REQ-035)
                    var emptyNode = NewNode(NodeKind.TagName, displayBase, node, tag, value: null);
                    ExpandChildren(emptyNode.Children, node.Id, byParent, tags, values, path, visited, warnings);
                    target.Add(emptyNode);
                    return;

                case 1:
                    // 一体型ノード「タグ名: 値」。階層上の子ノードは一体型ノードの配下に接続(REQ-035)
                    var combined = NewNode(
                        NodeKind.Combined, $"{displayBase}: {distinct[0]}", node, tag, distinct[0]);
                    ExpandChildren(combined.Children, node.Id, byParent, tags, values, path, visited, warnings);
                    target.Add(combined);
                    return;

                default:
                    // 値ノード群を生成し、階層上の子ノードは各値ノードの配下に接続(REQ-035)
                    var tagNameNode = NewNode(NodeKind.TagName, displayBase, node, tag, value: null);
                    foreach (var value in distinct)
                    {
                        var valueNode = NewNode(NodeKind.Value, value, node, tag, value);
                        ExpandChildren(valueNode.Children, node.Id, byParent, tags, values, path, visited, warnings);
                        tagNameNode.Children.Add(valueNode);
                    }

                    target.Add(tagNameNode);
                    return;
            }
        }
        finally
        {
            path.Remove(node.Id);
        }
    }

    private static GraphNode NewNode(NodeKind kind, string displayName, HierarchyNode source, Tag tag, string? value)
    {
        return new GraphNode
        {
            Kind = kind,
            DisplayName = displayName,
            HierarchyNodeId = source.Id,
            TagId = tag.Id,
            TagType = tag.Type,
            Value = value,
            ConditionType = source.ConditionType,
            ConditionValue = source.ConditionValue,
        };
    }

    /// <summary>
    /// textual タグの値抽出(REQ-035): distinct 値(predefined_values 外の自由入力値も含む)を
    /// ノードの condition_type で制限し、序数昇順で返す。
    /// </summary>
    private static List<string> ExtractValues(
        HierarchyNode node, Tag tag, ITagValueSource values, List<GraphWarning> warnings)
    {
        IEnumerable<string> result = values.GetDistinctValues(tag.Id).Where(v => v is not null);

        if (node.ConditionType is { } conditionType)
        {
            var filter = HierarchyConditionFilter.Create(node.Id, conditionType, node.ConditionValue, warnings);
            result = filter is null ? [] : result.Where(filter);
        }

        return result
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>condition_type 別の値制限(仕様 §2.4 condition_value スキーマ)。不正入力は null(=0 件)+警告。</summary>
    private static class HierarchyConditionFilter
    {
        public static Func<string, bool>? Create(
            string hierarchyNodeId,
            HierarchyConditionType conditionType,
            string? conditionValue,
            List<GraphWarning> warnings)
        {
            switch (conditionType)
            {
                case HierarchyConditionType.Equals:
                {
                    if (HierarchyConditionValue.TryGetString(conditionValue, "value", out var expected))
                    {
                        return v => string.Equals(v, expected, StringComparison.Ordinal);
                    }

                    break;
                }

                case HierarchyConditionType.Values:
                {
                    if (HierarchyConditionValue.TryGetStringArray(conditionValue, "values", out var list))
                    {
                        return v => list.Contains(v, StringComparer.Ordinal);
                    }

                    break;
                }

                case HierarchyConditionType.Pattern:
                {
                    if (HierarchyConditionValue.TryGetString(conditionValue, "pattern", out var pattern))
                    {
                        try
                        {
                            // K-REGEX: 部分一致・タイムアウト 1 秒。タイムアウト・不正は不成立扱い
                            var regex = new Regex(pattern, RegexOptions.None, PatternTimeout);
                            return v =>
                            {
                                try
                                {
                                    return regex.IsMatch(v);
                                }
                                catch (RegexMatchTimeoutException)
                                {
                                    return false;
                                }
                            };
                        }
                        catch (ArgumentException)
                        {
                            // 不正パターン → 下の警告共通処理へ
                        }
                    }

                    break;
                }

                case HierarchyConditionType.Range:
                {
                    if (HierarchyConditionValue.TryGetRange(conditionValue, out var from, out var to))
                    {
                        return v => HierarchyConditionValue.TryParseNumber(v, out var n) && n >= from && n <= to;
                    }

                    break;
                }

                default:
                    break;
            }

            warnings.Add(new GraphWarning(
                hierarchyNodeId, GraphWarningKind.InvalidConditionValue,
                "condition_value が不正のため値制限を適用できません(0 件扱い)。"));
            return null;
        }
    }
}
