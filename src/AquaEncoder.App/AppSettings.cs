using System.Text.Json;

namespace AquaEncoder.App;

/// <summary>Son kullanılan ayarlar. %AppData%/AquaEncoder/settings.json (Linux: ~/.config/AquaEncoder).</summary>
public sealed class AppSettings
{
    public bool DarkTheme { get; set; } = true;

    public int ProfileIndex { get; set; } = 1;
    public int CodecIndex { get; set; } = 0;
    public string? Backend { get; set; }
    public int HeightIndex { get; set; } = 0;
    public int SpeedIndex { get; set; } = 1;
    public int Quality { get; set; } = 23;
    public bool LowEnd { get; set; } = false;
    public int Threads { get; set; } = 0;

    public int AudioKbps { get; set; } = 160;
    public int AudioStreamIndex { get; set; } = 0;

    public string? IntroPath { get; set; }
    public string? FontsDir { get; set; }
    public string? OutputDir { get; set; }
    public string Suffix { get; set; } = "_hardsub";
    public bool Overwrite { get; set; } = false;

    public int PreviewStart { get; set; } = 60;
    public int PreviewLength { get; set; } = 10;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AquaEncoder", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { /* bozuk dosya: varsayılanlarla devam */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* yazılamadıysa sessizce geç */ }
    }
}
