using Avalonia.Threading;
using Microsoft.Extensions.Logging;

namespace ViewPrism2.App.Services;

/// <summary>
/// グローバル例外ハンドラ(NFR-002・CP-ROBUST-001、v1.3/ECO-002 DF-1)。
/// UI 操作起因の未処理例外でプロセスを終了させない: 捕捉した例外はスタック全文をログ
/// (%APPDATA%/ViewPrism2/logs/ — Serilog ファイルシンク)へ記録し、非モーダル通知
/// (ステータスバー)を要求する。<see cref="Handle"/> はテスト可能な純粋部品
/// (例外注入 → プロセス生存+ログ+通知フラグを unit で検査できる)。
/// </summary>
public sealed class GlobalExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler>? _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>非モーダル通知の要求(ステータスバーへ表示する利用者向け文言)。</summary>
    public event EventHandler<string>? NotificationRequested;

    /// <summary>利用者向け通知文言の生成(i18n)。未設定なら例外メッセージをそのまま使う。</summary>
    public Func<Exception, string>? FormatNotification { get; set; }

    /// <summary>捕捉済み件数(CP-ROBUST-001 の検査用)。</summary>
    public int HandledCount { get; private set; }

    /// <summary>
    /// 例外 1 件を処理する: スタック全文をログへ記録し、通知を要求する。
    /// いかなる場合も例外を外へ漏らさない(二次例外でプロセスを落とさない)。
    /// </summary>
    public void Handle(Exception exception, string source)
    {
        try
        {
            HandledCount++;

            // スタック全文の記録(DF-1 受入)。Serilog の File シンクは ToString() 相当
            // (型・メッセージ・スタックトレース・InnerException)を全文出力する
            _logger?.LogError(exception, "未処理例外を捕捉しました(発生元: {Source})", source);

            var message = FormatNotification?.Invoke(exception) ?? exception.Message;
            NotificationRequested?.Invoke(this, message);
        }
        catch
        {
            // 例外処理中の二次例外は握りつぶす(NFR-002: プロセス生存を最優先)
        }
    }

    /// <summary>
    /// プロセス全体の未処理例外フックを登録する(App 合成ルートから 1 回呼ぶ)。
    /// UI スレッド(Dispatcher)の例外は Handled=true でプロセス終了を防ぐ。
    /// </summary>
    public void Register()
    {
        // UI スレッドの未処理例外(イベントハンドラ・async void 継続を含む)
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            e.Handled = true; // プロセスを終了させない(NFR-002)
            Handle(e.Exception, "Dispatcher.UIThread");
        };

        // 待機されなかった Task の例外(ファイナライザ経由)。観測済みにして強制終了を防ぐ
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            e.SetObserved();
            Handle(e.Exception, "TaskScheduler.UnobservedTaskException");
        };

        // 最後の砦: ここに来た場合 CLR 仕様上プロセス終了は防げないが、スタック全文は記録する
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception)
            {
                Handle(exception, "AppDomain.UnhandledException");
            }
        };
    }
}
