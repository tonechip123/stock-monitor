using StockMonitor.Api;
using StockMonitor.Monitor;
using StockMonitor.Monitor.Models;

namespace StockMonitor;

public class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly ContextMenuStrip _contextMenu;
    private readonly UI.FloatingBar _floatingBar;
    private readonly System.Windows.Forms.Timer _rotationTimer;
    private readonly System.Windows.Forms.Timer _klineTimer;
    private readonly CancellationTokenSource _cts = new();
    private readonly ResonanceMonitor _monitor = new();
    private readonly SignalLogger _logger = new();

    private List<WatchStock> _watchlist = new();
    private int _currentIndex;
    private readonly Dictionary<string, DateTime> _lastNotifyTime = new();

    // 图标闪烁
    private readonly System.Windows.Forms.Timer _blinkTimer;
    private bool _blinkOn;
    private bool _isBlinking;
    private Icon? _normalIcon;
    private Icon? _blinkIcon;

    public TrayApplicationContext()
    {
        _contextMenu = new ContextMenuStrip();
        _trayIcon = new NotifyIcon
        {
            Visible = true,
            ContextMenuStrip = _contextMenu,
            Text = "共振监控 - 初始化中..."
        };

        // 双击托盘图标: 显示/隐藏状态窗口
        _trayIcon.DoubleClick += (_, _) => { StopBlinking(); _floatingBar.ToggleVisibility(); };

        // 悬浮窗
        _floatingBar = new UI.FloatingBar();
        _floatingBar.SetExternalMenu(_contextMenu);
        _floatingBar.NextStockRequested += (_, _) =>
        {
            if (_watchlist.Count <= 1) return;
            _currentIndex = (_currentIndex + 1) % _watchlist.Count;
            UpdateDisplay();
        };
        _floatingBar.Show();

        // 初始图标
        UpdateDisplay();

        // 定时器: 轮换股票 15秒
        _rotationTimer = new System.Windows.Forms.Timer { Interval = 15000 };
        _rotationTimer.Tick += (_, _) =>
        {
            if (_watchlist.Count <= 1) return;
            _currentIndex = (_currentIndex + 1) % _watchlist.Count;
            UpdateDisplay();
        };

        // 定时器: K线刷新+指标计算 60秒
        _klineTimer = new System.Windows.Forms.Timer { Interval = 60000 };
        _klineTimer.Tick += OnKlineTimerTick;

        // 定时器: 图标闪烁 (500ms交替)
        _blinkTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _blinkTimer.Tick += (_, _) =>
        {
            _blinkOn = !_blinkOn;
            _trayIcon.Icon = _blinkOn ? _normalIcon : _blinkIcon;
        };

        // 准备闪烁用的空白图标
        var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp)) g.Clear(Color.Transparent);
        _blinkIcon = Icon.FromHandle(bmp.GetHicon());

        // 加载自选股并启动
        LoadWatchlistAndStart();
    }

    private async void LoadWatchlistAndStart()
    {
        _watchlist = WatchlistManager.Load();
        RebuildContextMenu();

        if (_watchlist.Count == 0)
        {
            _trayIcon.Text = "共振监控 - 请添加财富";
            UpdateDisplay();
        }
        else
        {
            // 首次批量拉取所有股票价格(新浪, 一次请求)
            await RefreshAllPrices();
            UpdateDisplay();

            // 启动K线刷新(异步, 不阻塞)
            _ = Task.Run(() => InitialKlineRefresh(_cts.Token));
        }

        // 启动实时报价后台轮询(新浪批量, 收到即刷新)
        _ = Task.Run(() => PricePollingLoop(_cts.Token));

        _rotationTimer.Start();
        _klineTimer.Start();
    }

    /// <summary>
    /// 实时报价后台轮询 — 新浪批量API, 收到即刷新UI
    /// 交易时段: 每次请求完立刻下一次(约200-500ms一轮)
    /// 非交易时段: 每30秒拉一次
    /// </summary>
    private async Task PricePollingLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_watchlist.Count > 0)
                {
                    // 新浪批量接口: 一次请求所有股票
                    var codes = _watchlist.Select(s => s.Code).ToList();
                    var quotes = await SinaQuoteClient.GetQuotes(codes, ct);

                    if (quotes.Count > 0)
                    {
                        // 更新所有股票价格
                        foreach (var stock in _watchlist)
                        {
                            if (quotes.TryGetValue(stock.Code, out var q))
                            {
                                stock.Price = q.Price;
                                stock.ChangePercent = q.ChangePercent;
                                if (string.IsNullOrEmpty(stock.Name) && !string.IsNullOrEmpty(q.Name))
                                    stock.Name = q.Name;
                            }
                        }

                        // 回到UI线程刷新显示
                        try
                        {
                            _floatingBar.Invoke(() => UpdateDisplay());
                        }
                        catch { }
                    }
                }

                // 交易时段: 最短间隔1秒; 非交易时段: 30秒
                var delay = IsTradeTime() ? 1000 : 30000;
                await Task.Delay(delay, ct);
            }
            catch (OperationCanceledException) { break; }
            catch
            {
                // 网络异常, 等3秒重试
                try { await Task.Delay(3000, ct); } catch { break; }
            }
        }
    }

    private async Task InitialKlineRefresh(CancellationToken ct)
    {
        try
        {
            await _monitor.Initialize(_watchlist, ct);
            if (!ct.IsCancellationRequested)
                _trayIcon.Text = "共振监控 - 就绪";
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    private async void OnKlineTimerTick(object? sender, EventArgs e)
    {
        if (_watchlist.Count == 0 || !IsTradeTime()) return;

        _klineTimer.Stop();
        try
        {
            var prevStates = _watchlist.ToDictionary(s => s.Code, s => s.Resonance.LongSignalType);

            await Task.Run(() => _monitor.Refresh(_watchlist, _cts.Token));
            UpdateDisplay();
            RebuildContextMenu();

            foreach (var stock in _watchlist)
            {
                var prev = prevStates.GetValueOrDefault(stock.Code, "None");
                var curr = stock.Resonance.LongSignalType;
                if (curr != "None" && curr != prev)
                    ShowSignalNotification(stock);
            }
        }
        catch (OperationCanceledException) { }
        catch { }
        finally
        {
            _klineTimer.Start();
        }
    }

    /// <summary>
    /// 批量刷新所有股票价格(新浪一次请求)
    /// </summary>
    private async Task RefreshAllPrices()
    {
        if (_watchlist.Count == 0) return;
        var codes = _watchlist.Select(s => s.Code).ToList();
        var quotes = await SinaQuoteClient.GetQuotes(codes, _cts.Token);
        foreach (var stock in _watchlist)
        {
            if (quotes.TryGetValue(stock.Code, out var q))
            {
                stock.Price = q.Price;
                stock.ChangePercent = q.ChangePercent;
                if (string.IsNullOrEmpty(stock.Name) && !string.IsNullOrEmpty(q.Name))
                    stock.Name = q.Name;
            }
        }
    }

    private void UpdateDisplay()
    {
        if (_watchlist.Count == 0)
        {
            _trayIcon.Icon?.Dispose();
            _trayIcon.Icon = TrayIconRenderer.Render("--", 0, 0, "None");
            _trayIcon.Text = "共振监控 - 请添加财富";
            _floatingBar.UpdateNoStock();
            return;
        }

        var stock = _watchlist[_currentIndex];
        var r = stock.Resonance;

        // 托盘图标: 大周期信号优先
        var iconSignal = r.LongSignalType != "None" ? r.LongSignalType : r.ShortSignalType;
        _trayIcon.Icon?.Dispose();
        _trayIcon.Icon = TrayIconRenderer.Render(
            stock.PinYin, stock.Price, stock.ChangePercent, iconSignal);

        _trayIcon.Text = TrayIconRenderer.BuildTooltip(
            stock.Name, stock.Code, stock.Price, stock.ChangePercent,
            r.PeriodSignals, iconSignal,
            r.LongBuyCount + r.ShortBuyCount, r.LongSellCount + r.ShortSellCount);

        _floatingBar.SetSignalType(iconSignal);
        _floatingBar.UpdateDisplay(
            stock.PinYin, stock.Name, stock.Price, stock.ChangePercent,
            r.LongSignalType, r.ShortSignalType);
    }

    public void RebuildContextMenu()
    {
        _contextMenu.Items.Clear();

        foreach (var stock in _watchlist)
        {
            var s = stock;
            var text = $"{s.Name} ({s.Code})  {s.Price:F2}";
            var item = new ToolStripMenuItem(text);
            var removeItem = new ToolStripMenuItem("移除");
            removeItem.Click += (_, _) =>
            {
                _watchlist.Remove(s);
                WatchlistManager.Save(_watchlist);
                if (_currentIndex >= _watchlist.Count)
                    _currentIndex = Math.Max(0, _watchlist.Count - 1);
                RebuildContextMenu();
                UpdateDisplay();
            };
            item.DropDownItems.Add(removeItem);
            _contextMenu.Items.Add(item);
        }

        _contextMenu.Items.Add(new ToolStripSeparator());

        var addItem = new ToolStripMenuItem("添加财富...");
        addItem.Click += OnAddStockClick;
        _contextMenu.Items.Add(addItem);

        _contextMenu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (_, _) =>
        {
            _cts.Cancel();
            _trayIcon.Visible = false;
            Application.Exit();
        };
        _contextMenu.Items.Add(exitItem);
    }

    private async void OnAddStockClick(object? sender, EventArgs e)
    {
        using var form = new UI.AddStockForm();
        if (form.ShowDialog() == DialogResult.OK && form.SelectedStock != null)
        {
            var selected = form.SelectedStock;
            if (_watchlist.Any(s => s.Code == selected.Code)) return;

            var newStock = new WatchStock
            {
                Code = selected.Code,
                Name = selected.Name,
                PinYin = selected.PinYin
            };
            _watchlist.Add(newStock);
            WatchlistManager.Save(_watchlist);
            _currentIndex = _watchlist.Count - 1;
            RebuildContextMenu();

            // 立即拉取价格(新浪批量)
            await RefreshAllPrices();
            UpdateDisplay();
        }
    }

    private void StartBlinking()
    {
        if (_isBlinking) return;
        _isBlinking = true;
        _normalIcon = _trayIcon.Icon;
        _blinkTimer.Start();
    }

    private void StopBlinking()
    {
        if (!_isBlinking) return;
        _isBlinking = false;
        _blinkTimer.Stop();
        if (_normalIcon != null)
            _trayIcon.Icon = _normalIcon;
    }

    private void ShowSignalNotification(WatchStock stock)
    {
        var r = stock.Resonance;
        var key = $"{stock.Code}_{r.LongSignalType}";

        if (_lastNotifyTime.TryGetValue(key, out var lastTime)
            && (DateTime.Now - lastTime).TotalMinutes < 10)
            return;

        _lastNotifyTime[key] = DateTime.Now;

        var direction = r.LongSignalType == "Buy" ? "买入" : "卖出";
        var title = $"大周期共振{direction}信号";
        var text = $"{stock.Name} ({stock.Code}) {stock.Price:F2}\n" +
                   $"日线/60分/30分 {r.LongBuyCount + r.LongSellCount}/3周期共振{direction}";

        _trayIcon.ShowBalloonTip(5000, title, text,
            r.LongSignalType == "Buy" ? ToolTipIcon.Info : ToolTipIcon.Warning);

        System.Media.SystemSounds.Exclamation.Play();
        StartBlinking();

        _logger.Log(stock, r);
    }

    private static bool IsTradeTime()
    {
        var now = DateTime.Now;
        if (now.DayOfWeek == DayOfWeek.Saturday || now.DayOfWeek == DayOfWeek.Sunday)
            return false;
        var time = now.TimeOfDay;
        return time >= new TimeSpan(9, 15, 0) && time <= new TimeSpan(15, 5, 0);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _rotationTimer.Stop();
            _klineTimer.Stop();
            _blinkTimer.Stop();
            _cts.Cancel();
            _floatingBar.Close();
            _floatingBar.Dispose();
            _trayIcon.Dispose();
            _contextMenu.Dispose();
        }
        base.Dispose(disposing);
    }
}
