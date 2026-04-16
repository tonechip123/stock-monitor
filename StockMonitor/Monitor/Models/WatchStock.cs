namespace StockMonitor.Monitor.Models;

public sealed class WatchStock
{
    public string Code { get; init; } = "";
    public string Name { get; set; } = "";
    public string PinYin { get; set; } = "";
    public double Price { get; set; }
    public double ChangePercent { get; set; }
    public ResonanceState Resonance { get; set; } = new();
}

public sealed class ResonanceState
{
    // 短周期共振: 5分/15分/30分/60分中>=2个 → 淡黄色B/S
    public int ShortBuyCount { get; set; }
    public int ShortSellCount { get; set; }
    public string ShortSignalType { get; set; } = "None"; // None, Buy, Sell

    // 大周期共振: 日线/60分/30分中>=2个 → 红色B/绿色S
    public int LongBuyCount { get; set; }
    public int LongSellCount { get; set; }
    public string LongSignalType { get; set; } = "None"; // None, Buy, Sell

    public double SignalPrice { get; set; }
    public Dictionary<string, string> PeriodSignals { get; set; } = new();
    public bool AllPeriodsReady { get; set; }
}
