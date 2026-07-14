using System.Globalization;
using ViewPrism2.Core.Models;

namespace ViewPrism2.Core.Services.Package;

/// <summary>
/// パッケージ内タグ定義(ECO-073 §3.1。JSON 表現から独立した Core 入力)。
/// ValueDomain(REQ-095/ECO-086)は解釈属性のため意味定義一致(③)の比較対象に含めない
/// (旧パッケージ=欠落は Suggest 扱い・新規作成時のみ適用)。
/// </summary>
public sealed record PackageTagDef(
    string SourceId,
    string Name,
    TagType Type,
    string? ParentSourceId,
    string? Color,
    string? Description,
    IReadOnlyList<string> PredefinedValues,
    double? Min,
    double? Max,
    double? Step,
    string? Unit,
    TagValueDomain ValueDomain = TagValueDomain.Suggest);

/// <summary>タグ 1 件の取り込み分類(§3.2 の照合 5 段)。</summary>
public enum TagImportDecision
{
    /// <summary>①永続マッピング一致(検証済み)。</summary>
    MappedByPersistentMapping,

    /// <summary>②UUID 一致=同一タグ(値互換検証済み)。</summary>
    MappedById,

    /// <summary>③意味定義完全一致の自動マッピング(対応関係を保存する)。</summary>
    MappedBySemantic,

    /// <summary>④衝突なし → source UUID を維持して新規作成。</summary>
    CreateNew,

    /// <summary>⑤競合(名前一致・意味定義不一致、または型変更等の値互換差分)。解決が必要。</summary>
    Conflict,

    /// <summary>競合の解決=スキップ(このタグの付与は取り込まない)。</summary>
    ResolvedSkip,

    /// <summary>競合の解決=別名で取込(新規作成・名前を変更)。</summary>
    ResolvedRename,

    /// <summary>競合の解決=既存タグへ手動マッピング(型互換のみ・対応関係を保存する)。</summary>
    ResolvedManualMap,
}

/// <summary>競合の種別(CAD B-3 の提示文言の根拠)。</summary>
public enum TagConflictKind
{
    /// <summary>名前一致・意味定義不一致(別階層の同名タグ等)。</summary>
    NameCollision,

    /// <summary>UUID/マッピング一致だが型が変更されている(値互換性に影響)。</summary>
    TypeChanged,
}

/// <summary>競合へのユーザー解決(B-3 の 4 択。中止=解決を与えず適用しない)。</summary>
public sealed record TagConflictResolution(TagImportDecision Kind, string? RenameTo = null, string? MapToLocalTagId = null);

/// <summary>タグ 1 件の計画結果。</summary>
public sealed record TagPlanItem(
    PackageTagDef Source,
    TagImportDecision Decision,
    string? LocalTagId,
    string? CreateName,
    TagConflictKind? ConflictKind,
    string? Detail);

/// <summary>計画全体。Errors が空でない場合はファイル不正(取り込み不可)。</summary>
public sealed record TagImportPlan(
    IReadOnlyList<TagPlanItem> Items,
    IReadOnlyList<string> Errors)
{
    /// <summary>未解決の競合(実行ブロック)。</summary>
    public IReadOnlyList<TagPlanItem> UnresolvedConflicts =>
        [.. Items.Where(i => i.Decision == TagImportDecision.Conflict)];

    /// <summary>新規作成するタグ(親が先になる順=入力順を保存)。</summary>
    public IReadOnlyList<TagPlanItem> Creations =>
        [.. Items.Where(i => i.Decision is TagImportDecision.CreateNew or TagImportDecision.ResolvedRename)];
}

/// <summary>取り込み先ローカル状態のスナップショット(DB 非依存の入力)。</summary>
public sealed record LocalTagState(
    IReadOnlyList<Tag> Tags,
    IReadOnlyDictionary<string, TextualTagSettings> Textual,
    IReadOnlyDictionary<string, NumericTagSettings> Numeric,
    IReadOnlyDictionary<string, string> PersistentMappings);

