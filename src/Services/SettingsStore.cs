using System.IO;
using System.Text.Json;
using StreamKeeper.Models;

namespace StreamKeeper.Services;

public static class SettingsStore
{
    private static readonly string FilePath = Path.Combine(RoomStore.AppDataFolder, "settings.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    // Les réglages actuels, accessibles partout dans l'app
    public static AppSettings Current { get; } = Load();

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new();
        }
        catch { /* fichier abîmé : réglages par défaut */ }
        return new();
    }

    public static void Save()
    {
        Directory.CreateDirectory(RoomStore.AppDataFolder);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Current, Options));
        File.Move(tmp, FilePath, overwrite: true);
    }
}