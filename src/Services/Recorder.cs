using System.Diagnostics;
using System.IO;
using StreamKeeper.Models;

namespace StreamKeeper.Services;

public class Recorder
{
    private static readonly string ToolsDir = Path.Combine(AppContext.BaseDirectory, "tools");

    // Dossier de sortie (réglable plus tard dans Settings)
    public static string OutputRoot { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "StreamKeeper");

    // Checks that yt-dlp.exe and ffmpeg.exe are next to the app
    public static bool ToolsAvailable(out string missing)
    {
        foreach (var exe in new[] { "yt-dlp.exe", "ffmpeg.exe" })
        {
            if (!File.Exists(Path.Combine(ToolsDir, exe))) { missing = exe; return false; }
        }
        missing = "";
        return true;
    }

    public Room Room { get; }

    // true si l'arrêt a été demandé (par toi ou par l'app)
    public bool StopRequested { get; private set; }

    private Process? _process;
    private string _folder = "";
    private string _recPattern = "";
    private string _finalPath = "";

    public event Action<Recorder>? Finished;

    public Recorder(Room room) => Room = room;

    private string? FindRecFile() =>
        Directory.Exists(_folder) ? Directory.GetFiles(_folder, _recPattern).FirstOrDefault() : null;

    // ===== Démarrer =====
    public void Start()
    {
        _folder = Path.Combine(OutputRoot, Room.Platform.ToString());
        Directory.CreateDirectory(_folder);

        var name = $"{Room.Username}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";
        _recPattern = name + ".rec.*";
        _finalPath = Path.Combine(_folder, name + ".mp4");

        var logDir = Path.Combine(RoomStore.AppDataFolder, "logs");
        Directory.CreateDirectory(logDir);
        var log = new StreamWriter(Path.Combine(logDir, name + ".log")) { AutoFlush = true };

        var psi = new ProcessStartInfo
        {
            FileName = Path.Combine(ToolsDir, "yt-dlp.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in new[] { "--no-part", "--hls-use-mpegts",
                                    "--ffmpeg-location", ToolsDir,
                                    "-o", Path.Combine(_folder, name + ".rec.%(ext)s"),
                                    Room.Url })
            psi.ArgumentList.Add(arg);

        _process = new Process { StartInfo = psi };
        _process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (log) log.WriteLine(e.Data); };
        _process.ErrorDataReceived  += (_, e) => { if (e.Data != null) lock (log) log.WriteLine(e.Data); };

        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        Room.IsRecording = true;
        _ = MonitorAsync(log);
    }

    // ===== Arrêter (yt-dlp ET ffmpeg) =====
    public void Stop()
    {
        StopRequested = true;
        try
        {
            if (_process is { HasExited: false })
                _process.Kill(entireProcessTree: true);
        }
        catch { }
    }

    // ===== Suivi : vitesse + durée chaque seconde =====
    private async Task MonitorAsync(StreamWriter log)
    {
        var start = DateTime.Now;
        long lastSize = 0;

        while (!_process!.HasExited)
        {
            await Task.Delay(1000);
            var file = FindRecFile();
            long size = file != null ? new FileInfo(file).Length : 0;
            Room.Speed = $"{(size - lastSize) / 1_048_576.0:0.00} MB/s";
            Room.Duration = (DateTime.Now - start).ToString(@"hh\:mm\:ss");
            lastSize = size;
        }

        await _process.WaitForExitAsync();
        log.Dispose();

        Room.IsRecording = false;
        Room.Speed = "–";
        Room.Duration = "–";

        await RemuxAsync();
        Finished?.Invoke(this);
    }

    // ===== Conversion fichier temporaire → vrai .mp4 =====
    private async Task RemuxAsync()
    {
        try
        {
            var recFile = FindRecFile();
            if (recFile == null) return;
            if (new FileInfo(recFile).Length == 0) { File.Delete(recFile); return; }

            var psi = new ProcessStartInfo
            {
                FileName = Path.Combine(ToolsDir, "ffmpeg.exe"),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var arg in new[] { "-y", "-i", recFile, "-c", "copy",
                                        "-movflags", "+faststart", _finalPath })
                psi.ArgumentList.Add(arg);

            using var p = Process.Start(psi)!;
            var outTask = p.StandardOutput.ReadToEndAsync();
            var errTask = p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            await Task.WhenAll(outTask, errTask);

            if (p.ExitCode == 0 && File.Exists(_finalPath) && new FileInfo(_finalPath).Length > 0)
                File.Delete(recFile);
        }
        catch { }
    }
}