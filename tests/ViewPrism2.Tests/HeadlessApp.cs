using System.Reflection;
using Avalonia;
using Avalonia.Headless;
using Xunit;

[assembly: AssemblyFixture(typeof(ViewPrism2.Tests.HeadlessApp.SessionInitFixture))]

namespace ViewPrism2.Tests;

/// <summary>
/// プロセス共有のヘッドレス UI セッション。App リソース(スタイル/ブラシ/アイコン)込みで
/// 実レイアウトパスを回す(GfViewerDrawerScrollTests 由来・ECO-040 で共有化)。
/// Avalonia の AppBuilder.Setup はプロセス 1 回制約のため、view をホストするテストは
/// 必ず本セッションを共有する(クラスごとの StartNew は 2 個目が初期化で落ちる)。
/// Dispatch は単一 UI スレッドへ直列化されるため、テストクラス間の並列実行とも安全 —
/// ただしディスパッチループが生きている限り(ECO-083: ループは OperationCanceledException 以外の
/// 例外で静黙死し、以後の Dispatch が全て無限待ちになる。下記 fail-fast 監視で防御)。
/// </summary>
internal static class HeadlessApp
{
    // Owner/DB は不要(lifetime 無し=App の重い DI/DB 初期化はスキップされる)。
    public static readonly HeadlessUnitTestSession Session = Start();

    /// <summary>
    /// (ECO-084 是正中に定常化した初期化 race の恒久対処・51-cheat-log 2026-07-14 ①)
    /// セッションのプラットフォーム初期化(EnsureSharedApplication)は最初の Dispatch まで遅延される。
    /// テストクラスは並列に走るため、「初期化前に worker スレッドの VM 構築が Dispatcher.UIThread を
    /// 先取り → 初回初期化が VerifyAccess で死ぬ」race があり(ECO-083 の FailFast 監視が捕捉)、
    /// テスト集合の増減で発火配置が変わる(ECO-084 の 9 本追加で 3/3 定常発火を実測)。
    /// 対処= xunit v3 の AssemblyFixture: どのテストよりも先に初期化 Dispatch を同期完了させ、
    /// 順序を構造的に消す。注: [ModuleInitializer] での同期待ちは不可 — dispatch コールバック
    /// (本モジュールのラムダ)の実行がモジュール初期化完了を要求し、ローダーと相互待ちで
    /// デッドロックする(実測: 起動前ハングで HangDump 発火)。
    /// </summary>
    public sealed class SessionInitFixture
    {
        public SessionInitFixture() =>
            Session.Dispatch(() => true, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>ECO-083 監視が前提とする内部フィールド名(存在は CpHarnessEco083Tests が固定)。</summary>
    internal const string DispatchTaskFieldName = "_dispatchTask";

    private static HeadlessUnitTestSession Start()
    {
        // ECO-083(真因除去): StartNew(Type) の既定 isolation は PerTest=Dispatch ごとに
        // Application/Dispatcher を再作成し Avalonia プラットフォーム再初期化(SetupUnsafe→Compositor/
        // RenderLoop 再構築)が毎回走る。この再構築が間欠的にスレッドアフィニティ違反
        // (The calling thread cannot access this object)を起こし、保護外(DispatchCore の try 前)の
        // ためディスパッチループごと死んでいた(実発火スタックで確定)。本セッションは元来
        // 「プロセス共有・App リソース込み」の設計(上記クラスコメント)なので PerAssembly=
        // 単一 Application/Dispatcher の再利用へ明示し、毎回再初期化の構造自体を消す。
        var session = HeadlessUnitTestSession.StartNew(typeof(Entry), AvaloniaTestIsolationLevel.PerAssembly);

        // ECO-083: ディスパッチループの静黙死を fail-fast 化。
        // Avalonia.Headless 12.0.4 のループは OperationCanceledException しか握らず、テスト完了処理
        // (finally 内 Dispatcher.UIThread.RunJobs)で残留ジョブの未処理例外が飛ぶとループごと死ぬ。
        // 死ぬと以後の Dispatch は TCS 永遠未完=全 Headless テストが全緑のまま無限待ち(テスト失敗として
        // 観測されない)。ここで内部の dispatch タスクを監視し、fault 時は原因例外全文つきで即時クラッシュ
        // させる(沈黙ハング 5 分待ち=HangDump の最終安全弁より前段の一次防衛+真犯人の顕在化)。
        // フィールド不在(Avalonia 更新)時は監視スキップ=挙動は従来どおり(存在は検査で恒久 pin)。
        if (typeof(HeadlessUnitTestSession)
                .GetField(DispatchTaskFieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(session) is Task dispatchTask)
        {
            dispatchTask.ContinueWith(
                t => Environment.FailFast(
                    "ECO-083: HeadlessUnitTestSession のディスパッチループが未処理例外で死亡。"
                    + "以後の Headless テストは全て無限待ちになるため即時終了する。原因例外: " + t.Exception,
                    t.Exception?.GetBaseException()),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        return session;
    }

    /// <summary>ヘッドレスセッション用の AppBuilder エントリ(Inter フォント込み・実機と同等のテキスト計測)。</summary>
    private static class Entry
    {
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<ViewPrism2.App.App>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true })
                .WithInterFont();
    }
}
