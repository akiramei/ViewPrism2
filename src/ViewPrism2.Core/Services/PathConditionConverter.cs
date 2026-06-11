using ViewPrism2.Core.Models;

namespace ViewPrism2.Core.Services;

/// <summary>
/// パス→条件変換(OC-3、REQ-036)。選択ノードまでのパス上の全ノードから条件列を生成する
/// (§2.3 の評価器 OC-1 の入力形式)。
/// ルート=無条件 / simple・textual タグ名ノード=exists / 値ノード・一体型ノード=equals(その値)/
/// numeric ノード=condition_type に応じ equals または between(range の場合 valueFrom/valueTo)。
/// </summary>
public sealed class PathConditionConverter
{
    public IReadOnlyList<ViewCondition> BuildConditions(IReadOnlyList<GraphNode> pathFromRoot)
    {
        ArgumentNullException.ThrowIfNull(pathFromRoot);

        var conditions = new List<ViewCondition>();
        foreach (var node in pathFromRoot)
        {
            if (node.Kind == NodeKind.Root)
            {
                continue; // ルート=無条件
            }

            if (node.TagId is null)
            {
                continue; // 防御: タグなしノードは条件を生成しない(INV-008)
            }

            switch (node.Kind)
            {
                case NodeKind.Value:
                case NodeKind.Combined:
                    conditions.Add(NewCondition(node, conditions.Count, ConditionOperator.Equals, node.Value));
                    break;

                case NodeKind.TagName when node.TagType == TagType.Numeric:
                    conditions.Add(BuildNumericCondition(node, conditions.Count));
                    break;

                default:
                    // simple・textual タグ名ノード=exists(値 0 件の textual を選択した場合も exists、REQ-035)
                    conditions.Add(NewCondition(node, conditions.Count, ConditionOperator.Exists, null));
                    break;
            }
        }

        return conditions;
    }

    /// <summary>numeric ノードの条件生成。condition_type が無い・不正な場合は exists へフォールバック(INV-008)。</summary>
    private static ViewCondition BuildNumericCondition(GraphNode node, int index)
    {
        switch (node.ConditionType)
        {
            case HierarchyConditionType.Equals
                when HierarchyConditionValue.TryGetString(node.ConditionValue, "value", out var value):
                return NewCondition(node, index, ConditionOperator.Equals, value);

            case HierarchyConditionType.Range
                when HierarchyConditionValue.TryGetString(node.ConditionValue, "valueFrom", out var from) &&
                     HierarchyConditionValue.TryGetString(node.ConditionValue, "valueTo", out var to):
                return NewCondition(node, index, ConditionOperator.Between, from, to);

            default:
                return NewCondition(node, index, ConditionOperator.Exists, null);
        }
    }

    private static ViewCondition NewCondition(
        GraphNode node, int index, ConditionOperator op, string? value, string? value2 = null)
    {
        // OC-1 の評価にはタグ id・演算子・値のみが効く。Id はパス内で一意な合成 id(警告の紐付け用)
        return new ViewCondition
        {
            Id = $"{node.HierarchyNodeId}#{index}",
            ViewId = string.Empty,
            TagId = node.TagId,
            Operator = op,
            Value = value,
            Value2 = value2,
        };
    }
}
