using StreamKeeper.Models;

namespace StreamKeeper.Services;

public static class UrlParser
{
    // Transforme une URL collée en Room (ou null si l'URL est invalide)
    public static Room? Parse(string input)
    {
        input = input.Trim();
        if (string.IsNullOrEmpty(input)) return null;

        // Ajoute https:// si oublié
        if (!input.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            input = "https://" + input;

        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri)) return null;

        var host = uri.Host.ToLowerInvariant();
        var username = uri.AbsolutePath.Trim('/').Split('/')[0].ToLowerInvariant();
        if (string.IsNullOrEmpty(username)) return null;

        // Détection de la plateforme
        var platform = host switch
        {
            _ when host.EndsWith("chaturbate.com") => Platform.Chaturbate,
            _ when host.EndsWith("twitch.tv")      => Platform.Twitch,
            _ when host.EndsWith("kick.com")       => Platform.Kick,
            _                                      => Platform.Other
        };

        // URL propre et uniforme
        var url = platform switch
        {
            Platform.Chaturbate => $"https://chaturbate.com/{username}/",
            Platform.Twitch     => $"https://www.twitch.tv/{username}",
            Platform.Kick       => $"https://kick.com/{username}",
            _                   => uri.ToString()
        };

        return new Room { Username = username, Platform = platform, Url = url };
    }
}