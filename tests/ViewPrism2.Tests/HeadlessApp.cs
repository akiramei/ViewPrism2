using Avalonia;
using Avalonia.Headless;

namespace ViewPrism2.Tests;

/// <summary>
/// プロセス共有のヘッドレス UI セッション。App リソース(スタイル/ブラシ/アイコン)込みで
/// 実レイアウトパスを回す(GfViewerDrawerScrollTests 由来・ECO-040 で共有化)。
/// Avalonia の AppBuilder.Setup はプロセス 1 回制約のため、view をホストするテストは
/// 必ず本セッションを共有する(クラスごとの StartNew は 2 個目が初期化で落ちる)。
/// Dispatch は単一 UI スレッドへ直列化されるため、テストクラス間の並列実行とも安全。
/// </summary>
internal static class HeadlessApp
{
    // Owner/DB は不要(lifetime 無し=App の重い DI/DB 初期化はスキップされる)。
    public static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.StartNew(typeof(Entry));

    /// <summary>ヘッドレスセッション用の AppBuilder エントリ(Inter フォント込み・実機と同等のテキスト計測)。</summary>
    private static class Entry
    {
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<ViewPrism2.App.App>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true })
                .WithInterFont();
    }
}