/// <summary>
/// タグ取り込みプランナ(ECO-073 §3.2・純粋計算)。コレクション専用処理へ閉じ込めず、
/// 将来の tag-catalog/view-set からも再利用できる独立コンポーネント(§3.7)。
/// 照合順: ①永続マッピング → ②UUID一致 → ③意味定義完全一致(名前+型+型別設定+親の解決先)
/// → ④衝突なしなら UUID 維持で新規作成 → ⑤名前一致・意味定義不一致は競合。
/// ①②とも値互換検証(型変更=競合)を省略しない。親は入力のトポロジカル順で解決する。
/// </summary>
public static class TagImportPlanner
{
    public static TagImportPlan Plan(
        IReadOnlyList<PackageTagDef> packageTags,
        LocalTagState local,
        IReadOnlyDictionary<string, TagConflictResolution>? resolutions = null)
    {
        ArgumentNullException.ThrowIfNull(packageTags);
        ArgumentNullException.ThrowIfNull(local);
        resolutions ??= new Dictionary<string, TagConflictResolution>();

        var errors = new List<string>();
        var byId = packageTags.ToDictionary(t => t.SourceId, StringComparer.Ordinal);
        if (byId.Count != packageTags.Count)
        {
            errors.Add("パッケージ内にタグ source_id の重複があります。");
        }

        // 親参照の存在+循環検査(厳格拒否 §3.5)
        foreach (var tag in packageTags)
        {
            if (tag.ParentSourceId is not null && !byId.ContainsKey(tag.ParentSourceId))
            {
                errors.Add($"タグ '{tag.Name}' の親 {tag.ParentSourceId} がパッケージ内にありません。");
            }
        }

        var ordered = TopologicalOrder(packageTags, byId, errors);
        if (errors.Count > 0)
        {
            return new TagImportPlan([], errors);
        }

        var localById = local.Tags.ToDictionary(t => t.Id, StringComparer.Ordinal);
        var localByName = local.Tags.ToDictionary(t => t.Name, StringComparer.Ordinal);
        // package source_id → 解決先ローカル tag id(新規作成は source_id を維持するため自己)
        var resolvedLocal = new Dictionary<string, string?>(StringComparer.Ordinal);
        var items = new List<TagPlanItem>(ordered.Count);

        foreach (var tag in ordered)
        {
            var item = PlanOne(tag, local, localById, localByName, resolvedLocal, resolutions);
            resolvedLocal[tag.SourceId] = item.Decision switch
            {
                TagImportDecision.MappedByPersistentMapping or TagImportDecision.MappedById
                    or TagImportDecision.MappedBySemantic or TagImportDecision.ResolvedManualMap => item.LocalTagId,
                TagImportDecision.CreateNew => tag.SourceId,
                TagImportDecision.ResolvedRename => tag.SourceId,
                _ => null, // Conflict / ResolvedSkip: 子の親解決には使えない(null 親として作成される)
            };
            items.Add(item);
        }

        // 入力順(表示は元の並び)へ戻す
        var byInput = items.ToDictionary(i => i.Source.SourceId, StringComparer.Ordinal);
        return new TagImportPlan([.. packageTags.Select(t => byInput[t.SourceId])], errors);
    }

