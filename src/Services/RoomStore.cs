using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using StreamKeeper.Models;

namespace StreamKeeper.Services;

public static class RoomStore
{
    // Dossier %LOCALAPPDATA%\StreamKeeper
    public static readonly string AppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StreamKeeper");

    private static readonly string FilePath = Path.Combine(AppDataFolder, "rooms.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    // ===== Lire la liste au démarrage =====
    public static List<Room> Load()
    {
        if (!File.Exists(FilePath)) return new();
        try
        {
            return Parse(File.ReadAllText(FilePath));
        }
        catch
        {
            // Fichier abîmé : on le garde de côté au lieu de l'écraser
            File.Copy(FilePath, FilePath + ".bak", overwrite: true);
            return new();
        }
    }

    // ===== Écrire la liste =====
    public static void Save(IEnumerable<Room> rooms) => SaveTo(FilePath, rooms);

    // Écriture sécurisée (fichier temporaire puis renommage) vers n'importe quel chemin
    public static void SaveTo(string path, IEnumerable<Room> rooms)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(rooms, Options);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }

    // ===== Lecture tolérante =====
    // Accepte : notre format, une liste d'URL, ou l'export d'une autre app
    public static List<Room> Parse(string json)
    {
        var result = new List<Room>();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Accepte aussi la forme { "rooms": [ ... ] }
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Array) { root = prop.Value; break; }
            }
        }
        if (root.ValueKind != JsonValueKind.Array) return result;

        foreach (var item in root.EnumerateArray())
        {
            Room? room = null;

            if (item.ValueKind == JsonValueKind.String)
            {
                // Simple liste d'URL
                room = UrlParser.Parse(item.GetString() ?? "");
            }
            else if (item.ValueKind == JsonValueKind.Object)
            {
                string? url = GetString(item, "url", "link", "href");
                string? user = GetString(item, "username", "user", "name", "room", "channel");
                string? platform = GetString(item, "platform", "site", "source");

                if (!string.IsNullOrWhiteSpace(url))
                {
                    room = UrlParser.Parse(url);
                }
                else if (!string.IsNullOrWhiteSpace(user))
                {
                    var host = (platform ?? "chaturbate").ToLowerInvariant() switch
                    {
                        "twitch" => "twitch.tv",
                        "kick" => "kick.com",
                        _ => "chaturbate.com"
                    };
                    room = UrlParser.Parse($"{host}/{user}");
                }

                if (room != null && GetBool(item, "autoRecord", "auto_record", "auto", "checked", "selected") is bool auto)
                    room.AutoRecord = auto;
            }

            if (room != null && !result.Any(r => r.Platform == room.Platform && r.Username == room.Username))
                result.Add(room);
        }

        return result;
    }

    // Cherche un champ texte, quelle que soit la casse de son nom
    private static string? GetString(JsonElement obj, params string[] names)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            if (names.Any(n => string.Equals(n, prop.Name, StringComparison.OrdinalIgnoreCase))
                && prop.Value.ValueKind == JsonValueKind.String)
                return prop.Value.GetString();
        }
        return null;
    }

    // Cherche un champ vrai/faux, quelle que soit la casse de son nom
    private static bool? GetBool(JsonElement obj, params string[] names)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            if (names.Any(n => string.Equals(n, prop.Name, StringComparison.OrdinalIgnoreCase))
                && prop.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                return prop.Value.GetBoolean();
        }
        return null;
    }
}
