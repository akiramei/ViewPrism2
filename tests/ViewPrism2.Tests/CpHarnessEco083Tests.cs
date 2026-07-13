using System.Reflection;
using Avalonia.Headless;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// ECO-083: HeadlessApp の fail-fast 監視(ディスパッチループ静黙死→即時クラッシュ+原因例外顕在化)が
/// 前提とする Avalonia 内部構造を恒久 pin する。監視自体はフィールド不在時に黙ってスキップする設計
/// (実行時は安全側)のため、前提が崩れた場合はこの検査で顕在化させる(ECO-080 の 3 層原則=
/// 実行時スキップ+機械ゲートで検出)。ループ死亡→無限待ちの症状再現は共有セッションを殺すため
/// 恒久テスト化できない(ECO-083 本文 §4=一時注入の実測記録で代替)。
/// </summary>
[Trait("cp", "CP-UI-G1")]
public sealed class CpHarnessEco083Tests
{
    [Fact]
    public void 監視が前提とするdispatchTaskフィールドが存在しTask型である()
    {
        var field = typeof(HeadlessUnitTestSession).GetField(
            HeadlessApp.DispatchTaskFieldName, BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.True(field is not null,
            $"HeadlessUnitTestSession.{HeadlessApp.DispatchTaskFieldName} が見つからない — Avalonia 更新で"
            + " ECO-083 の fail-fast 監視が黙って無効化されている。監視の配線を新実装へ追随させること。");
        Assert.True(typeof(Task).IsAssignableFrom(field!.FieldType),
            $"{HeadlessApp.DispatchTaskFieldName} が Task 互換でない({field.FieldType})— 監視の配線を見直すこと。");
    }
}