    private static TagPlanItem PlanOne(
        PackageTagDef tag,
        LocalTagState local,
        Dictionary<string, Tag> localById,
        Dictionary<string, Tag> localByName,
        Dictionary<string, string?> resolvedLocal,
        IReadOnlyDictionary<string, TagConflictResolution> resolutions)
    {
        // ①永続マッピング(検証は省略しない: ローカル存在は FK 保証・型互換のみ再検証)
        if (local.PersistentMappings.TryGetValue(tag.SourceId, out var mappedId) &&
            localById.TryGetValue(mappedId, out var mapped))
        {
            return mapped.Type == tag.Type
                ? new TagPlanItem(tag, TagImportDecision.MappedByPersistentMapping, mapped.Id, null, null,
                    $"永続マッピング → {mapped.Name}")
                : Conflicted(tag, TagConflictKind.TypeChanged,
                    $"マッピング先 '{mapped.Name}' の型が {mapped.Type} に変更されています", resolutions, localById);
        }

        // ②UUID 一致=同一タグ。表示/構造差分は現行維持、型変更は競合(値互換差分・黙ってクランプしない)
        if (localById.TryGetValue(tag.SourceId, out var same))
        {
            return same.Type == tag.Type
                ? new TagPlanItem(tag, TagImportDecision.MappedById, same.Id, null, null,
                    same.Name == tag.Name ? null : $"現行名 '{same.Name}' を維持")
                : Conflicted(tag, TagConflictKind.TypeChanged,
                    $"同一タグの型が {tag.Type} → {same.Type} に変更されています", resolutions, localById);
        }

        // ③④⑤名前照合
        if (localByName.TryGetValue(tag.Name, out var sameName))
        {
            return IsSemanticallyIdentical(tag, sameName, local, resolvedLocal)
                // ③意味定義完全一致 → 自動マッピング(対応関係は適用時に永続化)
                ? new TagPlanItem(tag, TagImportDecision.MappedBySemantic, sameName.Id, null, null, null)
                // ⑤名前一致・意味定義不一致 → 競合
                : Conflicted(tag, TagConflictKind.NameCollision,
                    $"既存 '{sameName.Name}' と名前が衝突(意味定義が異なる)", resolutions, localById);
        }

        // ④衝突なし → source UUID を維持して新規作成
        return new TagPlanItem(tag, TagImportDecision.CreateNew, null, tag.Name, null, null);
    }

    private static TagPlanItem Conflicted(
        PackageTagDef tag,
        TagConflictKind kind,
        string detail,
        IReadOnlyDictionary<string, TagConflictResolution> resolutions,
        Dictionary<string, Tag> localById)
    {
        if (!resolutions.TryGetValue(tag.SourceId, out var res))
        {
            return new TagPlanItem(tag, TagImportDecision.Conflict, null, null, kind, detail);
        }

        return res.Kind switch
        {
            TagImportDecision.ResolvedSkip => new TagPlanItem(tag, TagImportDecision.ResolvedSkip, null, null, kind, detail),
            TagImportDecision.ResolvedRename when !string.IsNullOrWhiteSpace(res.RenameTo) =>
                new TagPlanItem(tag, TagImportDecision.ResolvedRename, null, res.RenameTo, kind, detail),
            // 手動マッピングは値の型が互換なタグのみ受理(CAD 実装契約)
            TagImportDecision.ResolvedManualMap when res.MapToLocalTagId is not null &&
                localById.TryGetValue(res.MapToLocalTagId, out var target) && target.Type == tag.Type =>
                new TagPlanItem(tag, TagImportDecision.ResolvedManualMap, target.Id, null, kind, detail),
            _ => new TagPlanItem(tag, TagImportDecision.Conflict, null, null, kind, detail + "(解決指定が不正)"),
        };
    }

