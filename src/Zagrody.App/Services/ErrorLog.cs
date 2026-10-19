using System.IO;

namespace Zagrody.App.Services;

internal static class ErrorLog
{
    private const long MaxSize = 1024 * 1024;

    public static string Path => System.IO.Path.Combine(AppPaths.DataDirectory, "error.log");

    public static void Write(Exception ex, string context)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            if (File.Exists(Path) && new FileInfo(Path).Length > MaxSize)
            {
                File.Move(Path, Path + ".old", overwrite: true);
            }
            File.AppendAllText(Path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Logging must never take the app down.
        }
    }
}

internal static class AppPaths
{
    public static string DataDirectory { get; } =
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Zagrody");

    public static string ConfigFile => System.IO.Path.Combine(DataDirectory, "config.json");

    public static string Desktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
}
