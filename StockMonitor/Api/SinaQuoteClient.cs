using System.Text;
using System.Text.RegularExpressions;

namespace StockMonitor.Api;

/// <summary>
/// 新浪实时行情客户端 — 移植自easyquotation
/// 支持批量获取, 一次请求所有自选股报价
/// API: http://hq.sinajs.cn/rn={timestamp}&list=sh600839,sz002050,...
/// </summary>
public static class SinaQuoteClient
{
    private static readonly HttpClient Http;

    // 解析新浪行情文本: var hq_str_sh600839="四川长虹,8.69,8.75,8.71,..."
    private static readonly Regex QuotePattern = new(
        @"hq_str_(\w{2}\d{6})=""([^""]*)"";",
        RegexOptions.Compiled);

    static SinaQuoteClient()
    {
        // 注册GBK编码支持(.NET 9默认不包含)
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var handler = new HttpClientHandler { UseProxy = false };
        Http = new HttpClient(handler);
        Http.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36");
        Http.DefaultRequestHeaders.Add("Referer", "http://finance.sina.com.cn/");
    }

    /// <summary>
    /// 6位代码转新浪前缀格式: 600839→sh600839, 002050→sz002050
    /// </summary>
    public static string? GetSinaCode(string code)
    {
        if (string.IsNullOrEmpty(code) || code.Length != 6) return null;
        if (code.StartsWith("6")) return $"sh{code}";
        if (code.StartsWith("0") || code.StartsWith("3")) return $"sz{code}";
        return null;
    }

    /// <summary>
    /// 批量获取实时报价(一次HTTP请求所有股票)
    /// </summary>
    public static async Task<Dictionary<string, StockQuote>> GetQuotes(
        IEnumerable<string> codes, CancellationToken ct = default)
    {
        var result = new Dictionary<string, StockQuote>();

        var sinaCodes = new List<string>();
        var codeMap = new Dictionary<string, string>(); // sinaCode → 6位code
        foreach (var code in codes)
        {
            var sina = GetSinaCode(code);
            if (sina != null)
            {
                sinaCodes.Add(sina);
                codeMap[sina] = code;
            }
        }

        if (sinaCodes.Count == 0) return result;

        var list = string.Join(",", sinaCodes);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var url = $"http://hq.sinajs.cn/rn={ts}&list={list}";

        try
        {
            // 新浪返回GBK编码
            var bytes = await Http.GetByteArrayAsync(url, ct);
            var text = Encoding.GetEncoding("GBK").GetString(bytes);

            foreach (Match m in QuotePattern.Matches(text))
            {
                var sinaCode = m.Groups[1].Value;
                var data = m.Groups[2].Value;

                if (string.IsNullOrEmpty(data)) continue;
                if (!codeMap.TryGetValue(sinaCode, out var code)) continue;

                var fields = data.Split(',');
                if (fields.Length < 32) continue;

                // 新浪行情字段顺序(与easyquotation/sina.py一致):
                // 0=名称 1=开盘 2=昨收 3=现价 4=最高 5=最低
                // 6=竞买价 7=竞卖价 8=成交量(股) 9=成交额
                var name = fields[0];
                var now = ParseDouble(fields[3]);
                var close = ParseDouble(fields[2]);

                // 非交易时段/集合竞价: 现价为0, 用昨收价代替
                if (now <= 0) now = close;
                if (now <= 0) continue; // 真正停牌(昨收也为0)

                var changePercent = close > 0 ? (now - close) / close * 100 : 0;

                result[code] = new StockQuote
                {
                    Code = code,
                    Name = name,
                    Price = now,
                    Open = ParseDouble(fields[1]),
                    High = ParseDouble(fields[4]),
                    Low = ParseDouble(fields[5]),
                    Volume = ParseLong(fields[8]),
                    ChangePercent = changePercent
                };
            }
        }
        catch
        {
            // 网络异常, 返回已解析的部分
        }

        return result;
    }

    /// <summary>
    /// 获取单只股票报价(内部调用批量接口)
    /// </summary>
    public static async Task<StockQuote?> GetQuote(string code, CancellationToken ct = default)
    {
        var quotes = await GetQuotes(new[] { code }, ct);
        return quotes.TryGetValue(code, out var q) ? q : null;
    }

    private static double ParseDouble(string s)
        => double.TryParse(s, out var v) ? v : 0;

    private static long ParseLong(string s)
        => long.TryParse(s, out var v) ? v : 0;
}
