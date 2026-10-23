using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;

namespace XingShu.Native.Services;

public static class Json
{
    public static JsonElement Get(this JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) ? v : default;
    public static string Text(this JsonElement e, string key = "", string fallback = "")
    { var v = key.Length > 0 ? e.Get(key) : e; return v.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False ? v.ToString() : fallback; }
    public static long Number(this JsonElement e, string key = "", long fallback = 0)
    { var v = key.Length > 0 ? e.Get(key) : e; return v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : fallback; }
    public static bool Flag(this JsonElement e, string key) => e.Get(key).ValueKind == JsonValueKind.True;
    public static JsonElement[] Rows(this JsonElement e) => e.ValueKind == JsonValueKind.Array ? e.EnumerateArray().ToArray() : [];
    public static string Metric(this JsonElement e, string key) => e.Get(key).ValueKind == JsonValueKind.Number ? e.Number(key).ToString("N0") : "—";
}

public sealed class CatalogStore
{
    public JsonElement Data { get; }
    public JsonElement Guides { get; }
    private readonly Dictionary<string, WeakReference<BitmapSource>> images = new();
    public CatalogStore()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "catalog.json")));
        Data = doc.RootElement.Clone();
        var guides = Path.Combine(AppContext.BaseDirectory, "Assets", "guides.json");
        if (File.Exists(guides)) { using var g = JsonDocument.Parse(File.ReadAllText(guides)); Guides = g.RootElement.Clone(); }
    }
    public JsonElement[] Rows(string section) => Data.Get(section).Rows();
    public JsonElement Find(string section, long id) => Rows(section).FirstOrDefault(r => r.Number("id") == id);
    public string Name(string section, long id) => Find(section, id).Text("name", $"#{id}");
    public string Items(JsonElement quantities) => quantities.ValueKind == JsonValueKind.Object && quantities.EnumerateObject().Any()
        ? string.Join("、", quantities.EnumerateObject().Select(p => Data.Get("itemNames").Text(p.Name, "道具 #" + p.Name) + " × " + p.Value.Text()))
        : "无额外道具";
    public static string Plain(string text)
    {
        text = Regex.Replace(text, @"\{[A-Za-z][A-Za-z0-9_]*=([^{}]+)\}", "$1");
        text = Regex.Replace(text, @"<sprite[^>]*>", " ◆ ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<[^>]*>|\[(?:/?color(?:=[^\]]+)?|/?b|/?i|/?size(?:=[^\]]+)?)\]", "", RegexOptions.IgnoreCase);
        return text.Replace("\\n", "\n").Trim();
    }
    public BitmapSource? Image(string key, int width = 360)
    {
        var path = ResourcePath(key, true); if (path is null) return null;
        var cacheKey = $"{path}|{width}";
        if (images.TryGetValue(cacheKey, out var reference) && reference.TryGetTarget(out var cached)) return cached;
        try
        {
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = width; image.UriSource = new Uri(path); image.EndInit(); image.Freeze();
            images[cacheKey] = new WeakReference<BitmapSource>(image); return image;
        }
        catch (IOException) { return null; }
        catch (NotSupportedException) { return null; }
    }
    public string? ResourcePath(string key, bool png = false)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        var resource = Data.Get("assets").Text(key);
        if (resource.Length == 0) return null;
        var relative = resource.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        if (png && Path.GetExtension(relative).Equals(".webp", StringComparison.OrdinalIgnoreCase)) relative = Path.ChangeExtension(relative, ".png");
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Assets", relative));
        var assetRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Assets")) + Path.DirectorySeparatorChar;
        return path.StartsWith(assetRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(path) ? path : null;
    }
}
