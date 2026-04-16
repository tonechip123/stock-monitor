using System.Text.Json;
using StockMonitor.Indicator;

namespace StockMonitor.Api;

public static class EastMoneyClient
{
    private static readonly HttpClient Http;

    static EastMoneyClient()
    {
        var handler = new HttpClientHandler { UseProxy = false };
        Http = new HttpClient(handler);
        Http.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
    }

    /// <summary>
    /// 根据6位代码生成东方财富secid (沪市=1, 深市=0)
    /// </summary>
    public static string? GetSecId(string code)
    {
        if (string.IsNullOrEmpty(code) || code.Length != 6) return null;
        if (code.StartsWith("6")) return $"1.{code}";
        if (code.StartsWith("0") || code.StartsWith("3")) return $"0.{code}";
        return null; // 北交所暂不支持
    }

    /// <summary>
    /// 获取实时报价
    /// </summary>
    public static async Task<StockQuote?> GetQuote(string code, CancellationToken ct = default)
    {
        var secid = GetSecId(code);
        if (secid == null) return null;

        var url = $"https://push2.eastmoney.com/api/qt/stock/get?secid={secid}" +
                  "&fields=f43,f44,f45,f46,f47,f58,f170&_={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

        try
        {
            var json = await Http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);
            var data = doc.RootElement.GetProperty("data");

            // f43=现价(分), f44=最高, f45=最低, f46=开盘, f47=成交量, f58=名称, f170=涨跌幅
            var price = GetDouble(data, "f43") / 100.0;
            if (price <= 0) return null; // 停牌或异常

            return new StockQuote
            {
                Code = code,
                Name = data.TryGetProperty("f58", out var n) ? n.GetString() ?? "" : "",
                Price = price,
                High = GetDouble(data, "f44") / 100.0,
                Low = GetDouble(data, "f45") / 100.0,
                Open = GetDouble(data, "f46") / 100.0,
                Volume = GetLong(data, "f47"),
                ChangePercent = GetDouble(data, "f170") / 100.0
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 获取K线数据
    /// </summary>
    public static async Task<List<KlineBar>> GetKline(string code, int klt, int limit,
        CancellationToken ct = default)
    {
        var secid = GetSecId(code);
        if (secid == null) return new List<KlineBar>();

        var url = $"https://push2his.eastmoney.com/api/qt/stock/kline/get?secid={secid}" +
                  $"&klt={klt}&fqt=1&lmt={limit}&beg=0&end=20500101" +
                  $"&fields1=f1,f2,f3,f4,f5,f6&fields2=f51,f52,f53,f54,f55,f56" +
                  $"&_={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

        try
        {
            var json = await Http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);
            var data = doc.RootElement.GetProperty("data");

            if (!data.TryGetProperty("klines", out var klines))
                return new List<KlineBar>();

            var result = new List<KlineBar>();
            foreach (var line in klines.EnumerateArray())
            {
                var parts = line.GetString()?.Split(',');
                if (parts == null || parts.Length < 6) continue;

                result.Add(new KlineBar
                {
                    Date = parts[0],
                    Open = double.TryParse(parts[1], out var o) ? o : 0,
                    Close = double.TryParse(parts[2], out var c) ? c : 0,
                    High = double.TryParse(parts[3], out var h) ? h : 0,
                    Low = double.TryParse(parts[4], out var l) ? l : 0,
                    Volume = long.TryParse(parts[5], out var v) ? v : 0
                });
            }
            return result;
        }
        catch
        {
            return new List<KlineBar>();
        }
    }

    /// <summary>
    /// 搜索联想API (支持拼音/代码/汉字模糊搜索)
    /// </summary>
    public static async Task<List<SearchResult>> Search(string input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input)) return new List<SearchResult>();

        var url = $"https://searchapi.eastmoney.com/api/suggest/get" +
                  $"?input={Uri.EscapeDataString(input)}&type=14&count=10" +
                  $"&token=D43BF722C8E33BDC906FB84D85E326E8";

        try
        {
            var json = await Http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("QuotationCodeTable", out var table))
                return new List<SearchResult>();
            if (!table.TryGetProperty("Data", out var data))
                return new List<SearchResult>();

            var result = new List<SearchResult>();
            foreach (var item in data.EnumerateArray())
            {
                var code = item.TryGetProperty("Code", out var c) ? c.GetString() ?? "" : "";
                if (GetSecId(code) == null) continue; // 跳过北交所等不支持的

                result.Add(new SearchResult
                {
                    Code = code,
                    Name = item.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "",
                    PinYin = item.TryGetProperty("PinYin", out var p) ? p.GetString() ?? "" : ""
                });
            }
            return result;
        }
        catch
        {
            return new List<SearchResult>();
        }
    }

    private static double GetDouble(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v)) return 0;
        return v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
    }

    private static long GetLong(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v)) return 0;
        return v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;
    }
}

public sealed class SearchResult
{
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string PinYin { get; init; } = "";
}
