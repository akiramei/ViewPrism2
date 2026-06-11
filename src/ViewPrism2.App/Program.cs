using Avalonia;

namespace ViewPrism2.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
        => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // K-AVALONIA: AppBuilder.Configure<App>().UsePlatformDetect()。フォントは Inter(K-DESIGN)
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont();
}
