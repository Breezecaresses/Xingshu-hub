using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XingShu.Native.Services;

public sealed record AccountBinding(string PlatformId, long PlayerId, string Role);
public sealed class Preferences
{
    public double Width { get; set; } = 1220;
    public double Height { get; set; } = 810;
    public double Scale { get; set; } = 1;
    public string Page { get; set; } = "overview";
    public string Account { get; set; } = "";
    public List<AccountBinding> Bindings { get; set; } = [];
    public List<long> Favorites { get; set; } = [];
    public bool ReducedMotion { get; set; } = true;
    public bool AutoFollow { get; set; }
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "com.xingshu.hub.native");
    [JsonIgnore] public string StorageFolder { get; set; } = Folder;
    private string FilePath => Path.Combine(StorageFolder, "settings.json");
    public static Preferences Load()
    {
        try {
            var settings = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(Path.Combine(Folder, "settings.json"))) ?? new();
            settings.Bindings ??= []; settings.Favorites ??= []; settings.Page ??= "overview"; settings.Account ??= "";
            return settings;
        }
        catch { return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(StorageFolder);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, FilePath, true);
    }
}
