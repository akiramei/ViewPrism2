using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services.Repair;

namespace ViewPrism2.App.ViewModels;

/// <summary>
/// 整理トレイの条件検索: マージ先(dest)との属性一致トグルを SearchCriteria へ写像する(ECO-055)。
/// CAD(整理マージ モック v1/v2 共通)の意味論=「dest と選択属性が一致する画像を探す」。
/// 一致の定義(maintainer 裁定 2026-07-06): ハッシュ= SHA-256 完全一致 / 拡張子=完全一致(大小無視・
/// エンジン側でドット正規化)/ ファイル名= dest 名本体(拡張子除く)の部分一致 / サイズ=バイト完全一致 /
/// 更新日=同一日(UTC)。画像タブ・作業タブで共有する(転写ドリフト防止 — ECO-050 教訓)。
/// </summary>
public static class OrganizeCriteria
{
    public static SearchCriteria FromMergeTarget(
        ImageRecord dest, bool hash, bool ext, bool size, bool name, bool date) => new()
    {
        Hash = hash ? dest.Hash : null,
        Extension = ext && Path.GetExtension(dest.FileName) is { Length: > 1 } e ? e : null,
        SizeMin = size ? dest.FileSize : null,
        SizeMax = size ? dest.FileSize : null,
        NameContains = name && Path.GetFileNameWithoutExtension(dest.FileName) is { Length: > 0 } n ? n : null,
        MtimeFrom = date ? SameDayStart(dest.ModifiedDate) : null,
        MtimeTo = date ? SameDayEnd(dest.ModifiedDate) : null,
    };

    // 格納形式は ISO 8601 UTC(yyyy-MM-ddTHH:mm:ss.fffZ・INV-002)— 序数比較で同一日を範囲に写像する。
    // 形式外(先頭 10 桁が日付でない)は当日絞りをかけない(null=条件なし・例外にしない防御)。
    private static string? SameDayStart(string modifiedDate)
        => modifiedDate.Length >= 10 ? modifiedDate[..10] + "T00:00:00.000Z" : null;

    private static string? SameDayEnd(string modifiedDate)
        => modifiedDate.Length >= 10 ? modifiedDate[..10] + "T23:59:59.999Z" : null;
}
