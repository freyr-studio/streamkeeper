using System.IO;

namespace StreamKeeper.Models;

public class AppSettings
{
    public string OutputFolder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "StreamKeeper");

    public string Theme { get; set; } = "Midnight";

    public bool AutoRecord { get; set; }
    public bool ShutdownWhenDone { get; set; }
}
