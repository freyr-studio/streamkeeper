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

    // Consecutive failures per room (key = platform + username)
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> Failures = new();
    private const int FailuresBeforeUnknown = 3;

    public static async Task<RoomStatus> CheckAsync(Room room)
    {
        var key = $"{room.Platform}:{room.Username}";

        for (int attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                var status = await CheckOnceAsync(room);
                Failures.TryRemove(key, out _);
                return status;
            }
            catch (Exception ex)
            {
                if (attempt == 1) { await Task.Delay(2000); continue; } // second attempt
                Log($"{key} -> {ex.GetType().Name}: {ex.Message}");
            }
        }

        // Failed: keep the last known status, only show Unknown after several failures
        int count = Failures.AddOrUpdate(key, 1, (_, c) => c + 1);
        return count < FailuresBeforeUnknown ? room.Status : RoomStatus.Unknown;
    }

    private static async Task<RoomStatus> CheckOnceAsync(Room room) => room.Platform switch
    {
        Platform.Chaturbate => await CheckChaturbateAsync(room.Username),
        Platform.Twitch     => await CheckTwitchAsync(room.Username),
        Platform.Kick       => await CheckKickAsync(room.Username),
        _                   => RoomStatus.Unknown
    };

    // Failure log : %LOCALAPPDATA%\StreamKeeper\logs\status.log
    private static readonly object LogLock = new();
    private static void Log(string message)
    {
        try
        {
            var dir = System.IO.Path.Combine(RoomStore.AppDataFolder, "logs");
            System.IO.Directory.CreateDirectory(dir);
            lock (LogLock)
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "status.log"),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
        }
        catch { }
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
