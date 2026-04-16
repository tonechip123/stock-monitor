using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text.Json;

namespace StockMonitor;

public static class AutoUpdater
{
    private const string VersionUrl = "http://8.147.70.248:8080/tools/version.json";
    private const string DownloadUrl = "http://8.147.70.248:8080/tools/StockMonitor.exe";

    public static string CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    public static bool CheckAndPrompt()
    {
        try
        {
            // 同步获取版本信息(快速, <1秒)
            using var wc = new WebClient();
            wc.Proxy = null;
            var json = wc.DownloadString(VersionUrl);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var latestVersion = root.GetProperty("version").GetString() ?? "0.0.0";
            var changelog = root.TryGetProperty("changelog", out var cl) ? cl.GetString() ?? "" : "";

            if (!IsNewer(latestVersion, CurrentVersion)) return false;

            var msg = $"发现新版本 v{latestVersion}\n当前版本 v{CurrentVersion}\n\n{changelog}\n\n是否立即升级?";
            var result = MessageBox.Show(msg, "共振监控 - 升级提示",
                MessageBoxButtons.YesNo, MessageBoxIcon.Information);

            if (result != DialogResult.Yes) return false;

            var dlForm = new DownloadForm();
            Application.Run(dlForm);
            return dlForm.Success;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsNewer(string latest, string current)
    {
        if (Version.TryParse(latest, out var v1) && Version.TryParse(current, out var v2))
            return v1 > v2;
        return false;
    }
}

internal class DownloadForm : Form
{
    private readonly ProgressBar _bar;
    private readonly Label _label;
    private readonly WebClient _wc;
    public bool Success { get; private set; }

    private string _newPath = "";
    private string _exePath = "";

    private const string DownloadUrl = "http://8.147.70.248:8080/tools/StockMonitor.exe";

    public DownloadForm()
    {
        Text = "共振监控 - 正在下载升级";
        Size = new Size(420, 130);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ControlBox = false;

        _bar = new ProgressBar
        {
            Location = new Point(20, 15),
            Size = new Size(360, 28),
            Maximum = 100
        };

        _label = new Label
        {
            Location = new Point(20, 50),
            Size = new Size(360, 20),
            Text = "正在连接服务器...",
            TextAlign = ContentAlignment.MiddleCenter
        };

        Controls.AddRange(new Control[] { _bar, _label });

        // WebClient异步下载 — 自带进度事件, 不阻塞UI
        _wc = new WebClient();
        _wc.Proxy = null;
        _wc.DownloadProgressChanged += OnProgress;
        _wc.DownloadFileCompleted += OnCompleted;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        _exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
        if (string.IsNullOrEmpty(_exePath)) { Close(); return; }

        _newPath = _exePath + ".new";

        // 启动异步下载(WebClient内部用后台线程, 进度事件自动回到UI线程)
        _wc.DownloadFileAsync(new Uri(DownloadUrl), _newPath);
    }

    private void OnProgress(object sender, DownloadProgressChangedEventArgs e)
    {
        _bar.Value = e.ProgressPercentage;
        var dlMB = e.BytesReceived / 1024 / 1024;
        var totalMB = e.TotalBytesToReceive / 1024 / 1024;
        _label.Text = $"下载中 {dlMB}MB / {totalMB}MB ({e.ProgressPercentage}%)";
    }

    private void OnCompleted(object? sender, System.ComponentModel.AsyncCompletedEventArgs e)
    {
        if (e.Error != null)
        {
            _label.Text = $"下载失败: {e.Error.Message}";
            _bar.Value = 0;
            var timer = new System.Windows.Forms.Timer { Interval = 3000 };
            timer.Tick += (_, _) => { timer.Stop(); Close(); };
            timer.Start();
            return;
        }

        // 下载成功, 写替换脚本
        var oldPath = _exePath + ".old";
        var batPath = Path.Combine(Path.GetTempPath(), "stockmonitor_update.bat");
        var bat = $"""
            @echo off
            :wait
            tasklist /fi "PID eq {Environment.ProcessId}" | find "{Environment.ProcessId}" >nul
            if not errorlevel 1 (
                timeout /t 1 /nobreak >nul
                goto wait
            )
            move /y "{_exePath}" "{oldPath}"
            move /y "{_newPath}" "{_exePath}"
            start "" "{_exePath}"
            timeout /t 3 /nobreak >nul
            del /f "{oldPath}" >nul 2>&1
            del /f "%~f0" >nul 2>&1
            """;
        File.WriteAllText(batPath, bat, System.Text.Encoding.Default);

        Process.Start(new ProcessStartInfo
        {
            FileName = batPath,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });

        Success = true;
        _label.Text = "下载完成, 正在重启...";
        var closeTimer = new System.Windows.Forms.Timer { Interval = 500 };
        closeTimer.Tick += (_, _) => { closeTimer.Stop(); Close(); };
        closeTimer.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _wc.Dispose();
        base.Dispose(disposing);
    }
}
