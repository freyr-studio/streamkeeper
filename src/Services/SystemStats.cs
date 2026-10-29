using System.Diagnostics;

namespace StreamKeeper.Services;

// Mesure CPU + RAM de l'app et de ses outils (yt-dlp, ffmpeg)
public class SystemStats
{
    private Dictionary<int, TimeSpan> _lastCpu = new();
    private DateTime _lastTime = DateTime.UtcNow;

    public (double CpuPercent, double RamMb) Sample()
    {
        var now = DateTime.UtcNow;
        double elapsedMs = (now - _lastTime).TotalMilliseconds;
        _lastTime = now;

        var procs = new List<Process> { Process.GetCurrentProcess() };
        procs.AddRange(Process.GetProcessesByName("yt-dlp"));
        procs.AddRange(Process.GetProcessesByName("ffmpeg"));

        double cpuMs = 0, ramBytes = 0;
        var current = new Dictionary<int, TimeSpan>();

        foreach (var p in procs)
        {
            try
            {
                var cpuTime = p.TotalProcessorTime;
                ramBytes += p.WorkingSet64;
                if (_lastCpu.TryGetValue(p.Id, out var previous))
                    cpuMs += (cpuTime - previous).TotalMilliseconds;
                current[p.Id] = cpuTime;
            }
            catch { /* processus terminé entre-temps */ }
            finally { p.Dispose(); }
        }

        _lastCpu = current;

        double cpu = elapsedMs > 0
            ? cpuMs / (elapsedMs * Environment.ProcessorCount) * 100
            : 0;

        return (cpu, ramBytes / 1_048_576);
    }
}