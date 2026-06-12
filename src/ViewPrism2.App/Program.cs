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
        catch (Exception ex)
        {
            // DF-1 最後の砦: メインループ外へ漏れた例外(Dispatcher フックで防げない致命例外)も
            // スタック全文をログへ残してから終了する
            WriteFatalLog(ex);
            throw;
        }
        finally
        {
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
        }
    }

    /// <summary>致命例外の最終ログ(%APPDATA%/ViewPrism2/logs/fatal.log)。失敗しても落とさない。</summary>
    private static void WriteFatalLog(Exception ex)
    {
        try
        {
            var logsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ViewPrism2", "logs");
            Directory.CreateDirectory(logsDir);
            File.AppendAllText(
                Path.Combine(logsDir, "fatal.log"),
                $"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ} [FATAL] {ex}{Environment.NewLine}");
        }
        catch (Exception logEx) when (logEx is IOException or UnauthorizedAccessException)
        {
            // ログ書き込み失敗は無視(これ以上できることがない)
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
