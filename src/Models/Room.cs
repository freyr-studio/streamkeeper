using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StreamKeeper.Models;

public enum Platform { Chaturbate, Twitch, Kick, Other }

public enum RoomStatus { Unknown, Offline, Online, Private, Away }

public partial class Room : ObservableObject
{
    // Sauvegardé dans la liste
    public string Username { get; set; } = "";
    public Platform Platform { get; set; }
    public string Url { get; set; } = "";

    [ObservableProperty] private bool _autoRecord;

    // En direct uniquement (pas sauvegardé)
    [ObservableProperty][property: JsonIgnore]
    [NotifyPropertyChangedFor(nameof(SortRank))]
    private RoomStatus _status = RoomStatus.Unknown;

    [ObservableProperty][property: JsonIgnore]
    [NotifyPropertyChangedFor(nameof(SortRank))]
    private bool _isRecording;

    [ObservableProperty][property: JsonIgnore] private string _speed = "–";
    [ObservableProperty][property: JsonIgnore] private string _duration = "–";

    // Rang de tri : plus petit = plus haut dans la liste
    [JsonIgnore]
    public int SortRank => IsRecording ? 0 : Status switch
    {
        RoomStatus.Online  => 1,
        RoomStatus.Private => 2,
        RoomStatus.Away    => 3,
        RoomStatus.Offline => 4,
        _                  => 5
    };
}