using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;
using Xunit;

namespace ViewPrism2.Oracle;

/// <summary>
/// S-11: タグ削除の波及(spec §2.2 REQ-028・INV-008・§2.3 エッジケース、EQ-001)。
/// ビュー条件・ビュー階層・子タグ・画像付与を持つタグを削除し、当該ビューを開いて
/// NodeGraph 構築+条件評価。構築・評価とも例外なし。階層から該当ノード消滅、
/// 条件は tag_id=NULL となり評価から無視(警告)、子タグはルート化。
/// </summary>
[Trait("oracle", "S-11")]
public sealed class S11TagDeletionRippleTests
{
    [Fact]
    public async Task タグ削除後もビューは例外なく開け波及が仕様どおり()
    {
        using var db = new OracleDb();

        // --- フィクスチャ: 画像 1 枚+タグ 3 種(削除対象/その子/階層用の別タグ) ---
        var folder = new SyncFolder { Id = IdGenerator.NewId(), Name = "pics", Path = "C:/oracle-s11" };
        Assert.True((await db.Folders.AddAsync(folder)).IsSuccess);

        var image = new ImageRecord
        {
            Id = IdGenerator.NewId(),
            SyncFolderId = folder.Id,
            RelativePath = "a.jpg",
            FileName = "a.jpg",
            FileSize = 10,
            Hash = new string('0', 64),
            Status = ImageStatus.Normal,
            CreatedDate = "2026-01-01T00:00:00.000Z",
            ModifiedDate = "2026-01-01T00:00:00.000Z",
        };
        await db.Images.AddAsync(image);

        var doomed = new Tag { Id = IdGenerator.NewId(), Name = "Genre", Type = TagType.Textual };
        var child = new Tag { Id = IdGenerator.NewId(), Name = "SubGenre", Type = TagType.Simple, ParentId = doomed.Id };
        var other = new Tag { Id = IdGenerator.NewId(), Name = "Star", Type = TagType.Simple };
        await db.Tags.AddAsync(doomed);
        await db.Tags.AddAsync(child);
        await db.Tags.AddAsync(other);

        // 画像付与
        await db.Tags.UpsertImageTagAsync(new ImageTag { ImageId = image.Id, TagId = doomed.Id, Value = "rock" });

        // ビュー(条件: exists(doomed)、階層: doomed ノード → 子に other ノード)
        var views = new ViewService(db.Views, db.Clock);
        var view = (await views.CreateAsync("oracle view")).Value!;
        var condition = (await views.AddConditionAsync(view.Id, doomed.Id, ConditionOperator.Exists)).Value!;
        var doomedNode = (await views.AddNodeAsync(view.Id, doomed.Id, parentId: null, position: 0)).Value!;
        var otherNode = (await views.AddNodeAsync(view.Id, other.Id, parentId: doomedNode.Id, position: 0)).Value!;

        // --- タグ削除(カスケードは FK、REQ-028) ---
        // ECO-045(O-a 裁定): 使用中タグはサービス層で削除拒否(REQ-082)になったため、削除入口を
        // repository 直呼びへ変更。本オラクルの契約(参照切れ後の FK 波及+読み取り耐性 INV-008)は
        // 不変 — 旧 DB・異常系で参照切れは起こり得るため検査意図は存続する。
        await db.Tags.DeleteAsync(doomed.Id);

        // --- 当該ビューを開く: NodeGraph 構築(例外が出ればテスト失敗として記録される) ---
        var hierarchy = await views.GetHierarchyAsync(view.Id);
        var allTags = await db.Tags.GetAllAsync();
        var tagsById = allTags.ToDictionary(t => t.Id, StringComparer.Ordinal);
        var valuesByTag = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var tag in allTags)
        {
            valuesByTag[tag.Id] = await db.Images.GetDistinctNormalTagValuesAsync(tag.Id);
        }

        var graph = new NodeGraphBuilder().BuildGraph(
            hierarchy, tagsById, TagValueIndex.FromValues(valuesByTag));

        // 階層から該当ノード消滅(view_tag_hierarchies.tag_id CASCADE)
        Assert.DoesNotContain(hierarchy, n => n.TagId == doomed.Id);
        Assert.DoesNotContain(Flatten(graph.Root), n => n.TagId == doomed.Id);

        // 残った子側ノード(other)は親消滅後も到達可能(parent_id SET NULL → ルート直下)
        Assert.Contains(Flatten(graph.Root), n => n.TagId == other.Id);

        // --- 条件評価: tag_id=NULL の条件は評価から無視(警告) ---
        var conditions = await views.GetConditionsAsync(view.Id);
        var orphaned = Assert.Single(conditions);
        Assert.Equal(condition.Id, orphaned.Id);
        Assert.Null(orphaned.TagId); // view_conditions.tag_id SET NULL

        var imagesWithTags = await LoadImagesWithTagsAsync(db);
        var evaluation = new ConditionEvaluator().Evaluate(imagesWithTags, conditions);

        Assert.Contains(evaluation.Warnings, w => w.Kind == EvalWarningKind.ConditionIgnored);
        // 無視された条件は絞り込みに作用しない → normal 画像は結果に残る
        Assert.Equal([image.Id], evaluation.MatchedImageIds.Order(StringComparer.Ordinal));

        // --- 子タグはルート化(tags.parent_id SET NULL) ---
        var orphanedChild = await db.Tags.GetByIdAsync(child.Id);
        Assert.NotNull(orphanedChild);
        Assert.Null(orphanedChild.ParentId);

        Assert.Equal(otherNode.TagId, other.Id); // フィクスチャ整合の自己検査
    }

    private static async Task<IReadOnlyList<ImageWithTags>> LoadImagesWithTagsAsync(OracleDb db)
    {
        var tagsById = (await db.Tags.GetAllAsync()).ToDictionary(t => t.Id, StringComparer.Ordinal);
        var result = new List<ImageWithTags>();
        foreach (var image in await db.Images.GetAllNormalAsync())
        {
            var tags = (await db.Tags.GetImageTagsAsync(image.Id))
                .Where(t => tagsById.ContainsKey(t.TagId))
                .Select(t => new EvalTagValue(t.TagId, tagsById[t.TagId].Type, t.Value))
                .ToList();
            result.Add(new ImageWithTags(image.Id, image.Status, tags));
        }

        return result;
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
}
