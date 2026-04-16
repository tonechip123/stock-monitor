using System.Text.Json;
using StockMonitor.Monitor.Models;

namespace StockMonitor;

public static class WatchlistManager
{
    private static readonly string FilePath = Path.Combine(
        AppContext.BaseDirectory, "watchlist.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static List<WatchStock> Load()
    {
        if (!File.Exists(FilePath)) return new List<WatchStock>();

        try
        {
            var json = File.ReadAllText(FilePath);
            var items = JsonSerializer.Deserialize<List<WatchStockDto>>(json, JsonOptions);
            return items?.Select(dto => new WatchStock
            {
                Code = dto.Code ?? "",
                Name = dto.Name ?? "",
                PinYin = dto.PinYin ?? ""
            }).ToList() ?? new List<WatchStock>();
        }
        catch
        {
            return new List<WatchStock>();
        }
    }

    public static void Save(List<WatchStock> watchlist)
    {
        var dtos = watchlist.Select(s => new WatchStockDto
        {
            Code = s.Code,
            Name = s.Name,
            PinYin = s.PinYin
        }).ToList();

        var json = JsonSerializer.Serialize(dtos, JsonOptions);
        File.WriteAllText(FilePath, json);
    }

    private sealed class WatchStockDto
    {
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string PinYin { get; set; } = "";
    }
}
