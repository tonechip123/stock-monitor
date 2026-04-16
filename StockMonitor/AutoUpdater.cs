using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace StockMonitor;

public static class AutoUpdater
{
    private const string VersionUrl = "http://8.147.70.248:8080/tools/version.json";
    private const string DownloadUrl = "http://8.147.70.248:8080/tools/StockMonitor.exe";

    public static string CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>
    /// 同步检查版本(快速, 不阻塞), 有新版弹确认框, 确认后打开下载窗口
    /// 返回true表示正在升级, 主程序应退出
    /// </summary>
    public static bool CheckAndPrompt()
    {
        try
        {
            using var handler = new HttpClientHandler { UseProxy = false };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };

            var json = http.GetStringAsync(VersionUrl).GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var latestVersion = root.GetProperty("version").GetString() ?? "0.0.0";
            var changelog = root.TryGetProperty("changelog", out var cl) ? cl.GetString() ?? "" : "";

            if (!IsNewer(latestVersion, CurrentVersion)) return false;

            var msg = $"发现新版本 v{latestVersion}\n当前版本 v{CurrentVersion}\n\n{changelog}\n\n是否立即升级?";
            var result = MessageBox.Show(msg, "共振监控 - 升级提示",
                MessageBoxButtons.YesNo, MessageBoxIcon.Information);

            if (result != DialogResult.Yes) return false;

            // 打开下载窗口(独立消息循环, 不阻塞)
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

/// <summary>
/// 独立下载窗口 — 有自己的消息循环, 不会"未响应"
/// </summary>
internal class DownloadForm : Form
{
    private readonly ProgressBar _bar;
    private readonly Label _label;
    public bool Success { get; private set; }

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
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        await DownloadAndReplace();
    }

    private async Task DownloadAndReplace()
    {
        try
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath)) { Close(); return; }

            var newPath = exePath + ".new";
            var oldPath = exePath + ".old";

            using var handler = new HttpClientHandler { UseProxy = false };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };

            using var response = await http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            using var stream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(newPath, FileMode.Create, FileAccess.Write);

            var buffer = new byte[81920];
            long downloaded = 0;

            while (true)
            {
                var bytesRead = await stream.ReadAsync(buffer);
                if (bytesRead == 0) break;

                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                downloaded += bytesRead;

                if (totalBytes > 0)
                {
                    var percent = (int)(downloaded * 100 / totalBytes);
                    _bar.Value = Math.Min(percent, 100);
                    _label.Text = $"下载中 {downloaded / 1024 / 1024}MB / {totalBytes / 1024 / 1024}MB ({percent}%)";
                }
            }

            fileStream.Close();

            // 写替换脚本
            var batPath = Path.Combine(Path.GetTempPath(), "stockmonitor_update.bat");
            var bat = $"""
                @echo off
                :wait
                tasklist /fi "PID eq {Environment.ProcessId}" | find "{Environment.ProcessId}" >nul
                if not errorlevel 1 (
                    timeout /t 1 /nobreak >nul
                    goto wait
                )
                move /y "{exePath}" "{oldPath}"
                move /y "{newPath}" "{exePath}"
                start "" "{exePath}"
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
            await Task.Delay(500);
            Close();
        }
        catch (Exception ex)
        {
            _label.Text = $"下载失败: {ex.Message}";
            _bar.Value = 0;
            await Task.Delay(3000);
            Close();
        }
    }
}
