using Avalonia.Threading;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// ヘッドレスセッションの**故障封じ込め契約**を恒久 pin する(ECO-083 由来・ECO-141 で改訂)。
///
/// ECO-083 期(Avalonia.Headless 12.0.x〜12.1.0)は、work item から逃げた例外が consumer loop
/// ごと殺し、以後の Dispatch が TCS 永遠未完=**全 Headless テストが全緑のまま無限待ち**になった
/// (我々の上流報告 AvaloniaUI/Avalonia#21770)。当時は内部フィールド `_dispatchTask` を
/// リフレクション監視して fail-fast 化していた。
///
/// **Avalonia 12.1.1 の #21781 で上流解消**(DispatchCore が例外を捕捉し TCS 完了前に当該
/// work item の結果へ格納→呼び出し元へ surface・ループは生存)。ECO-141 で監視を撤去し、
/// 検査対象を**内部構造の pin から、我々が実際に依存している挙動契約の pin へ**置き換えた
/// (私的リフレクションへの依存自体を解消=前提が壊れたら本検査が直接赤くなる)。
///
/// 注: 各 await の時間上限は**性能閾値ではなく liveness 保護**(退行時にスイート全体を
/// 無限待ちにせず、この検査自身を明示的に失敗させるため)。
/// </summary>
[Trait("cp", "CP-UI-G1")]
public sealed class CpHarnessEco083Tests
{
    private static readonly TimeSpan Liveness = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task cleanup段階で飛んだ例外は当該Dispatchへsurfaceしセッションは生存する()
    {
        const string marker = "ECO-141 contract: thrown by a posted job during cleanup RunJobs";

        // 残留ジョブを置いて work item の cleanup 段階(finally 内 RunJobs)で例外を飛ばす
        // = ECO-083/#21770 の故障増幅を起こしていた経路そのもの。
        // 注(R8 L-7): 「この Post が cleanup 段で消費される」ことは本テストからは直接観測できない
        // (根拠= ECO-141 §7 の IL 読解+再現ハーネスの陽性対照)。将来 Avalonia が RunJobs を
        // 保護領域内へ移すと本テストは緑のまま cleanup 経路の被覆だけ失う — 版更新時は §7 の手順で再確認する。
        var poison = HeadlessApp.Session.Dispatch(
            () => Dispatcher.UIThread.Post(() => throw new InvalidOperationException(marker)),
            CancellationToken.None);

        var faulted = await Record.ExceptionAsync(() => poison.WaitAsync(Liveness, TestContext.Current.CancellationToken));

        Assert.True(faulted is not null,
            "cleanup 段階の例外が握り潰された(当該 Dispatch へ surface していない)。");
        Assert.False(faulted is TimeoutException,
            $"cleanup 段階の例外で当該 Dispatch が完了しない(#21770 の退行= 以後の Headless テストが"
            + $" 無限待ちになる)。Avalonia のバージョンと HeadlessApp の防御を見直すこと。");
        Assert.Contains(marker, faulted!.GetBaseException().Message, StringComparison.Ordinal);

        // セッション生存= 以後の Dispatch が通ること(ここが死ぬと全 Headless テストが道連れ)。
        var alive = await HeadlessApp.Session.Dispatch(() => 1, CancellationToken.None).WaitAsync(Liveness, TestContext.Current.CancellationToken);
        Assert.Equal(1, alive);
    }

    [Fact]
    public async Task workItemコールバック自身の例外も当該Dispatchへsurfaceしセッションは生存する()
    {
        const string marker = "ECO-141 contract: thrown by the work item callback itself";
        Func<int> throwing = () => throw new InvalidOperationException(marker);

        var direct = HeadlessApp.Session.Dispatch(throwing, CancellationToken.None);
        var faulted = await Record.ExceptionAsync(() => direct.WaitAsync(Liveness, TestContext.Current.CancellationToken));

        Assert.True(faulted is not null, "work item の例外が握り潰された。");
        Assert.False(faulted is TimeoutException,
            "work item の例外で当該 Dispatch が完了しない(consumer loop 死亡の疑い)。");
        Assert.Contains(marker, faulted!.GetBaseException().Message, StringComparison.Ordinal);

        var alive = await HeadlessApp.Session.Dispatch(() => 2, CancellationToken.None).WaitAsync(Liveness, TestContext.Current.CancellationToken);
        Assert.Equal(2, alive);
    }
}
