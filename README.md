# 共振监控 (StockMonitor)

Windows 悬浮小窗口实时行情监控程序，内置多周期共振趋势指标引擎，自动检测买卖信号。

## 功能

- **悬浮小窗口** — 无边框置顶窗口，显示拼音缩写 + 实时股价 + 信号(B/S)，可拖动到任意位置
- **实时行情** — 新浪行情API批量获取，收到即刷新，交易时段约1秒更新一次
- **多周期共振信号**
  - 大周期共振（日线/60分/30分 ≥2个同方向）→ 红色B / 绿色S
  - 短周期共振（5分/15分/30分/60分 ≥2个同方向）→ 淡黄色B / S
- **涨停检测** — 涨幅≥9.8%时显示红色 `---`
- **信号通知** — 大周期共振触发时弹窗 + 声音 + 托盘图标闪烁
- **自选股管理** — 拼音/代码/名称模糊搜索添加，右键移除
- **自动升级** — 启动时检查服务器版本，有新版自动下载替换重启
- **左键切换** — 点击悬浮窗切换下一只股票，15秒自动轮换
- **信号日志** — 按日期轮转，保留30天

## 截图

```
┌──────────────┐
│ SCCH  8.71 B │  ← 悬浮小窗口(可拖动, 置顶)
└──────────────┘
  拼音  股价  信号
```

## 编译运行

### 环境要求
- .NET 9 SDK
- Windows 10/11

### 编译
```bash
cd 股票监控
dotnet build StockMonitor/StockMonitor.csproj
```

### 运行
```bash
dotnet run --project StockMonitor
```

### 发布独立exe（双击即用，无需安装.NET）
```bash
dotnet publish StockMonitor/StockMonitor.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```
输出: `StockMonitor/bin/Release/net9.0-windows/win-x64/publish/StockMonitor.exe` (~108MB)

## 下载

直接下载编译好的exe（无需安装任何依赖）:

[StockMonitor.exe](http://8.147.70.248:8080/tools/StockMonitor.exe)

## 使用说明

1. 双击 `StockMonitor.exe` 启动，屏幕右侧出现悬浮小窗口
2. 右键悬浮窗 → "添加财富" → 输入拼音/代码搜索 → 双击选中
3. 左键点击悬浮窗切换下一只股票
4. 拖动悬浮窗到你喜欢的位置
5. 交易时段自动拉取K线并计算共振信号
6. 大周期共振触发时弹窗+声音+图标闪烁，双击托盘图标停止

## 架构

```
StockMonitor/
├── Api/
│   ├── EastMoneyClient.cs      东方财富API(K线数据+搜索联想)
│   ├── SinaQuoteClient.cs      新浪实时报价(批量获取)
│   └── StockQuote.cs           报价DTO
├── Monitor/
│   ├── ResonanceMonitor.cs     多周期共振监控器
│   ├── SignalLogger.cs         信号日志(按日轮转)
│   └── Models/
│       └── WatchStock.cs       自选股+共振状态模型
├── UI/
│   ├── FloatingBar.cs          悬浮小窗口
│   └── AddStockForm.cs         添加股票对话框
├── lib/
│   └── StockMonitor.Indicator.dll  共振趋势指标引擎(预编译)
├── TrayApplicationContext.cs   托盘主逻辑
├── TrayIconRenderer.cs         动态图标渲染
├── WatchlistManager.cs         自选股持久化
├── AutoUpdater.cs              自动升级
└── Program.cs                  入口点
```

## 指标引擎

共振趋势指标引擎以预编译DLL形式提供 (`lib/StockMonitor.Indicator.dll`)，包含：
- `TdxFunctions` — 通达信基础函数(SMA/EMA/HHV/LLV/REF等)
- `ResonanceIndicator` — 共振趋势指标计算(多维RSV融合 → 3K-2D平滑 → 动态参考线 → 买卖信号)
- `KlineBar` — K线数据结构
- `SignalResult` — 信号结果

如需替换为自己的指标引擎，实现相同接口即可。

## 数据源

| 用途 | API | 说明 |
|------|-----|------|
| 实时报价 | 新浪行情 | 批量获取，GBK编码，约200ms响应 |
| K线数据 | 东方财富 | 5分/15分/30分/60分/日线，前复权 |
| 搜索联想 | 东方财富 | 支持拼音/代码/汉字模糊搜索 |

## License

MIT
