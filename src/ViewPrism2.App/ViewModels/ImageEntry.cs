using ViewPrism2.Core.Models;
using ViewPrism2.Core.Services;

namespace ViewPrism2.App.ViewModels;

/// <summary>
/// 表示系で扱う画像 1 件(レコード+絶対パス+タグ付け状態)。
/// 評価器入力(OC-1 の ImageWithTags)と表示(サムネイル・ビューア・リスト列)の共通素材。
/// </summary>
public sealed record ImageEntry(ImageRecord Record, string AbsolutePath, IReadOnlyList<EvalTagValue> Tags)
{
    public ImageWithTags ToImageWithTags() => new(Record.Id, Record.Status, Tags);
}
