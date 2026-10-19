using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.VisualBasic.FileIO;
using Zagrody.Core.Sorting;

namespace Zagrody.App.Services;

/// <summary>File operations that go through the Windows shell, so users get the familiar progress and conflict dialogs.</summary>
internal static class ShellOps
{
    public static void Open(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            ShowError($"Nie udało się otworzyć:\n{path}\n\n{ex.Message}");
        }
    }

    public static void ShowInExplorer(string path)
    {
        try
        {
            var args = File.Exists(path) || Directory.Exists(path)
                ? $"/select,\"{path}\""
                : $"\"{Path.GetDirectoryName(path)}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    /// <summary>Moves or copies items into a folder with the shell's dialogs. Returns what was transferred where.</summary>
    public static List<(string From, string To)> TransferInto(string folder, IEnumerable<string> paths, bool copy)
    {
        var done = new List<(string From, string To)>();
        foreach (var source in paths)
        {
            var destination = Path.Combine(folder, Path.GetFileName(source.TrimEnd('\\', '/')));
            if (PathUtil.AreSame(source, destination))
            {
                continue;
            }
            if (Directory.Exists(source) && PathUtil.IsSameOrInside(folder, source))
            {
                ShowError($"Nie można przenieść folderu do jego własnego podfolderu:\n{source}");
                continue;
            }
            try
            {
                var isDirectory = Directory.Exists(source);
                switch ((isDirectory, copy))
                {
                    case (true, true): FileSystem.CopyDirectory(source, destination, UIOption.AllDialogs); break;
                    case (true, false): FileSystem.MoveDirectory(source, destination, UIOption.AllDialogs); break;
                    case (false, true): FileSystem.CopyFile(source, destination, UIOption.AllDialogs); break;
                    default: FileSystem.MoveFile(source, destination, UIOption.AllDialogs); break;
                }
                done.Add((source, destination));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ShowError($"Nie udało się {(copy ? "skopiować" : "przenieść")}:\n{source}\n\n{ex.Message}");
            }
        }
        return done;
    }

    public static void Recycle(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                }
                else if (File.Exists(path))
                {
                    FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ShowError(ex.Message);
            }
        }
    }

    public static bool IsSameVolume(string a, string b) =>
        string.Equals(Path.GetPathRoot(Path.GetFullPath(a)), Path.GetPathRoot(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    public static void ShowError(string message) =>
        MessageBox.Show(message, "Zagrody", MessageBoxButton.OK, MessageBoxImage.Warning);
}
