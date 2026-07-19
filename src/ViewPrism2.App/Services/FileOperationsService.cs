using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform; // SetTextAsync は IClipboard 拡張(ClipboardExtensions・Avalonia 12)

namespace ViewPrism2.App.Services;

/// <summary>
/// ファイル操作モード(ECO-112)の OS 連携。参照系のみ(パスのクリップボードコピー+
/// OS ファイルマネージャでの表示)で、実ファイルの変更は行わない(CAD image_tab.md「ファイル操作モード」)。
/// VM からはこの抽象経由で呼ぶ(unit テストはフェイクを注入)。
/// </summary>
public interface IFileOperationsService
{
    /// <summary>テキストを OS クリップボードへコピーする。</summary>
    Task CopyTextAsync(string text);

    /// <summary>
    /// 親フォルダを OS ファイルマネージャで開き、可能なら当該ファイルを選択状態にする。
    /// Windows=explorer /select・macOS=Finder reveal(open -R)・
    /// Linux=D-Bus ShowItems 試行→失敗時 xdg-open 親フォルダ(IMG-026③ 裁定 2026-07-19=実行時失敗フォールバック)。
    /// </summary>
    void RevealInFileManager(string absolutePath);
}

public sealed class FileOperationsService : IFileOperationsService
{
    public async Task CopyTextAsync(string text)
    {
        var clipboard = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow?.Clipboard
            : null;
        if (clipboard is null) return; // headless 等クリップボード不在環境では黙って何もしない(参照系)
        await clipboard.SetTextAsync(text).ConfigureAwait(true);
    }

    public void RevealInFileManager(string absolutePath)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{absolutePath}\"")
                { UseShellExecute = false });
            }
            else if (OperatingSystem.IsMacOS())
            {
                var psi = new ProcessStartInfo("open") { UseShellExecute = false };
                psi.ArgumentList.Add("-R");
                psi.ArgumentList.Add(absolutePath);
                Process.Start(psi);
            }
            else
            {
                // D-Bus 試行は同期待ち(最大3秒)を含むため UI スレッドから外す(R8 所見 4-3)。
                // 参照系=失敗しても黙って何もしない契約なので fire-and-forget でよい。
                _ = Task.Run(() =>
                {
                    try { RevealLinux(absolutePath); }
                    catch { /* 契約どおり沈黙 */ }
                });
            }
        }
        catch
        {
            // 起動不能環境でもアプリを落とさない(参照系・破壊なし)。失敗の常駐表示はしない(IMG-026② 裁定=無表現)。
        }
    }

    private static void RevealLinux(string absolutePath)
    {
        // IMG-026③ 裁定: 事前検出せず D-Bus ShowItems を試行し、失敗(未提供/非0終了)で親フォルダを開くへフォールバック。
        try
        {
            // dbus-send の array:string はカンマを要素区切りに解釈するためエスケープする(R8 所見 4-2。
            // AbsoluteUri はカンマを % エンコードしない。失敗しても xdg-open へフォールバックする fail-safe)
            var uri = new Uri(absolutePath).AbsoluteUri.Replace(",", "%2C");
            var psi = new ProcessStartInfo("dbus-send") { UseShellExecute = false };
            foreach (var arg in new[]
            {
                "--session", "--print-reply", "--dest=org.freedesktop.FileManager1",
                "/org/freedesktop/FileManager1", "org.freedesktop.FileManager1.ShowItems",
                $"array:string:{uri}", "string:",
            })
            {
                psi.ArgumentList.Add(arg);
            }
            using var proc = Process.Start(psi);
            if (proc is not null)
            {
                proc.WaitForExit(3000);
                if (proc.HasExited && proc.ExitCode == 0) return;
            }
        }
        catch
        {
            // dbus-send 不在等 → フォールバックへ
        }

        var parent = Path.GetDirectoryName(absolutePath);
        if (parent is null) return;
        Process.Start(new ProcessStartInfo("xdg-open") { UseShellExecute = false, ArgumentList = { parent } });
    }
}
