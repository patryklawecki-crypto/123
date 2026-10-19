namespace Zagrody.Core.Rules;

/// <summary>A snapshot of a file or folder, decoupled from the file system so rules are easy to test.</summary>
public sealed record FileCandidate(string FullPath, bool IsDirectory, long Size, DateTime LastWriteTimeUtc, FileAttributes Attributes = FileAttributes.Normal)
{
    private static readonly string[] TransientSuffixes =
    {
        ".tmp", ".temp", ".crdownload", ".part", ".partial", ".download", ".opdownload", ".!ut",
    };

    public string Name => Path.GetFileName(FullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    public string Extension => IsDirectory ? string.Empty : Path.GetExtension(FullPath).ToLowerInvariant();

    /// <summary>
    /// Items that should never be moved automatically: system/hidden files, Office lock files
    /// and downloads that are still in progress.
    /// </summary>
    public bool IsTransientOrSystem =>
        Attributes.HasFlag(FileAttributes.Hidden)
        || Attributes.HasFlag(FileAttributes.System)
        || Attributes.HasFlag(FileAttributes.Temporary)
        || Name.StartsWith("~$", StringComparison.Ordinal)
        || Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)
        || Name.Equals("thumbs.db", StringComparison.OrdinalIgnoreCase)
        || (!IsDirectory && TransientSuffixes.Contains(Extension));

    public static FileCandidate? FromPath(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                var dir = new DirectoryInfo(path);
                return new FileCandidate(dir.FullName, true, 0, dir.LastWriteTimeUtc, dir.Attributes);
            }
            if (File.Exists(path))
            {
                var file = new FileInfo(path);
                return new FileCandidate(file.FullName, false, file.Length, file.LastWriteTimeUtc, file.Attributes);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        return null;
    }
}
