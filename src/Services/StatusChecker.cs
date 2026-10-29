using System.Net.Http;
using System.Text;
using System.Text.Json;
using StreamKeeper.Models;

namespace StreamKeeper.Services;

public static class StatusChecker
{
    // Un seul client HTTP réutilisé (bonne pratique, évite les fuites)
    private static readonly HttpClient Http = CreateClient();

    // Identifiant public utilisé par le site twitch.tv lui-même
    private const string TwitchClientId = "kimne78kx3ncx6brgo4mv6wki5h1ko";

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return http;
    }

    public static async Task<RoomStatus> CheckAsync(Room room)
    {
        try
        {
            return room.Platform switch
            {
                Platform.Chaturbate => await CheckChaturbateAsync(room.Username),
                Platform.Twitch     => await CheckTwitchAsync(room.Username),
                Platform.Kick       => await CheckKickAsync(room.Username),
                _                   => RoomStatus.Unknown
            };
        }
        catch
        {
            return RoomStatus.Unknown; // erreur réseau, room introuvable, blocage…
        }
    }

    // ===== Chaturbate =====
    private static async Task<RoomStatus> CheckChaturbateAsync(string username)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://chaturbate.com/api/chatvideocontext/{username}/");
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");

        using var response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var status = doc.RootElement.GetProperty("room_status").GetString();

        return status switch
        {
            "public" => RoomStatus.Online,
            "private" or "hidden" or "group" or "password protected" => RoomStatus.Private,
            "away" => RoomStatus.Away,
            _ => RoomStatus.Offline
        };
    }

    // ===== Twitch =====
    private static async Task<RoomStatus> CheckTwitchAsync(string login)
    {
        var body = JsonSerializer.Serialize(new
        {
            query = "query($login: String!) { user(login: $login) { stream { id } } }",
            variables = new { login }
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://gql.twitch.tv/gql")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Client-Id", TwitchClientId);

        using var response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var user = doc.RootElement.GetProperty("data").GetProperty("user");

        if (user.ValueKind == JsonValueKind.Null) return RoomStatus.Unknown; // chaîne inexistante
        return user.GetProperty("stream").ValueKind == JsonValueKind.Null
            ? RoomStatus.Offline
            : RoomStatus.Online;
    }

    // ===== Kick =====
    private static async Task<RoomStatus> CheckKickAsync(string username)
    {
        var json = await Http.GetStringAsync($"https://kick.com/api/v2/channels/{username}");
        using var doc = JsonDocument.Parse(json);

        return doc.RootElement.TryGetProperty("livestream", out var live)
               && live.ValueKind != JsonValueKind.Null
            ? RoomStatus.Online
            : RoomStatus.Offline;
    }
}
