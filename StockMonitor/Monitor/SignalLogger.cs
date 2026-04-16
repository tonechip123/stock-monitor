using StockMonitor.Monitor.Models;

namespace StockMonitor.Monitor;

/// <summary>
/// 信号日志 — 按日期轮转, 保留30天
/// </summary>
public sealed class SignalLogger
{
    private readonly string _logDir;

    public SignalLogger()
    {
        _logDir = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(_logDir);
        CleanOldLogs();
    }

    public void Log(WatchStock stock, ResonanceState state)
    {
        var fileName = $"monitor_{DateTime.Now:yyyyMMdd}.log";
        var filePath = Path.Combine(_logDir, fileName);
        var signals = string.Join(" ",
            state.PeriodSignals.Select(kv => $"{kv.Key}{kv.Value}"));

        var line = $"[{DateTime.Now:HH:mm:ss}] {stock.Name}({stock.Code}) " +
                   $"价格:{stock.Price:F2} 大周期:{state.LongSignalType} 短周期:{state.ShortSignalType} " +
                   $"周期:{signals} 大买:{state.LongBuyCount} 大卖:{state.LongSellCount} " +
                   $"短买:{state.ShortBuyCount} 短卖:{state.ShortSellCount}\n";

        try
        {
            File.AppendAllText(filePath, line);
        }
        catch
        {
            // 日志写入失败不影响主程序
        }
    }

    private void CleanOldLogs()
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-30);
            foreach (var file in Directory.GetFiles(_logDir, "monitor_*.log"))
            {
                var fi = new FileInfo(file);
                if (fi.CreationTime < cutoff)
                    fi.Delete();
            }
        }
        catch { }
    }
}
