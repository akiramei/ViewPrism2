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
/// Dispatch は単一 UI スレッドへ直列化されるため、テストクラス間の並列実行とも安全。
/// (ECO-083 期はループが OperationCanceledException 以外で静黙死し以後の Dispatch が
/// 全て無限待ちになる故障増幅があったが、**Avalonia 12.1.1 の #21781 で上流解消**=
/// 例外は当該 Dispatch へ surface しループは生存する。ECO-141 で fail-fast 監視を撤去し、
/// 依存する挙動は CpHarnessEco083Tests が契約として恒久 pin する。)
/// </summary>
internal static class HeadlessApp
{
    // Owner/DB は不要(lifetime 無し=App の重い DI/DB 初期化はスキップされる)。
    public static readonly HeadlessUnitTestSession Session = Start();

    /// <summary>
    /// (ECO-084 是正中に定常化した初期化 race の恒久対処・51-cheat-log 2026-07-14 ①)
    /// セッションのプラットフォーム初期化(EnsureSharedApplication)は最初の Dispatch まで遅延される。
    /// テストクラスは並列に走るため、「初期化前に worker スレッドの VM 構築が Dispatcher.UIThread を
    /// 先取り → 初回初期化が VerifyAccess で死ぬ」race があり(当時の ECO-083 FailFast 監視が捕捉。
    /// 監視は ECO-141 で撤去済み — 12.1.1 では #21688 により当該例外も TCS へ routing され、
    /// 本 fixture の同期待ちで露出する)、
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

    private static HeadlessUnitTestSession Start()
    {
        // (ECO-141・2026-07-24)**ECO-083 の fail-fast 監視は撤去した**(内部フィールド
        // `_dispatchTask` をリフレクション取得し fault 時に Environment.FailFast していたもの)。
        // 存在理由= Avalonia.Headless 12.0.x/12.1.0 では work item から逃げた例外が consumer loop
        // ごと殺し、以後の Dispatch が TCS 永遠未完=全 Headless テストが全緑のまま無限待ちに
        // なったこと(我々の上流報告 AvaloniaUI/Avalonia#21770)。**12.1.1 の #21781 で解消**
        // (DispatchCore が例外を捕捉し TCS 完了前に当該 work item の結果へ格納→呼び出し元へ surface)。
        // 撤去は変更ログでなく**実測で裏取り**(ECO-141 §7・独立プロセス再現ハーネス
        // bomdd/reports/upstream-avalonia-headless-eco083/repro を陽性対照つきで実行):
        //   12.1.0(陽性対照)= poison/後続とも未完了=永久ハング再現
        //   12.1.1          = cleanup 段の例外も work item 直接 throw も自身の Dispatch へ faulted・
        //                     後続 Dispatch は完了=ループ生存
        // **限定(R8 の IL 実測による)**: consumer loop の catch は 12.1.0/12.1.1 とも
        // `OperationCanceledException` のみで**不変**。12.1.1 の追加保護は work item 側の 1 箇所だけ。
        // よって封じ込めが確認できたのは**外部から到達できる 2 経路(cleanup 段/work item 本体)**に限る。
        // queued action の prologue/epilogue・finally 本体・ExecutionContext.Run は依然無保護で、
        // そこから逃げれば ECO-083 と同じ静黙死が再発し得る(残余経路は Avalonia 内部のみ)。
        // **撤去による診断能力の後退を明示する**: 発火時は HangDump(5 分)+mini ダンプで検出できるが、
        // FailFast が出していた**原因例外全文の即時顕在化は失われる**(ECO-082/083 の真因確定は
        // これが決め手だった)。再発時は本コメントを起点に監視の再導入を検討すること。
        // 依存する挙動は CpHarnessEco083Tests が**内部フィールドの pin から挙動契約の pin へ**
        // 置き換えて恒久検査する(私的リフレクションへの依存自体を解消)。
        // なお **PerAssembly 明示(下記)・SessionInitFixture・HangDump は別事象のため維持**。

        // ECO-083(真因除去): StartNew(Type) の既定 isolation は PerTest=Dispatch ごとに
        // Application/Dispatcher を再作成し Avalonia プラットフォーム再初期化(SetupUnsafe→Compositor/
        // RenderLoop 再構築)が毎回走る。この再構築が間欠的にスレッドアフィニティ違反
        // (The calling thread cannot access this object)を起こし、保護外(DispatchCore の try 前)の
        // ためディスパッチループごと死んでいた(実発火スタックで確定)。本セッションは元来
        // 「プロセス共有・App リソース込み」の設計(上記クラスコメント)なので PerAssembly=
        // 単一 Application/Dispatcher の再利用へ明示し、毎回再初期化の構造自体を消す。
        return HeadlessUnitTestSession.StartNew(typeof(Entry), AvaloniaTestIsolationLevel.PerAssembly);
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
