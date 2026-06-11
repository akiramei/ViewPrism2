using Avalonia;

namespace ViewPrism2.App;

internal static class Program
{
    /// <summary>プロセス存続中保持する単一インスタンスミューテックス(K-AVALONIA)。</summary>
    private static Mutex? _singleInstanceMutex;

    [STAThread]
    public static void Main(string[] args)
    {
        // 多重起動禁止(仕様 §4 / K-AVALONIA): 名前付き Mutex(Global\ViewPrism2)。
        // 2 つ目はプロセス終了(既存アクティブ化は V1 ではベストエフォート=未実装)
        if (!TryAcquireSingleInstance())
        {
            return;
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
        }
    }

    // K-AVALONIA: AppBuilder.Configure<App>().UsePlatformDetect()。フォントは Inter(K-DESIGN)
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont();

    private static bool TryAcquireSingleInstance()
    {
        try
        {
            _singleInstanceMutex = new Mutex(initiallyOwned: true, @"Global\ViewPrism2", out var createdNew);
            return createdNew;
        }
        catch (UnauthorizedAccessException)
        {
            // 他セッションの既存 Mutex にアクセスできない場合も「既に起動中」とみなす
            return false;
        }
    }
}
