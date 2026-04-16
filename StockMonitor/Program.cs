namespace StockMonitor;

static class Program
{
    [STAThread]
    static void Main()
    {
        // 全局异常捕获，防止程序崩溃退出
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            LogError("UI线程异常", e.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            LogError("非UI线程异常", e.ExceptionObject as Exception);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogError("Task异常", e.Exception);
            e.SetObserved(); // 防止进程终止
        };

        ApplicationConfiguration.Initialize();

        // 启动时检查升级
        var needRestart = AutoUpdater.CheckAndUpdate().GetAwaiter().GetResult();
        if (needRestart)
        {
            Application.Exit();
            return;
        }

        Application.Run(new TrayApplicationContext());
    }

    private static void LogError(string source, Exception? ex)
    {
        try
        {
            var logDir = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(logDir);
            var logFile = Path.Combine(logDir, $"crash_{DateTime.Now:yyyyMMdd}.log");
            var msg = $"[{DateTime.Now:HH:mm:ss}] {source}: {ex?.Message}\n{ex?.StackTrace}\n\n";
            File.AppendAllText(logFile, msg);
        }
        catch { }
    }
}
