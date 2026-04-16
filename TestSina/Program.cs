using System.Net.Http;
using System.Text;

var handler = new HttpClientHandler { UseProxy = false };
var http = new HttpClient(handler);
http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");
http.DefaultRequestHeaders.Add("Referer", "http://finance.sina.com.cn/");
var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
var url = $"http://hq.sinajs.cn/rn={ts}&list=sh600839,sz002050";
Console.WriteLine($"URL: {url}");
try {
    var bytes = await http.GetByteArrayAsync(url);
    var text = Encoding.GetEncoding("GBK").GetString(bytes);
    Console.WriteLine($"OK, length={text.Length}");
    Console.WriteLine(text.Substring(0, Math.Min(200, text.Length)));
} catch (Exception ex) {
    Console.WriteLine($"ERROR: {ex.GetType().Name}: {ex.Message}");
    if (ex.InnerException != null)
        Console.WriteLine($"INNER: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
}
