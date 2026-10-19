namespace Zagrody.Core.Sorting;

public static class PathUtil
{
    public static string Normalize(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    public static bool AreSame(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    public static bool IsSameOrInside(string path, string folder)
    {
        var p = Normalize(path) + Path.DirectorySeparatorChar;
        var f = Normalize(folder) + Path.DirectorySeparatorChar;
        return p.StartsWith(f, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns "dir/name" or, if taken, "dir/name (2).ext", "dir/name (3).ext"... like Explorer does.
    /// </summary>
    public static string UniqueDestination(string directory, string name, Func<string, bool> isTaken)
    {
        var candidate = Path.Combine(directory, name);
        if (!isTaken(candidate))
        {
            return candidate;
        }

        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        if (stem.Length == 0)
        {
            // Names like ".gitignore" have no stem; treat the whole name as the stem.
            stem = name;
            extension = string.Empty;
        }

        for (var i = 2; ; i++)
        {
            candidate = Path.Combine(directory, $"{stem} ({i}){extension}");
            if (!isTaken(candidate))
            {
                return candidate;
            }
        }
    }

    public static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
}
