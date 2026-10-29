using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Data;
using StreamKeeper.Models;
using StreamKeeper.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace StreamKeeper.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public ObservableCollection<Room> Rooms { get; }
    public ICollectionView RoomsView { get; }

    [ObservableProperty] private string _urlInput = "";

    // Onglet sélectionné : "All", "Chaturbate", "Twitch", "Kick" ou "Other"
    [ObservableProperty] private string _platformFilter = "All";

    // Interrupteurs (mémorisés dans settings.json)
    [ObservableProperty] private bool _autoRecordEnabled;
    [ObservableProperty] private bool _shutdownWhenDone;

    // Barre d'état
    [ObservableProperty] private string _cpuText = "CPU –";
    [ObservableProperty] private string _ramText = "RAM –";
    [ObservableProperty] private string _diskText = "Disk –";
    [ObservableProperty] private string _countersText = "";

    private readonly Dictionary<Room, Recorder> _recorders = new();
    private readonly Dictionary<Room, DateTime> _offlineSince = new();
    private readonly HashSet<Room> _manuallyStopped = new();
    private readonly Dictionary<Room, int> _restartCount = new();

    private static readonly TimeSpan OfflineGrace = TimeSpan.FromSeconds(90);
    private const int MaxRestarts = 15;

    public MainViewModel()
    {
        Rooms = new ObservableCollection<Room>(RoomStore.Load());
        foreach (var room in Rooms) Track(room);

        RoomsView = CollectionViewSource.GetDefaultView(Rooms);
        RoomsView.SortDescriptions.Add(new SortDescription(nameof(Room.SortRank), ListSortDirection.Ascending));
        RoomsView.SortDescriptions.Add(new SortDescription(nameof(Room.Username), ListSortDirection.Ascending));
        if (RoomsView is ICollectionViewLiveShaping live)
        {
            live.LiveSortingProperties.Add(nameof(Room.SortRank));
            live.IsLiveSorting = true;
        }

        // Filtre par onglet
        RoomsView.Filter = item =>
            PlatformFilter == "All" || ((Room)item).Platform.ToString() == PlatformFilter;

        // Réglages mémorisés
        var s = SettingsStore.Current;
        Recorder.OutputRoot = s.OutputFolder;
        AutoRecordEnabled = s.AutoRecord;
        // ShutdownWhenDone repart volontairement sur Off à chaque lancement (sécurité)

        _ = MonitorLoopAsync();
        _ = StatsLoopAsync();
    }

    // ===== Onglets plateformes =====
    [RelayCommand]
    private void SetPlatformFilter(string platform) => PlatformFilter = platform;

    partial void OnPlatformFilterChanged(string value) => RoomsView.Refresh();

    // ===== Sauvegarde des interrupteurs =====
    partial void OnAutoRecordEnabledChanged(bool value)
    {
        SettingsStore.Current.AutoRecord = value;
        SettingsStore.Save();
        foreach (var room in Rooms) Evaluate(room);
    }

    // ===== Surveillance : vérifie toutes les rooms toutes les 30 s =====
    private async Task MonitorLoopAsync()
    {
        while (true)
        {
            using var limiter = new SemaphoreSlim(4);
            var tasks = Rooms.ToList().Select(async room =>
            {
                await limiter.WaitAsync();
                try
                {
                    room.Status = await StatusChecker.CheckAsync(room);
                    Evaluate(room);
                }
                finally { limiter.Release(); }
            });
            await Task.WhenAll(tasks);

            await Task.Delay(TimeSpan.FromSeconds(30));
        }
    }

    // ===== Barre d'état : mise à jour toutes les 2 s =====
    private async Task StatsLoopAsync()
    {
        var stats = new SystemStats();
        while (true)
        {
            var (cpu, ram) = await Task.Run(() => stats.Sample());
            CpuText = $"CPU {cpu:0.0}%";
            RamText = $"RAM {ram:0} MB";

            try
            {
                var root = Path.GetPathRoot(Path.GetFullPath(Recorder.OutputRoot))!;
                var drive = new DriveInfo(root);
                DiskText = $"Disk {drive.AvailableFreeSpace / 1_073_741_824.0:0.0} GB free";
            }
            catch { DiskText = "Disk –"; }

            int online = Rooms.Count(r => r.Status == RoomStatus.Online);
            CountersText = $"Rooms: {Rooms.Count}   ·   Online: {online}   ·   Recording: {_recorders.Count}";

            await Task.Delay(2000);
        }
    }

    // ===== Le "cerveau" : démarrer / arrêter selon le statut =====
    private void Evaluate(Room room)
    {
        if (room.Status is RoomStatus.Offline or RoomStatus.Private or RoomStatus.Away)
        {
            _manuallyStopped.Remove(room);
            _restartCount.Remove(room);
        }

        bool recording = _recorders.ContainsKey(room);

        if (recording)
        {
            switch (room.Status)
            {
                case RoomStatus.Private:
                    StopRecorder(room);
                    break;

                case RoomStatus.Offline:
                case RoomStatus.Away:
                    if (!_offlineSince.ContainsKey(room))
                        _offlineSince[room] = DateTime.Now;
                    else if (DateTime.Now - _offlineSince[room] >= OfflineGrace)
                        StopRecorder(room);
                    break;

                case RoomStatus.Online:
                    _offlineSince.Remove(room);
                    break;
            }
        }
        else if (AutoRecordEnabled
                 && room.AutoRecord
                 && room.Status == RoomStatus.Online
                 && !_manuallyStopped.Contains(room))
        {
            StartRecorder(room);
        }
    }

    // ===== Enregistrement (interne) =====
    private bool _toolsWarningShown;

    private void StartRecorder(Room room)
    {
        if (_recorders.ContainsKey(room)) return;

        if (!Recorder.ToolsAvailable(out var missing))
        {
            if (!_toolsWarningShown)
            {
                _toolsWarningShown = true;
                _ = Notify("Missing tools",
                    $"{missing} was not found in the \"tools\" folder next to StreamKeeper.exe.\n\n" +
                    "Download the full release zip from GitHub, or put yt-dlp.exe and ffmpeg.exe in that folder.");
            }
            return;
        }

        var rec = new Recorder(room);
        rec.Finished += r => _ = OnRecorderFinishedAsync(r);
        _recorders[room] = rec;

        try { rec.Start(); }
        catch
        {
            _recorders.Remove(room);
            room.IsRecording = false;
        }
    }

    private void StopRecorder(Room room)
    {
        if (_recorders.TryGetValue(room, out var rec)) rec.Stop();
    }

    // ===== Fin d'un enregistrement =====
    private async Task OnRecorderFinishedAsync(Recorder rec)
    {
        _recorders.Remove(rec.Room);
        _offlineSince.Remove(rec.Room);

        if (await TryRestartAsync(rec)) return; // relancé : pas fini

        await CheckShutdownAsync();
    }

    // Auto-restart si yt-dlp s'est arrêté tout seul. Renvoie true si relancé.
    private async Task<bool> TryRestartAsync(Recorder rec)
    {
        var room = rec.Room;

        if (rec.StopRequested) return false;
        if (!Rooms.Contains(room)) return false;

        int count = _restartCount.GetValueOrDefault(room);
        if (count >= MaxRestarts) return false;

        await Task.Delay(5000);
        room.Status = await StatusChecker.CheckAsync(room);

        if (room.Status != RoomStatus.Online) return false;
        if (_manuallyStopped.Contains(room)) return false;
        if (_recorders.ContainsKey(room)) return true; // déjà relancé ailleurs

        _restartCount[room] = count + 1;
        StartRecorder(room);
        return true;
    }

    // ===== Shutdown when done =====
    private bool _shutdownScheduled;

    private async Task CheckShutdownAsync()
    {
        if (!ShutdownWhenDone || _recorders.Count > 0 || _shutdownScheduled) return;

        _shutdownScheduled = true;
        RunHidden("shutdown", "/s /t 60 /c \"StreamKeeper: all recordings are finished.\"");

        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = "Shutdown",
            Content = "All recordings are finished.\nThe PC will shut down in 60 seconds.",
            CloseButtonText = "Cancel shutdown"
        };
        await box.ShowDialogAsync();

        // Si on arrive ici, l'utilisateur a cliqué sur "Cancel shutdown"
        RunHidden("shutdown", "/a");
        _shutdownScheduled = false;
    }

    private static void RunHidden(string file, string args)
    {
        try
        {
            Process.Start(new ProcessStartInfo(file, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch { }
    }

    // ===== Boutons Record / Stop / Stop All =====
    [RelayCommand]
    private void Record(Room room)
    {
        _manuallyStopped.Remove(room);
        _restartCount.Remove(room);
        StartRecorder(room);
    }

    [RelayCommand]
    private void Stop(Room room)
    {
        _manuallyStopped.Add(room);
        StopRecorder(room);
    }

    [RelayCommand]
    public void StopAll()
    {
        foreach (var room in _recorders.Keys.ToList())
        {
            _manuallyStopped.Add(room);
            StopRecorder(room);
        }
    }

    // ===== Ouvrir la room / le dossier =====
    [RelayCommand]
    private void OpenRoom(Room room)
    {
        try { Process.Start(new ProcessStartInfo(room.Url) { UseShellExecute = true }); }
        catch { }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(Recorder.OutputRoot);
            Process.Start("explorer.exe", Recorder.OutputRoot);
        }
        catch { }
    }

    // ===== Export / Import =====
    [RelayCommand]
    private async Task ExportList()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export room list",
            Filter = "JSON file|*.json",
            FileName = $"rooms_{DateTime.Now:yyyy-MM-dd}.json"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            RoomStore.SaveTo(dialog.FileName, Rooms);
            await Notify("Export", $"{Rooms.Count} room(s) exported.");
        }
        catch (Exception ex) { await Notify("Export failed", ex.Message); }
    }

    [RelayCommand]
    private async Task ImportList()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import room list",
            Filter = "JSON file|*.json|All files|*.*"
        };
        if (dialog.ShowDialog() != true) return;

        await ImportFromAsync(dialog.FileName, "Import");
    }

    // Ajoute les rooms d'un fichier (sans doublons)
    private async Task ImportFromAsync(string path, string title)
    {
        try
        {
            var imported = await Task.Run(() => RoomStore.Parse(File.ReadAllText(path)));
            var added = new List<Room>();

            foreach (var room in imported)
            {
                if (Rooms.Any(r => r.Platform == room.Platform && r.Username == room.Username)) continue;
                Rooms.Add(room);
                Track(room);
                added.Add(room);
            }
            Save();

            await Notify(title, $"{added.Count} new room(s) added ({imported.Count - added.Count} already in the list).");

            // Vérifie tout de suite le statut des nouvelles rooms
            foreach (var room in added)
            {
                room.Status = await StatusChecker.CheckAsync(room);
                Evaluate(room);
            }
        }
        catch (Exception ex) { await Notify($"{title} failed", ex.Message); }
    }

    // Petite boîte de message moderne (WPF-UI)
    private static async Task Notify(string title, string message)
    {
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK"
        };
        await box.ShowDialogAsync();
    }

    // ===== Gestion de la liste =====
    [RelayCommand]
    private async Task AddUrl()
    {
        var room = UrlParser.Parse(UrlInput);
        UrlInput = "";
        if (room is null) return;

        bool exists = Rooms.Any(r => r.Platform == room.Platform && r.Username == room.Username);
        if (exists) return;

        Rooms.Add(room);
        Track(room);
        Save();

        room.Status = await StatusChecker.CheckAsync(room);
        Evaluate(room);
    }

    [RelayCommand]
    private void Paste()
    {
        if (Clipboard.ContainsText())
            UrlInput = Clipboard.GetText().Trim();
    }

    [RelayCommand]
    private void RemoveRoom(Room room)
    {
        StopRecorder(room);
        Rooms.Remove(room);
        Save();
    }

    [RelayCommand]
    private void CheckAll() { foreach (var r in Rooms) r.AutoRecord = true; }

    [RelayCommand]
    private void UncheckAll() { foreach (var r in Rooms) r.AutoRecord = false; }

    // ===== Sauvegarde + réaction quand on coche une room =====
    private void Track(Room room)
    {
        room.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Room.AutoRecord))
            {
                Save();
                Evaluate(room);
            }
        };
    }

    private void Save() => RoomStore.Save(Rooms);
}
