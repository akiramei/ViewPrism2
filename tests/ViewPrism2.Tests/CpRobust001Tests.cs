using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using ViewPrism2.App.Services;
using Xunit;

namespace ViewPrism2.Tests;

/// <summary>
/// CP-ROBUST-001(NFR-002、v1.3/ECO-002 DF-1): UI 未処理例外の封じ込め。
/// 例外注入でプロセスが生存し(Handle が例外を漏らさない)、スタック全文がログへ記録され、
/// 非モーダル通知(フラグ/文言)が要求されることを検査する。
/// 実アプリでのフック動作(Dispatcher.UIThread.UnhandledException)は L1 スモークで確認する。
/// </summary>
[Trait("cp", "CP-ROBUST-001")]
public sealed class CpRobust001Tests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "ViewPrism2.Tests", Guid.NewGuid().ToString("D"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // 一時ディレクトリの後始末失敗はテスト結果に影響させない
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>実スタックトレース付きの例外を生成する(throw → catch で StackTrace を確定させる)。</summary>
    private static Exception NewThrownException(string message)
    {
        try
        {
            throw new InvalidOperationException(message);
        }
        catch (InvalidOperationException ex)
        {
            return ex;
        }
    }

    [Fact]
    public void 例外注入でプロセス生存_スタック全文ログ_非モーダル通知()
    {
        // App と同じ Serilog ファイルシンク構成(%APPDATA%/ViewPrism2/logs/ 相当を一時ディレクトリへ)
        var logPath = Path.Combine(_directory, "logs", "app-test.log");
        var serilog = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(logPath)
            .CreateLogger();
        string? notified = null;
        int notifyCount = 0;

        using (var factory = new SerilogLoggerFactory(serilog, dispose: true))
        {
            var handler = new GlobalExceptionHandler(factory.CreateLogger<GlobalExceptionHandler>())
            {
                FormatNotification = _ => "予期しないエラーが発生しました(テスト)",
            };
            handler.NotificationRequested += (_, message) =>
            {
                notified = message;
                notifyCount++;
            };

            var injected = NewThrownException("CP-ROBUST-001 注入例外");

            // 例外注入 → Handle が例外を外へ漏らさない(=プロセス生存)
            var caught = Record.Exception(() => handler.Handle(injected, "unit-test"));
            Assert.Null(caught);
            Assert.Equal(1, handler.HandledCount);
        } // dispose でログをフラッシュ

        // 非モーダル通知(フラグ+文言)
        Assert.Equal(1, notifyCount);
        Assert.Equal("予期しないエラーが発生しました(テスト)", notified);

        // ログにスタック全文(型名・メッセージ・スタックフレーム)が記録されている
        var log = File.ReadAllText(logPath);
        Assert.Contains("System.InvalidOperationException", log, StringComparison.Ordinal);
        Assert.Contains("CP-ROBUST-001 注入例外", log, StringComparison.Ordinal);
        Assert.Contains("at ViewPrism2.Tests.CpRobust001Tests", log, StringComparison.Ordinal); // スタックフレーム
        Assert.Contains("unit-test", log, StringComparison.Ordinal); // 発生元
    }

    [Fact]
    public void 通知ハンドラが例外を投げても外へ漏らさない()
    {
        // 二次例外(通知側の失敗)でもプロセスを落とさない(NFR-002)
        var handler = new GlobalExceptionHandler(logger: null);
        handler.NotificationRequested += (_, _) => throw new InvalidOperationException("通知側の二次例外");

        var caught = Record.Exception(() => handler.Handle(NewThrownException("元例外"), "unit-test"));

        Assert.Null(caught);
    }

    [Fact]
    public void ロガー未設定でも安全に処理できる()
    {
        var handler = new GlobalExceptionHandler(logger: null);
        var notified = false;
        handler.NotificationRequested += (_, _) => notified = true;

        var caught = Record.Exception(() => handler.Handle(NewThrownException("元例外"), "unit-test"));

        Assert.Null(caught);
        Assert.True(notified);
        Assert.Equal(1, handler.HandledCount);
    }

    [Fact]
    public void 通知文言はFormatNotification未設定なら例外メッセージ()
    {
        var handler = new GlobalExceptionHandler(logger: null);
        string? notified = null;
        handler.NotificationRequested += (_, message) => notified = message;

        handler.Handle(NewThrownException("素のメッセージ"), "unit-test");

        Assert.Equal("素のメッセージ", notified);
    }
}
