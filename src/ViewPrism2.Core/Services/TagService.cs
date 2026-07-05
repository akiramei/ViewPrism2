using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;
using ViewPrism2.Core.Common;
using ViewPrism2.Core.Models;
using ViewPrism2.Core.Repositories;

namespace ViewPrism2.Core.Services;

/// <summary>タグ+使用数(REQ-029)。</summary>
public sealed record TagWithUsage(Tag Tag, int UsageCount);

/// <summary>パレット表示用のタグ+型別設定(ECO-009: 候補値/数値範囲をパレット行に提示)。</summary>
public sealed record PaletteTagItem(
    Tag Tag, int UsageCount, IReadOnlyList<string> PredefinedValues, NumericTagSettings? Numeric);

/// <summary>
/// タグ管理サービス(M-VIEWSVC-012、仕様 §2.2)。
/// バリデーション(色形式・名前空白・numeric 範囲・循環)・UPSERT 付与・原子バッチ・使用数。
/// </summary>
public sealed class TagService
{
    private static readonly Regex ColorPattern = new("^#[0-9A-Fa-f]{6}$", RegexOptions.None, TimeSpan.FromSeconds(1));

    private readonly ITagRepository _tags;

    public TagService(ITagRepository tags)
    {
        _tags = tags;
    }

    /// <summary>タグ作成(REQ-021〜023)。名前は一意(case-sensitive)・空白のみ拒否。</summary>
    public async Task<Result<Tag>> CreateAsync(
        string name, TagType type, string? parentId = null, string? color = null, string? description = null)
    {
        if (Validate(name, color) is { } invalid)
        {
            return Result<Tag>.Fail(invalid.Error!.Value, invalid.Message);
        }

        if (await _tags.GetByNameAsync(name).ConfigureAwait(false) is not null)
        {
            return Result<Tag>.Fail(ErrorCode.DuplicateTagName, $"タグ名 '{name}' は既に存在します。");
        }

        if (parentId is not null && await _tags.GetByIdAsync(parentId).ConfigureAwait(false) is null)
        {
            return Result<Tag>.Fail(ErrorCode.NotFound, "親タグが存在しません。");
        }

        var tag = new Tag
        {
            Id = IdGenerator.NewId(),
            Name = name,
            Type = type,
            ParentId = parentId,
            Color = color,
            Description = description,
        };
        await _tags.AddAsync(tag).ConfigureAwait(false);
        return Result<Tag>.Ok(tag);
    }

    /// <summary>タグ更新。親付け替えの循環(自己・子孫)は拒否(REQ-022 / INV-004)。</summary>
    public async Task<Result<Tag>> UpdateAsync(Tag tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        if (Validate(tag.Name, tag.Color) is { } invalid)
        {
            return Result<Tag>.Fail(invalid.Error!.Value, invalid.Message);
        }

        var current = await _tags.GetByIdAsync(tag.Id).ConfigureAwait(false);
        if (current is null)
        {
            return Result<Tag>.Fail(ErrorCode.NotFound, "タグが存在しません。");
        }

        var byName = await _tags.GetByNameAsync(tag.Name).ConfigureAwait(false);
        if (byName is not null && !string.Equals(byName.Id, tag.Id, StringComparison.Ordinal))
        {
            return Result<Tag>.Fail(ErrorCode.DuplicateTagName, $"タグ名 '{tag.Name}' は既に存在します。");
        }

        if (await CreatesCycleAsync(tag.Id, tag.ParentId).ConfigureAwait(false))
        {
            return Result<Tag>.Fail(ErrorCode.CircularReference, "自己または子孫を親に指定することはできません。");
        }

        await _tags.UpdateAsync(tag).ConfigureAwait(false);
        return Result<Tag>.Ok(tag);
    }

    /// <summary>
    /// タグ削除。使用中(画像への値付与・ビュー階層への配置・ビュー条件からの参照)は
    /// TagInUse で拒否する(REQ-082 / TAG-008 裁定・ECO-045)。子タグの親は「使用」でない
    /// (4a 裁定: ルート化 SET NULL 存続)。削除が通った場合のカスケードは FK(REQ-028)が
    /// 防御層として存続: image_tags/階層ノード消滅・条件 SET NULL・子 parent_id NULL。
    /// </summary>
    public async Task<Result> DeleteAsync(string id)
    {
        if (await _tags.GetByIdAsync(id).ConfigureAwait(false) is null)
        {
            return Result.Fail(ErrorCode.NotFound, "タグが存在しません。");
        }

        var usage = await _tags.GetUsageRefsAsync(id).ConfigureAwait(false);
        if (usage.InUse)
        {
            return Result.Fail(ErrorCode.TagInUse, "使用中のタグ定義は削除できません。画像への付与またはビューでの使用を解除してから削除してください。");
        }

        await _tags.DeleteAsync(id).ConfigureAwait(false);
        return Result.Ok();
    }

