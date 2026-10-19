using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Zagrody.Core.Models;
using Zagrody.Core.Search;

namespace Zagrody.App.ViewModels;

/// <summary>State of one fence. Every change is written straight into <see cref="Config"/> and reported via <see cref="Changed"/>.</summary>
public sealed class FenceViewModel : ObservableObject, IDisposable
{
    public static readonly string[] Palette =
    {
        "#3B82F6", "#8B5CF6", "#EC4899", "#EF4444", "#F59E0B", "#10B981", "#06B6D4", "#64748B",
    };

    private readonly DispatcherTimer _reloadDebounce;
    private FileSystemWatcher? _watcher;
    private string _searchText = string.Empty;
    private bool _isSearchVisible;
    private bool _isRenaming;
    private bool _isDropTarget;

    public FenceViewModel(FenceConfig config)
    {
        Config = config;
        View = CollectionViewSource.GetDefaultView(Items);
        View.Filter = o => o is ItemViewModel item && FuzzyMatcher.Score(_searchText, item.DisplayName) is not null;
        _reloadDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _reloadDebounce.Tick += (_, _) =>
        {
            _reloadDebounce.Stop();
            Reload();
        };
        StartWatching();
        Reload();
    }

    /// <summary>Raised whenever something that has to be saved changed.</summary>
    public event EventHandler? Changed;

    public FenceConfig Config { get; }
    public Guid Id => Config.Id;
    public ObservableCollection<ItemViewModel> Items { get; } = new();
    public ICollectionView View { get; }

    public string Title
    {
        get => Config.Title;
        set
        {
            var title = string.IsNullOrWhiteSpace(value) ? Config.Title : value.Trim();
            if (title != Config.Title)
            {
                Config.Title = title;
                OnPropertyChanged();
                RaiseChanged();
            }
        }
    }

    public FenceMode Mode => Config.Mode;
    public bool IsFolder => Config.Mode == FenceMode.Folder;
    public string? FolderPath => Config.FolderPath;
    public bool FolderExists => IsFolder && Directory.Exists(Config.FolderPath);

    public bool IsRolledUp
    {
        get => Config.IsRolledUp;
        set => SetConfig(Config.IsRolledUp, value, v => Config.IsRolledUp = v);
    }

    public bool IsLocked
    {
        get => Config.IsLocked;
        set => SetConfig(Config.IsLocked, value, v => Config.IsLocked = v);
    }

    public int IconSize
    {
        get => Config.IconSize;
        set
        {
            if (SetConfig(Config.IconSize, Math.Clamp(value, FenceConfig.MinIconSize, FenceConfig.MaxIconSize), v => Config.IconSize = v))
            {
                OnPropertyChanged(nameof(TileWidth));
                Reload();
            }
        }
    }

    /// <summary>Width of one icon tile; labels get room for roughly a dozen characters.</summary>
    public double TileWidth => Math.Max(IconSize + 28, 76);

    public ItemSortMode SortMode
    {
        get => Config.SortMode;
        set
        {
            if (value == ItemSortMode.Manual && IsFolder)
            {
                return;
            }
            if (SetConfig(Config.SortMode, value, v => Config.SortMode = v))
            {
                Reload();
            }
        }
    }

    public bool SortDescending
    {
        get => Config.SortDescending;
        set
        {
            if (SetConfig(Config.SortDescending, value, v => Config.SortDescending = v))
            {
                Reload();
            }
        }
    }

    public bool ShowHiddenFiles
    {
        get => Config.ShowHiddenFiles;
        set
        {
            if (SetConfig(Config.ShowHiddenFiles, value, v => Config.ShowHiddenFiles = v))
            {
                Reload();
            }
        }
    }

    public string AccentColor
    {
        get => Config.AccentColor;
        set
        {
            if (ColorParser.IsValidHex(value) && SetConfig(Config.AccentColor, value, v => Config.AccentColor = v))
            {
                OnPropertyChanged(nameof(AccentBrush));
                OnPropertyChanged(nameof(BackgroundBrush));
            }
        }
    }

    public double BackgroundOpacity
    {
        get => Config.BackgroundOpacity;
        set
        {
            if (SetConfig(Config.BackgroundOpacity, Math.Clamp(value, 0, 1), v => Config.BackgroundOpacity = v))
            {
                OnPropertyChanged(nameof(BackgroundBrush));
            }
        }
    }

    public Brush AccentBrush => Freeze(new SolidColorBrush(ParseColor(Config.AccentColor)));

    /// <summary>A dark tint of the accent color, so white labels stay readable on any wallpaper.</summary>
    public Brush BackgroundBrush
    {
        get
        {
            var accent = ParseColor(Config.AccentColor);
            var alpha = (byte)Math.Round(Config.BackgroundOpacity * 255);
            var tint = Color.FromArgb(alpha, (byte)(18 + accent.R * 0.18), (byte)(18 + accent.G * 0.18), (byte)(22 + accent.B * 0.18));
            return Freeze(new SolidColorBrush(tint));
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value ?? string.Empty))
            {
                View.Refresh();
                OnPropertyChanged(nameof(EmptyText));
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }

    public bool IsSearchVisible
    {
        get => _isSearchVisible;
        set
        {
            if (SetField(ref _isSearchVisible, value) && !value)
            {
                SearchText = string.Empty;
            }
        }
    }

    public bool IsRenaming
    {
        get => _isRenaming;
        set => SetField(ref _isRenaming, value);
    }

    public bool IsDropTarget
    {
        get => _isDropTarget;
        set => SetField(ref _isDropTarget, value);
    }

    public bool IsEmpty => View.IsEmpty;

