using System.IO;
using System.Windows.Threading;
using Zagrody.Core.Models;

namespace Zagrody.App.Services;

/// <summary>
/// Watches folders (the desktop by default) and hands new items to the sorter once they have been
/// quiet for a while, so half-downloaded or still-being-written files are left alone.
/// </summary>
internal sealed class AutoSortService : IDisposable
{
    private const int MaxLockedRetries = 20;

    private readonly Dispatcher _dispatcher;
    private readonly Action<IReadOnlyList<string>> _sort;
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly Dictionary<string, Pending> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer _timer;
    private TimeSpan _quietPeriod = TimeSpan.FromSeconds(5);

    private sealed class Pending
    {
        public DateTime LastSeenUtc;
        public int Attempts;
    }

    public AutoSortService(Dispatcher dispatcher, Action<IReadOnlyList<string>> sort)
    {
        _dispatcher = dispatcher;
        _sort = sort;
        _timer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public static IReadOnlyList<string> ResolveFolders(AppSettings settings) =>
        settings.WatchedFolders.Count > 0 ? settings.WatchedFolders : new[] { AppPaths.Desktop };

    public void Restart(AppSettings settings)
    {
        StopWatchers();
        if (!settings.AutoSortEnabled)
        {
            return;
        }

        _quietPeriod = TimeSpan.FromSeconds(settings.AutoSortDelaySeconds);
        foreach (var folder in ResolveFolders(settings).Where(Directory.Exists))
        {
            var watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            watcher.Created += (_, e) => Enqueue(e.FullPath);
            watcher.Changed += (_, e) => Enqueue(e.FullPath);
            watcher.Renamed += (_, e) => Enqueue(e.FullPath); // browsers rename "x.crdownload" to "x" when done
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }
        _timer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    private void Enqueue(string path)
    {
        lock (_pending)
        {
            if (!_pending.TryGetValue(path, out var entry))
            {
                _pending[path] = entry = new Pending();
            }
            entry.LastSeenUtc = DateTime.UtcNow;
        }
    }

    private void Flush()
    {
        var ready = new List<string>();
        var now = DateTime.UtcNow;
        lock (_pending)
        {
            foreach (var (path, entry) in _pending.ToList())
            {
                if (now - entry.LastSeenUtc < _quietPeriod)
                {
                    continue;
                }
                if (!PathExists(path))
                {
                    _pending.Remove(path);
                }
                else if (IsLocked(path))
                {
                    // Still being written by someone; check again after another quiet period.
                    entry.LastSeenUtc = now;
                    if (++entry.Attempts > MaxLockedRetries)
                    {
                        _pending.Remove(path);
                    }
                }
                else
                {
                    _pending.Remove(path);
                    ready.Add(path);
                }
            }
        }

        if (ready.Count > 0)
        {
            _dispatcher.BeginInvoke(() => _sort(ready));
        }
    }

    private static bool PathExists(string path) => File.Exists(path) || Directory.Exists(path);

    private static bool IsLocked(string path)
    {
        if (Directory.Exists(path))
        {
            return false;
        }
        try
        {
            using var _ = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            // Read-only or protected file: not locked, the move itself will report if it can't proceed.
            return false;
        }
    }

    private void StopWatchers()
    {
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        foreach (var watcher in _watchers)
        {
            watcher.Dispose();
        }
        _watchers.Clear();
        lock (_pending)
        {
            _pending.Clear();
        }
    }

    public void Dispose()
    {
        StopWatchers();
        _timer.Dispose();
    }
}