    /// <summary>textual の定義済み値リストを設定する(順序保持、REQ-024)。</summary>
    public async Task<Result> SetTextualSettingsAsync(string tagId, IReadOnlyList<string> predefinedValues)
    {
        ArgumentNullException.ThrowIfNull(predefinedValues);
        var tag = await _tags.GetByIdAsync(tagId).ConfigureAwait(false);
        if (tag is null)
        {
            return Result.Fail(ErrorCode.NotFound, "タグが存在しません。");
        }

        if (tag.Type != TagType.Textual)
        {
            return Result.Fail(ErrorCode.ValidationError, "textual タグ以外には定義済み値を設定できません。");
        }

        try
        {
            await _tags.UpsertTextualSettingsAsync(new TextualTagSettings
            {
                TagId = tagId,
                PredefinedValues = predefinedValues,
            }).ConfigureAwait(false);
            return Result.Ok();
        }
        catch (DbException)
        {
            // DB 例外を Result へ変換(v1.3/ECO-002 DF-2: 他メソッドとの非対称を是正)
            return Result.Fail(ErrorCode.Database, "定義済み値の保存に失敗しました。");
        }
    }

    /// <summary>numeric の min/max/step/unit を設定する(REQ-025。すべて null 可)。</summary>
    public async Task<Result> SetNumericSettingsAsync(
        string tagId, double? min, double? max, double? step, string? unit)
    {
        var tag = await _tags.GetByIdAsync(tagId).ConfigureAwait(false);
        if (tag is null)
        {
            return Result.Fail(ErrorCode.NotFound, "タグが存在しません。");
        }

        if (tag.Type != TagType.Numeric)
        {
            return Result.Fail(ErrorCode.ValidationError, "numeric タグ以外には数値設定を設定できません。");
        }

        if (min is { } lo && max is { } hi && lo > hi)
        {
            return Result.Fail(ErrorCode.ValidationError, "min は max 以下である必要があります。");
        }

        await _tags.UpsertNumericSettingsAsync(new NumericTagSettings
        {
            TagId = tagId,
            Min = min,
            Max = max,
            Step = step,
            Unit = unit,
        }).ConfigureAwait(false);
        return Result.Ok();
    }

    /// <summary>付与(UPSERT: 再付与=値上書き、行は増えない。REQ-026 / INV-003)。</summary>
    public async Task<Result> TagImageAsync(string imageId, string tagId, string? value)
    {
        var validated = await ValidateValueAsync(tagId, value).ConfigureAwait(false);
        if (!validated.IsSuccess)
        {
            return validated;
        }

        try
        {
            await _tags.UpsertImageTagAsync(new ImageTag { ImageId = imageId, TagId = tagId, Value = value })
                .ConfigureAwait(false);
            return Result.Ok();
        }
        catch (DbException)
        {
            // FK 違反(画像・タグの不存在)
            return Result.Fail(ErrorCode.NotFound, "画像またはタグが存在しません。");
        }
    }

    /// <summary>一括付与。単一トランザクション、失敗時全ロールバック(REQ-027 / INV-006)。</summary>
    public async Task<Result> TagImagesAsync(IReadOnlyList<string> imageIds, string tagId, string? value)
    {
        ArgumentNullException.ThrowIfNull(imageIds);
        var validated = await ValidateValueAsync(tagId, value).ConfigureAwait(false);
        if (!validated.IsSuccess)
        {
            return validated;
        }

        try
        {
            await _tags.TagImagesAsync(imageIds, tagId, value).ConfigureAwait(false);
            return Result.Ok();
        }
        catch (DbException)
        {
            return Result.Fail(ErrorCode.NotFound, "画像またはタグが存在しないため、バッチ全体を取り消しました。");
        }
    }

    /// <summary>
    /// 画像ごとに異なる値での一括付与(REQ-046 の連番適用)。
    /// 全値を適用前に検証し(REQ-025 範囲)、1 件でも不正なら 0 件適用。
    /// 適用は単一トランザクション、失敗時全ロールバック(REQ-027 / INV-006)。
    /// </summary>
    public async Task<Result> TagImagesWithValuesAsync(
        string tagId, IReadOnlyList<(string ImageId, string? Value)> assignments)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        foreach (var (_, value) in assignments)
        {
            var validated = await ValidateValueAsync(tagId, value).ConfigureAwait(false);
            if (!validated.IsSuccess)
            {
                return validated; // 1 件でも不正 → 適用 0 件
            }
        }

