using StockMonitor.Api;
using StockMonitor.Indicator;
using StockMonitor.Monitor.Models;

namespace StockMonitor.Monitor;

/// <summary>
/// 多周期共振监控器 — 为每只股票维护5个周期的K线缓存并计算指标
/// </summary>
public class ResonanceMonitor
{
    // 周期定义: klt参数, periodCode, K线请求数量, 刷新间隔(秒)
    private static readonly PeriodConfig[] Periods = new[]
    {
        new PeriodConfig("5分",  5,   1, 800, 60),
        new PeriodConfig("15分", 15,  2, 800, 60),
        new PeriodConfig("30分", 30,  3, 400, 300),
        new PeriodConfig("60分", 60,  4, 250, 300),
        new PeriodConfig("日线", 101, 5, 150, 1800),
    };

    private readonly Dictionary<string, StockCache> _cache = new();

    /// <summary>
    /// 首次初始化: 拉取所有K线并计算指标
    /// </summary>
    public async Task Initialize(List<WatchStock> watchlist, CancellationToken ct)
    {
        foreach (var stock in watchlist)
        {
            if (ct.IsCancellationRequested) break;

            var cache = GetOrCreateCache(stock.Code);
            foreach (var period in Periods)
            {
                if (ct.IsCancellationRequested) break;

                var bars = await EastMoneyClient.GetKline(stock.Code, period.Klt, period.Limit, ct);
                if (bars.Count > 0)
                {
                    cache.KlineData[period.Name] = bars.ToArray();
                    cache.ReadyPeriods.Add(period.Name);

                    var signal = ResonanceIndicator.Compute(bars.ToArray(), period.PeriodCode);
                    cache.Signals[period.Name] = signal;
                }

                await Task.Delay(200, ct); // API限流
            }

            UpdateStockResonance(stock, cache);
        }
    }

    /// <summary>
    /// 增量刷新: 按各周期的刷新间隔拉取最新K线
    /// </summary>
    public async Task Refresh(List<WatchStock> watchlist, CancellationToken ct)
    {
        var now = DateTime.Now;

        foreach (var stock in watchlist)
        {
            if (ct.IsCancellationRequested) break;

            var cache = GetOrCreateCache(stock.Code);
            bool updated = false;

            foreach (var period in Periods)
            {
                if (ct.IsCancellationRequested) break;

                // 按刷新间隔判断是否需要刷新
                if (cache.LastRefresh.TryGetValue(period.Name, out var lastTime)
                    && (now - lastTime).TotalSeconds < period.RefreshIntervalSec)
                    continue;

                var bars = await EastMoneyClient.GetKline(stock.Code, period.Klt, period.Limit, ct);
                if (bars.Count > 0)
                {
                    cache.KlineData[period.Name] = bars.ToArray();
                    cache.ReadyPeriods.Add(period.Name);

                    var signal = ResonanceIndicator.Compute(bars.ToArray(), period.PeriodCode);
                    cache.Signals[period.Name] = signal;
                    cache.LastRefresh[period.Name] = now;
                    updated = true;
                }

                await Task.Delay(200, ct);
            }

            if (updated)
                UpdateStockResonance(stock, cache);
        }
    }

    private static void UpdateStockResonance(WatchStock stock, StockCache cache)
    {
        var r = stock.Resonance;
        r.AllPeriodsReady = cache.ReadyPeriods.Count >= Periods.Length;
        r.PeriodSignals.Clear();

        // 大周期: 日线/60分/30分
        var longPeriods = new[] { "日线", "60分", "30分" };
        // 短周期: 5分/15分/30分/60分
        var shortPeriods = new[] { "5分", "15分", "30分", "60分" };

        int longBuy = 0, longSell = 0;
        int shortBuy = 0, shortSell = 0;

        foreach (var period in Periods)
        {
            if (cache.Signals.TryGetValue(period.Name, out var signal))
            {
                var mark = signal.Signal switch
                {
                    SignalType.Buy => "B",
                    SignalType.Sell => "S",
                    _ => "-"
                };
                r.PeriodSignals[period.Name] = mark;

                if (longPeriods.Contains(period.Name))
                {
                    if (signal.Signal == SignalType.Buy) longBuy++;
                    if (signal.Signal == SignalType.Sell) longSell++;
                }
                if (shortPeriods.Contains(period.Name))
                {
                    if (signal.Signal == SignalType.Buy) shortBuy++;
                    if (signal.Signal == SignalType.Sell) shortSell++;
                }
            }
            else
            {
                r.PeriodSignals[period.Name] = "?";
            }
        }

        r.LongBuyCount = longBuy;
        r.LongSellCount = longSell;
        r.ShortBuyCount = shortBuy;
        r.ShortSellCount = shortSell;

        // 大周期共振: 日线/60分/30分中>=2个同方向
        if (r.AllPeriodsReady)
        {
            if (longBuy >= 2)
            {
                r.LongSignalType = "Buy";
                r.SignalPrice = stock.Price;
            }
            else if (longSell >= 2)
            {
                r.LongSignalType = "Sell";
                r.SignalPrice = stock.Price;
            }
            else
            {
                r.LongSignalType = "None";
            }

            // 短周期共振: 5分/15分/30分/60分中>=2个同方向
            if (shortBuy >= 2)
                r.ShortSignalType = "Buy";
            else if (shortSell >= 2)
                r.ShortSignalType = "Sell";
            else
                r.ShortSignalType = "None";
        }
    }

    private StockCache GetOrCreateCache(string code)
    {
        if (!_cache.TryGetValue(code, out var cache))
        {
            cache = new StockCache();
            _cache[code] = cache;
        }
        return cache;
    }

    private sealed class StockCache
    {
        public Dictionary<string, KlineBar[]> KlineData { get; } = new();
        public Dictionary<string, SignalResult> Signals { get; } = new();
        public Dictionary<string, DateTime> LastRefresh { get; } = new();
        public HashSet<string> ReadyPeriods { get; } = new();
    }

    private sealed record PeriodConfig(string Name, int Klt, int PeriodCode, int Limit, int RefreshIntervalSec);
}