    /// <summary>③の意味定義完全一致: 名前+型+型別設定+親の解決先(§3.2。名前+型だけの自動統合はしない)。</summary>
    private static bool IsSemanticallyIdentical(
        PackageTagDef tag, Tag candidate, LocalTagState local, Dictionary<string, string?> resolvedLocal)
    {
        if (candidate.Name != tag.Name || candidate.Type != tag.Type)
        {
            return false;
        }

        // 親の解決先: パッケージ親の解決結果とローカル親が一致すること(両方 null も一致)
        var expectedParent = tag.ParentSourceId is null
            ? null
            : resolvedLocal.GetValueOrDefault(tag.ParentSourceId);
        if (!string.Equals(candidate.ParentId, expectedParent, StringComparison.Ordinal))
        {
            return false;
        }

        return tag.Type switch
        {
            TagType.Textual => local.Textual.TryGetValue(candidate.Id, out var t)
                ? t.PredefinedValues.SequenceEqual(tag.PredefinedValues, StringComparer.Ordinal)
                : tag.PredefinedValues.Count == 0,
            TagType.Numeric => local.Numeric.TryGetValue(candidate.Id, out var n)
                ? n.Min == tag.Min && n.Max == tag.Max && n.Step == tag.Step &&
                  string.Equals(n.Unit, tag.Unit, StringComparison.Ordinal)
                : tag.Min is null && tag.Max is null && tag.Step is null && tag.Unit is null,
            _ => true,
        };
    }

    private static List<PackageTagDef> TopologicalOrder(
        IReadOnlyList<PackageTagDef> tags, Dictionary<string, PackageTagDef> byId, List<string> errors)
    {
        var ordered = new List<PackageTagDef>(tags.Count);
        var state = new Dictionary<string, int>(StringComparer.Ordinal); // 0=未 1=訪問中 2=完了

        bool Visit(PackageTagDef tag)
        {
            switch (state.GetValueOrDefault(tag.SourceId))
            {
                case 2: return true;
                case 1:
                    errors.Add($"タグ親子関係に循環があります: '{tag.Name}'");
                    return false;
            }

            state[tag.SourceId] = 1;
            if (tag.ParentSourceId is not null && byId.TryGetValue(tag.ParentSourceId, out var parent) && !Visit(parent))
            {
                return false;
            }

            state[tag.SourceId] = 2;
            ordered.Add(tag);
            return true;
        }

        foreach (var tag in tags)
        {
            if (!Visit(tag))
            {
                break;
            }
        }

        return ordered;
    }
}

/// <summary>
/// タグ値の正規形と互換性検証(ECO-073 §3.1: string|null・タグ定義が型の唯一の正・クランプ禁止)。
/// simple=null 必須 / textual=文字列 / numeric=InvariantCulture 正規化済み数値文字列。
/// </summary>
public static class TagValueFormat
{
    /// <summary>エクスポート時の正規化(4/4.0/04.000 の揺れを作らない)。不正値は null=書き出し不能。</summary>
    public static string? TryNormalizeNumeric(string raw)
    {
        if (!decimal.TryParse(raw, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        return value.ToString("0.#############################", CultureInfo.InvariantCulture);
    }

    /// <summary>取り込み値の検証(桁区切り/カンマ小数点/NaN/Infinity/範囲外を拒否)。null=有効。</summary>
    public static string? Validate(TagType type, string? value, NumericTagSettings? numeric)
    {
        switch (type)
        {
            case TagType.Simple:
                return value is null ? null : "simple タグの値は null でなければなりません";
            case TagType.Textual:
                return value is null ? "textual タグの値がありません" : null;
            default:
                if (value is null)
                {
                    return "numeric タグの値がありません";
                }

                if (!decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture, out var number))
                {
                    return $"数値として解釈できません: '{value}'";
                }

                if (numeric?.Min is { } min && (double)number < min)
                {
                    return $"最小値 {min} を下回ります: {value}";
                }

                if (numeric?.Max is { } max && (double)number > max)
                {
                    return $"最大値 {max} を超えます: {value}";
                }

                return null;
        }
    }

    /// <summary>数値の一致判定はパース後比較(文字列比較しない・§3.1)。</summary>
    public static bool ValuesEqual(TagType type, string? a, string? b)
    {
        if (type != TagType.Numeric)
        {
            return string.Equals(a, b, StringComparison.Ordinal);
        }

        if (a is null || b is null)
        {
            return a == b;
        }

        return decimal.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var da) &&
               decimal.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out var db) &&
               da == db;
    }
}