    public string EmptyText
    {
        get
        {
            if (_searchText.Length > 0)
            {
                return "Nic nie pasuje do wyszukiwania";
            }
            if (IsFolder)
            {
                return FolderExists ? "Folder jest pusty — przeciągnij tu pliki" : $"Nie znaleziono folderu:\n{Config.FolderPath}";
            }
            return "Przeciągnij tu pliki, foldery lub skróty";
        }
    }

    public void Reload()
    {
        var size = IconSize;
        List<ItemViewModel> items;
        if (IsFolder)
        {
            items = new();
            if (FolderExists)
            {
                try
                {
                    foreach (var info in new DirectoryInfo(Config.FolderPath!).EnumerateFileSystemInfos())
                    {
                        if (!Config.ShowHiddenFiles && (info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
                        {
                            continue;
                        }
                        items.Add(ItemViewModel.FromInfo(info, size));
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Show what we got; the empty text explains a missing folder.
                }
            }
        }
        else
        {
            items = Config.Items.Select(p => ItemViewModel.FromPath(p, size)).ToList();
        }

        var sorted = ItemSorter.Sort(items, Config.SortMode, Config.SortDescending);
        Items.Clear();
        foreach (var item in sorted)
        {
            Items.Add(item);
        }
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
    }

    /// <summary>Cheap check used by a periodic timer so pinned items that vanished get dimmed.</summary>
    public void RefreshMissingState()
    {
        if (!IsFolder && Items.Any(i => i.IsMissing == PathExists(i.Path)))
        {
            Reload();
        }
    }

    /// <summary>Pins paths to a collection fence, optionally before the item at <paramref name="index"/>.</summary>
    public void AddPaths(IEnumerable<string> paths, int? index = null)
    {
        if (IsFolder)
        {
            return;
        }
        var incoming = paths.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (incoming.Count == 0)
        {
            return;
        }

        // Re-dropping pinned items moves them, which is how manual reordering works. The drop index
        // refers to the list before removal, so shift it by the moved items that were in front of it.
        var isIncoming = (string p) => incoming.Contains(p, StringComparer.OrdinalIgnoreCase);
        var target = index ?? Config.Items.Count;
        target -= Config.Items.Take(Math.Clamp(target, 0, Config.Items.Count)).Count(isIncoming);
        Config.Items.RemoveAll(p => isIncoming(p));
        var at = Math.Clamp(target, 0, Config.Items.Count);
        Config.Items.InsertRange(at, incoming);
        Reload();
        RaiseChanged();
    }

    public void RemovePaths(IEnumerable<string> paths)
    {
        var set = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        if (!IsFolder && Config.Items.RemoveAll(set.Contains) > 0)
        {
            Reload();
            RaiseChanged();
        }
    }

    public void RemoveMissing() => RemovePaths(Config.Items.Where(p => !PathExists(p)).ToList());

    /// <summary>
    /// Switches to manual order and keeps what the user currently sees, so a drag to reorder
    /// starts from the familiar arrangement.
    /// </summary>
    public void EnsureManualOrder()
    {
        if (IsFolder || Config.SortMode == ItemSortMode.Manual)
        {
            return;
        }
        Config.Items = Items.Select(i => i.Path).ToList();
        Config.SortMode = ItemSortMode.Manual;
        Config.SortDescending = false;
        OnPropertyChanged(nameof(SortMode));
        OnPropertyChanged(nameof(SortDescending));
        RaiseChanged();
    }

    /// <summary>Index in <see cref="FenceConfig.Items"/> of a displayed item, used as a drop position.</summary>
    public int IndexOfPath(string path) => Config.Items.FindIndex(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));

    public void SetFolder(string folder)
    {
        Config.Mode = FenceMode.Folder;
        Config.FolderPath = folder;
        if (Config.SortMode == ItemSortMode.Manual)
        {
            Config.SortMode = ItemSortMode.Name;
        }
        OnPropertyChanged(nameof(Mode));
        OnPropertyChanged(nameof(IsFolder));
        OnPropertyChanged(nameof(FolderPath));
        OnPropertyChanged(nameof(SortMode));
        StartWatching();
        Reload();
        RaiseChanged();
    }

    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private bool SetConfig<T>(T current, T value, Action<T> apply, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(current, value))
        {
            return false;
        }
        apply(value);
        OnPropertyChanged(name);
        RaiseChanged();
        return true;
    }

    private void StartWatching()
    {
        _watcher?.Dispose();
        _watcher = null;
        if (!FolderExists)
        {
            return;
        }

        try
        {
            _watcher = new FileSystemWatcher(Config.FolderPath!)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Attributes | NotifyFilters.LastWrite,
            };
            FileSystemEventHandler onChange = (_, _) => ScheduleReload();
            _watcher.Created += onChange;
            _watcher.Deleted += onChange;
            _watcher.Changed += onChange;
            _watcher.Renamed += (_, _) => ScheduleReload();
            _watcher.Error += (_, _) => ScheduleReload();
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            _watcher = null;
        }
    }

    private void ScheduleReload() =>
        _reloadDebounce.Dispatcher.BeginInvoke(() =>
        {
            _reloadDebounce.Stop();
            _reloadDebounce.Start();
        });

    private static bool PathExists(string path) => File.Exists(path) || Directory.Exists(path);

    private static Color ParseColor(string hex) =>
        ColorParser.TryParse(hex, out var a, out var r, out var g, out var b) ? Color.FromArgb(a, r, g, b) : Colors.SteelBlue;

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    public void Dispose()
    {
        _reloadDebounce.Stop();
        _watcher?.Dispose();
    }
}