        try
        {
            await _tags.TagImagesWithValuesAsync(tagId, assignments).ConfigureAwait(false);
            return Result.Ok();
        }
        catch (DbException)
        {
            return Result.Fail(ErrorCode.NotFound, "画像またはタグが存在しないため、バッチ全体を取り消しました。");
        }
    }

    /// <summary>一括解除。冪等(無い行の解除はエラーにしない)・単一トランザクション(REQ-026/027)。</summary>
    public async Task<Result> UntagImagesAsync(IReadOnlyList<string> imageIds, string tagId)
    {
        ArgumentNullException.ThrowIfNull(imageIds);
        await _tags.UntagImagesAsync(imageIds, tagId).ConfigureAwait(false);
        return Result.Ok();
    }

    /// <summary>解除(冪等)。</summary>
    public async Task<Result> UntagImageAsync(string imageId, string tagId)
    {
        await _tags.RemoveImageTagAsync(imageId, tagId).ConfigureAwait(false);
        return Result.Ok();
    }

    /// <summary>一覧: name 昇順(OrdinalIgnoreCase・同値 id 昇順)+使用数(REQ-029)。</summary>
    public async Task<IReadOnlyList<TagWithUsage>> GetAllWithUsageAsync()
    {
        var all = await _tags.GetAllAsync().ConfigureAwait(false);
        var usage = await _tags.GetUsageCountsAsync().ConfigureAwait(false);
        return all
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Id, StringComparer.Ordinal)
            .Select(t => new TagWithUsage(t, usage.TryGetValue(t.Id, out var count) ? count : 0))
            .ToList();
    }

    /// <summary>
    /// パレット表示用に、各タグ+型別設定(textual 候補値 / numeric 範囲)を name 昇順で取得する
    /// (ECO-009/E-UI-TAGS-026 タグパレット行: 色+名前+型+候補値/範囲)。閲覧のみ。
    /// </summary>
    public async Task<IReadOnlyList<PaletteTagItem>> GetPaletteItemsAsync()
    {
        var withUsage = await GetAllWithUsageAsync().ConfigureAwait(false);
        var items = new List<PaletteTagItem>(withUsage.Count);
        foreach (var tu in withUsage)
        {
            IReadOnlyList<string> values = [];
            NumericTagSettings? numeric = null;
            if (tu.Tag.Type == TagType.Textual)
            {
                values = (await _tags.GetTextualSettingsAsync(tu.Tag.Id).ConfigureAwait(false))?.PredefinedValues ?? [];
            }
            else if (tu.Tag.Type == TagType.Numeric)
            {
                numeric = await _tags.GetNumericSettingsAsync(tu.Tag.Id).ConfigureAwait(false);
            }

            items.Add(new PaletteTagItem(tu.Tag, tu.UsageCount, values, numeric));
        }

        return items;
    }

    /// <summary>name 空白のみ・color 形式のバリデーション(REQ-021/023)。問題なければ null。</summary>
    private static Result? Validate(string name, string? color)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Fail(ErrorCode.ValidationError, "タグ名が空白のみです。");
        }

        if (color is not null && !ColorPattern.IsMatch(color))
        {
            return Result.Fail(ErrorCode.ValidationError, "color は #RRGGBB 形式のみ受理します。");
        }

        return null;
    }

    /// <summary>付与値の型別バリデーション(REQ-020/025)。</summary>
    private async Task<Result> ValidateValueAsync(string tagId, string? value)
    {
        var tag = await _tags.GetByIdAsync(tagId).ConfigureAwait(false);
        if (tag is null)
        {
            return Result.Fail(ErrorCode.NotFound, "タグが存在しません。");
        }

        switch (tag.Type)
        {
            case TagType.Simple:
                // simple は付いている/いないのみ(value=NULL)
                return value is null
                    ? Result.Ok()
                    : Result.Fail(ErrorCode.ValidationError, "simple タグには値を付与できません。");

            case TagType.Textual:
                // textual は value に文字列(predefined_values 外も許可、REQ-024)
                return value is not null
                    ? Result.Ok()
                    : Result.Fail(ErrorCode.ValidationError, "textual タグには文字列値が必要です。");

            case TagType.Numeric:
            default:
            {
                if (value is null ||
                    !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                {
                    return Result.Fail(ErrorCode.ValidationError, "numeric タグには数値が必要です(InvariantCulture)。");
                }

                var settings = await _tags.GetNumericSettingsAsync(tagId).ConfigureAwait(false);
                if (settings?.Min is { } min && number < min)
                {
                    return Result.Fail(ErrorCode.ValidationError, $"値が下限 {min} を下回っています。");
                }

                if (settings?.Max is { } max && number > max)
                {
                    return Result.Fail(ErrorCode.ValidationError, $"値が上限 {max} を超えています。");
                }

                return Result.Ok();
            }
        }
    }

    /// <summary>親付け替えが循環(自己・子孫を親に指定)を作るか(INV-004)。</summary>
    private async Task<bool> CreatesCycleAsync(string tagId, string? newParentId)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var cursor = newParentId;
        while (cursor is not null)
        {
            if (string.Equals(cursor, tagId, StringComparison.Ordinal))
            {
                return true;
            }

            if (!seen.Add(cursor))
            {
                return true; // 既存データ側の循環(防御)
            }

            var parent = await _tags.GetByIdAsync(cursor).ConfigureAwait(false);
            cursor = parent?.ParentId;
        }

        return false;
    }
}
