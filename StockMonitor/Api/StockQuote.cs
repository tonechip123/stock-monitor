namespace StockMonitor.Api;

public sealed class StockQuote
{
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public double Price { get; init; }
    public double ChangePercent { get; init; }
    public double Open { get; init; }
    public double High { get; init; }
    public double Low { get; init; }
    public long Volume { get; init; }
}
