using System.Drawing;
using System.Drawing.Text;

namespace StockMonitor;

public static class TrayIconRenderer
{
    /// <summary>
    /// 渲染16x16托盘图标: 第1行拼音缩写(2字母), 第2行价格
    /// </summary>
    public static Icon Render(string pinyin, double price, double changePercent, string signalType)
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

        // 背景色: 共振买入=绿, 共振卖出=红, 正常=深灰
        var bgColor = signalType switch
        {
            "Buy" => Color.FromArgb(0, 128, 0),
            "Sell" => Color.FromArgb(200, 0, 0),
            _ => Color.FromArgb(40, 40, 40)
        };
        g.Clear(bgColor);

        // 第1行: 拼音缩写(取前2个字母)
        var abbr = pinyin.Length >= 2 ? pinyin[..2] : pinyin;
        using var fontSmall = new Font("Consolas", 6f, FontStyle.Bold, GraphicsUnit.Pixel);
        g.DrawString(abbr, fontSmall, Brushes.White, -1, 0);

        // 第2行: 价格(尽量精简)
        var priceStr = FormatPrice(price);
        var priceColor = changePercent >= 0 ? Color.FromArgb(255, 80, 80) : Color.FromArgb(80, 255, 80);
        using var priceBrush = new SolidBrush(priceColor);
        g.DrawString(priceStr, fontSmall, priceBrush, -1, 8);

        var icon = Icon.FromHandle(bmp.GetHicon());
        return icon;
    }

    /// <summary>
    /// 格式化价格, 尽量短
    /// </summary>
    private static string FormatPrice(double price)
    {
        if (price <= 0) return "--";
        if (price >= 1000) return $"{price / 1000:F0}k";
        if (price >= 100) return $"{price:F0}";
        if (price >= 10) return $"{price:F1}";
        return $"{price:F2}";
    }

    /// <summary>
    /// 生成Tooltip详情文本(不超过128字符)
    /// </summary>
    public static string BuildTooltip(string name, string code, double price, double changePercent,
        Dictionary<string, string>? periodSignals, string signalType, int buyCount, int sellCount)
    {
        var sign = changePercent >= 0 ? "+" : "";
        var line1 = $"{name} ({code})";
        var line2 = $"现价: {price:F2} ({sign}{changePercent:F2}%)";

        if (periodSignals == null || periodSignals.Count == 0)
            return $"{line1}\n{line2}\n初始化中...";

        var signals = string.Join(" ",
            periodSignals.Select(kv => $"{kv.Key}{kv.Value}"));
        var line3 = $"共振: {signals}";

        var status = signalType switch
        {
            "Buy" => $"状态: {buyCount}/5周期买入共振",
            "Sell" => $"状态: {sellCount}/5周期卖出共振",
            _ => "状态: 正常"
        };

        var tooltip = $"{line1}\n{line2}\n{line3}\n{status}";
        return tooltip.Length > 127 ? tooltip[..127] : tooltip;
    }
}
