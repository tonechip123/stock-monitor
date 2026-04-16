using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace StockMonitor;

/// <summary>
/// 自动升级 — 启动时检查服务器版本, 有新版则下载替换重启
/// 服务器: http://8.147.70.248:8080/tools/version.json
/// </summary>
public static class AutoUpdater
{
    private const string VersionUrl = "http://8.147.70.248:8080/tools/version.json";
    private const string DownloadUrl = "http://8.147.70.248:8080/tools/StockMonitor.exe";

    private static readonly HttpClient Http;

    static AutoUpdater()
    {
        var handler = new HttpClientHandler { UseProxy = false };
        Http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
    }

    /// <summary>
    /// 获取当前版本号
    /// </summary>
    public static string CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>
    /// 检查并提示升级, 返回true表示需要重启(已下载新版)
    /// </summary>
    public static async Task<bool> CheckAndUpdate()
    {
        try
        {
            // 1. 获取服务器版本信息
            var json = await Http.GetStringAsync(VersionUrl);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var latestVersion = root.GetProperty("version").GetString() ?? "0.0.0";
            var changelog = root.TryGetProperty("changelog", out var cl) ? cl.GetString() ?? "" : "";

            // 2. 比较版本
            if (!IsNewer(latestVersion, CurrentVersion)) return false;

            // 3. 提示用户
            var msg = $"发现新版本 v{latestVersion}\n当前版本 v{CurrentVersion}\n\n{changelog}\n\n是否立即升级?";
            var result = MessageBox.Show(msg, "共振监控 - 升级提示",
                MessageBoxButtons.YesNo, MessageBoxIcon.Information);

            if (result != DialogResult.Yes) return false;

            // 4. 下载新版本
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath)) return false;

            var newPath = exePath + ".new";
            var oldPath = exePath + ".old";

            using (var progress = new DownloadProgressForm())
            {
                progress.Show();
                Application.DoEvents();

                using var response = await Http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                using var stream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(newPath, FileMode.Create, FileAccess.Write);

                var buffer = new byte[81920];
                long downloaded = 0;
                int bytesRead;

                while ((bytesRead = await stream.ReadAsync(buffer)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                    downloaded += bytesRead;

                    if (totalBytes > 0)
                    {
                        var percent = (int)(downloaded * 100 / totalBytes);
                        progress.UpdateProgress(percent, downloaded, totalBytes);
                        Application.DoEvents();
                    }
                }
            }

            // 5. 写替换脚本: 等待当前进程退出→重命名→启动新版→删除旧版
            var batPath = Path.Combine(Path.GetTempPath(), "stockmonitor_update.bat");
            var bat = $"""
                @echo off
                echo 正在升级共振监控...
                :wait
                tasklist /fi "PID eq {Environment.ProcessId}" | find "{Environment.ProcessId}" >nul
                if not errorlevel 1 (
                    timeout /t 1 /nobreak >nul
                    goto wait
                )
                move /y "{exePath}" "{oldPath}"
                move /y "{newPath}" "{exePath}"
                start "" "{exePath}"
                del /f "{oldPath}" >nul 2>&1
                del /f "%~f0" >nul 2>&1
                """;
            File.WriteAllText(batPath, bat, System.Text.Encoding.Default);

            // 6. 启动替换脚本, 退出当前程序
            Process.Start(new ProcessStartInfo
            {
                FileName = batPath,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });

            return true; // 通知主程序退出
        }
        catch
        {
            // 升级检查失败不影响正常使用
            return false;
        }
    }

    /// <summary>
    /// 比较版本号: latest > current ?
    /// </summary>
    private static bool IsNewer(string latest, string current)
    {
        if (Version.TryParse(latest, out var v1) && Version.TryParse(current, out var v2))
            return v1 > v2;
        return false;
    }
}

/// <summary>
/// 下载进度窗口
/// </summary>
internal class DownloadProgressForm : Form
{
    private readonly ProgressBar _bar;
    private readonly Label _label;

    public DownloadProgressForm()
    {
        Text = "共振监控 - 正在下载升级";
        Size = new Size(400, 120);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ControlBox = false;

        _bar = new ProgressBar
        {
            Location = new Point(20, 15),
            Size = new Size(340, 25),
            Minimum = 0,
            Maximum = 100
        };

        _label = new Label
        {
            Location = new Point(20, 48),
            Size = new Size(340, 20),
            Text = "正在下载...",
            TextAlign = ContentAlignment.MiddleCenter
        };

        Controls.AddRange(new Control[] { _bar, _label });
    }

    public void UpdateProgress(int percent, long downloaded, long total)
    {
        _bar.Value = Math.Min(percent, 100);
        _label.Text = $"下载中 {downloaded / 1024 / 1024}MB / {total / 1024 / 1024}MB ({percent}%)";
    }
}
