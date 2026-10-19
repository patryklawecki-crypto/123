using System.IO;
using System.Windows;
using System.Windows.Threading;
using Zagrody.App.Interop;
using Zagrody.App.Services;
using Zagrody.App.ViewModels;
using Zagrody.App.Views;
using Zagrody.Core.Layout;
using Zagrody.Core.Models;
using Zagrody.Core.Rules;
using Zagrody.Core.Sorting;
using Zagrody.Core.Storage;

namespace Zagrody.App;

/// <summary>Owns all fences and their windows, persists the config and runs app-wide features.</summary>
internal sealed class FenceManager : IDisposable
{
    private readonly ConfigStore _store;
    private readonly List<(FenceViewModel Vm, FenceWindow Window)> _fences = new();
    private readonly DispatcherTimer _saveDebounce;
    private readonly DispatcherTimer _missingCheck;
    private readonly FileOrganizer _organizer = new();
    private readonly AutoSortService _autoSort;
    private readonly MessageWindow _messages;
    private readonly DesktopDoubleClickWatcher _doubleClick;
    private readonly ShowDesktopWatcher _showDesktop;
    private SearchWindow? _search;
    private SettingsWindow? _settingsWindow;
    private bool _fencesVisible = true;
    private bool _iconsHiddenByToggle;
    private bool _saveErrorReported;

    public FenceManager(ConfigStore store, AppConfig config)
    {
        _store = store;
        Config = config;
        var dispatcher = Dispatcher.CurrentDispatcher;

        _saveDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _saveDebounce.Tick += (_, _) => SaveNow();
        _missingCheck = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _missingCheck.Tick += (_, _) =>
        {
            foreach (var (vm, _) in _fences)
            {
                vm.RefreshMissingState();
            }
        };

        _autoSort = new AutoSortService(dispatcher, paths => SortPaths(paths, automatic: true));
        _messages = new MessageWindow();
        _messages.ExplorerRestarted += (_, _) => OnExplorerRestarted();
        _doubleClick = new DesktopDoubleClickWatcher(dispatcher);
        _doubleClick.DoubleClicked += (_, _) => ToggleVisibility();
        _showDesktop = new ShowDesktopWatcher();
        _showDesktop.DesktopShownChanged += (_, shown) => DesktopPinning.SetRaised(_fences.Select(f => (Window)f.Window), shown);
    }

    /// <summary>Short user-facing messages (shown as tray balloons).</summary>
    public event EventHandler<string>? Notification;

    public AppConfig Config { get; }
    public IEnumerable<FenceViewModel> Fences => _fences.Select(f => f.Vm);
    public bool FencesVisible => _fencesVisible;
    public bool CanUndoSort => _organizer.CanUndo;

    public bool AllLocked
    {
        get => _fences.Count > 0 && _fences.All(f => f.Vm.IsLocked);
        set
        {
            foreach (var (vm, _) in _fences)
            {
                vm.IsLocked = value;
            }
        }
    }

    public void Start()
    {
        if (Config.Fences.Count == 0)
        {
            CreateWelcomeFence();
        }
        foreach (var fence in Config.Fences)
        {
            EnsureOnScreen(fence);
            Open(fence);
        }
        ApplySettings(Config.Settings, rules: null);
        _missingCheck.Start();
    }

    public FenceViewModel? Find(Guid id) => _fences.FirstOrDefault(f => f.Vm.Id == id).Vm;

    // ---- Fence lifecycle -----------------------------------------------------------------------

    public FenceViewModel CreateFence(FenceMode mode, string? folder)
    {
        var area = PrimaryWorkArea();
        var occupied = _fences.Select(f => new Bounds(f.Window.Left, f.Window.Top, f.Window.ActualWidth, f.Window.ActualHeight)).ToList();
        var spot = LayoutMath.FindFreeSpot(area, 340, 240, occupied);
        var config = new FenceConfig
        {
            X = spot.X,
            Y = spot.Y,
            Width = spot.Width,
            Height = spot.Height,
            AccentColor = FenceViewModel.Palette[Config.Fences.Count % FenceViewModel.Palette.Length],
        };
        if (mode == FenceMode.Folder && folder is not null)
        {
            config.Mode = FenceMode.Folder;
            config.FolderPath = folder;
            config.SortMode = ItemSortMode.Name;
            config.Title = Path.GetFileName(folder.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : folder;
        }

        Config.Fences.Add(config);
        var (vm, window) = Open(config);
        SetVisible(true);
        if (mode == FenceMode.Collection)
        {
            window.BeginRename();
        }
        else
        {
            window.Flash();
        }
        RequestSave();
        return vm;
    }

    public void DeleteFence(FenceViewModel vm)
    {
        var entry = _fences.FirstOrDefault(f => f.Vm == vm);
        if (entry.Window is null)
        {
            return;
        }

        var details = vm.IsFolder
            ? "Pliki w folderze pozostaną na dysku."
            : "Przypięte pliki pozostaną na swoich miejscach.";
        var rules = Config.Rules.Count(r => r.TargetFenceId == vm.Id);
        if (rules > 0)
        {
            details += $"\nUsunięte zostaną też reguły sortowania kierujące do tej zagrody ({rules}).";
        }
        if (MessageBox.Show($"Usunąć zagrodę „{vm.Title}”?\n\n{details}", "Zagrody", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        _fences.Remove(entry);
        entry.Window.CloseForGood();
        vm.Dispose();
        Config.Fences.Remove(vm.Config);
        Config.Rules.RemoveAll(r => r.TargetFenceId == vm.Id);
        RequestSave();
    }

    private (FenceViewModel, FenceWindow) Open(FenceConfig config)
    {
        var vm = new FenceViewModel(config);
        vm.Changed += (_, _) => RequestSave();
        var window = new FenceWindow(vm, this);
        _fences.Add((vm, window));
        if (_fencesVisible)
        {
            window.Show();
        }
        return (vm, window);
    }

    private void CreateWelcomeFence()
    {
        var area = PrimaryWorkArea();
        Config.Fences.Add(new FenceConfig
        {
            Title = "Moje skróty",
            X = area.X + 24,
            Y = area.Y + 24,
            Width = 360,
            Height = 240,
        });
        RequestSave();
    }

    // ---- Visibility ----------------------------------------------------------------------------

    /// <summary>Hides or shows all fences together with the desktop icons, like Stardock Fences' double-click.</summary>
    public void ToggleVisibility() => SetVisible(!_fencesVisible);

    public void SetVisible(bool visible)
    {
        if (visible == _fencesVisible)
        {
            return;
        }
        _fencesVisible = visible;
        if (!visible)
        {
            _iconsHiddenByToggle = DesktopIcons.IsVisible;
            if (_iconsHiddenByToggle)
            {
                DesktopIcons.SetVisible(false);
            }
        }
        else if (_iconsHiddenByToggle)
        {
            DesktopIcons.SetVisible(true);
            _iconsHiddenByToggle = false;
        }

        foreach (var (_, window) in _fences)
        {
            if (visible)
            {
                DesktopPinning.Show(window);
            }
            else
            {
                DesktopPinning.Hide(window);
            }
        }
    }

    public void ShowSearch()
    {
        _search ??= new SearchWindow(this);
        _search.ShowAndFocus();
    }

    public void Reveal(FenceViewModel fence, ItemViewModel? item)
    {
        SetVisible(true);
        _fences.FirstOrDefault(f => f.Vm == fence).Window?.Reveal(item);
    }

    public void ShowSettings()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(this);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    // ---- Drag and drop -------------------------------------------------------------------------

    public void HandleDrop(FenceViewModel target, string[] paths, Guid? sourceId, DragDropEffects effect, int? index)
    {
        var source = sourceId is { } id ? Find(id) : null;

        if (source == target)
        {
            target.AddPaths(paths, index); // reorder
            return;
        }

        if (!target.IsFolder)
        {
            target.AddPaths(paths, index);
            if (source is { IsFolder: false } && effect == DragDropEffects.Move)
            {
                source.RemovePaths(paths);
            }
            return;
        }

        var copy = effect == DragDropEffects.Copy;
        var moves = ShellOps.TransferInto(target.FolderPath!, paths, copy);
        if (!copy)
        {
            if (source is { IsFolder: false })
            {
                source.RemovePaths(moves.Select(m => m.From));
            }
            RetargetReferences(moves);
        }
        target.Reload();
        source?.Reload();
    }

    /// <summary>Pinned references follow files that the app moved, so they don't turn into "missing" items.</summary>
    private void RetargetReferences(IEnumerable<(string From, string To)> moves)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (from, to) in moves)
        {
            map[from] = to;
        }
        if (map.Count == 0)
        {
            return;
        }
        foreach (var (vm, _) in _fences.Where(f => !f.Vm.IsFolder))
        {
            var changed = false;
            for (var i = 0; i < vm.Config.Items.Count; i++)
            {
                if (map.TryGetValue(vm.Config.Items[i], out var to))
                {
                    vm.Config.Items[i] = to;
                    changed = true;
                }
            }
            if (changed)
            {
                vm.Reload();
                vm.RaiseChanged();
            }
        }
    }

    // ---- Sorting rules -------------------------------------------------------------------------

    /// <summary>Dry run with not-yet-saved rules and folders, for the settings window.</summary>
    public IReadOnlyList<SortAction> PreviewSort(IEnumerable<SortRule> rules, AppSettings settings)
    {
        var preview = new AppConfig { Fences = Config.Fences, Rules = rules.ToList(), Settings = settings };
        return SortPlanner.Plan(ScanWatchedFolders(settings), new RuleEngine(preview.Rules), preview);
    }

    public void SortNow()
    {
        if (!Config.Rules.Any(r => r.Enabled))
        {
            Notification?.Invoke(this, "Nie ma włączonych reguł sortowania. Dodaj je w Ustawieniach.");
            return;
        }
        SortPaths(ScanWatchedFolders(Config.Settings).Select(c => c.FullPath).ToList(), automatic: false);
    }

    public void UndoSort()
    {
        var report = _organizer.UndoLast();
        RetargetReferences(report.Moved.Select(m => (m.SourcePath, m.DestinationPath)));
        ReloadFolderFences();
        var message = $"Cofnięto: {report.Moved.Count} el.";
        if (report.Failures.Count > 0)
        {
            message += $", nie udało się: {report.Failures.Count}";
        }
        Notification?.Invoke(this, message);
    }

    private void SortPaths(IReadOnlyList<string> paths, bool automatic)
    {
        var candidates = paths.Select(FileCandidate.FromPath).OfType<FileCandidate>().ToList();
        var plan = SortPlanner.Plan(candidates, new RuleEngine(Config.Rules), Config);
        if (plan.Count == 0)
        {
            if (!automatic)
            {
                Notification?.Invoke(this, "Wszystko jest już na swoim miejscu.");
            }
            return;
        }

        var report = _organizer.Execute(plan, pin => Find(pin.FenceId)?.AddPaths(new[] { pin.SourcePath }));
        RetargetReferences(report.Moved.Select(m => (m.SourcePath, m.DestinationPath)));
        ReloadFolderFences();

        var message = $"Posortowano: {report.Total} el.";
        if (report.Failures.Count > 0)
        {
            message += $", pominięto: {report.Failures.Count}";
            foreach (var failure in report.Failures)
            {
                ErrorLog.Write(new IOException(failure.Message), $"Sortowanie: {failure.Path}");
            }
        }
        if (report.Moved.Count > 0)
        {
            message += ". Cofniesz to z menu w zasobniku.";
        }
        Notification?.Invoke(this, message);
    }

    private static List<FileCandidate> ScanWatchedFolders(AppSettings settings)
    {
        var result = new List<FileCandidate>();
        foreach (var folder in AutoSortService.ResolveFolders(settings).Where(Directory.Exists))
        {
            try
            {
                result.AddRange(Directory.EnumerateFileSystemEntries(folder).Select(FileCandidate.FromPath).OfType<FileCandidate>());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ErrorLog.Write(ex, $"Skanowanie {folder}");
            }
        }
        return result;
    }

    private void ReloadFolderFences()
    {
        foreach (var (vm, _) in _fences.Where(f => f.Vm.IsFolder))
        {
            vm.Reload();
        }
    }

    // ---- Settings ------------------------------------------------------------------------------

    /// <returns>Problems worth telling the user about, e.g. a hotkey taken by another app.</returns>
    public IReadOnlyList<string> ApplySettings(AppSettings settings, IReadOnlyList<SortRule>? rules)
    {
        var problems = new List<string>();
        Config.Settings = settings;
        if (rules is not null)
        {
            Config.Rules = rules.ToList();
        }

        try
        {
            StartupRegistration.Apply(settings.StartWithWindows);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            problems.Add("Nie udało się ustawić autostartu: " + ex.Message);
        }

        _messages.UnregisterAll();
        if (_messages.Register(settings.ToggleHotkey, ToggleVisibility) is null)
        {
            problems.Add($"Skrót {settings.ToggleHotkey} jest zajęty przez inny program.");
        }
        if (_messages.Register(settings.SearchHotkey, ShowSearch) is null)
        {
            problems.Add($"Skrót {settings.SearchHotkey} jest zajęty przez inny program.");
        }

        _doubleClick.IsEnabled = settings.DoubleClickDesktopToHide;
        _autoSort.Restart(settings);
        foreach (var (_, window) in _fences)
        {
            window.ApplyBlur(settings.UseBlurEffect);
        }

        foreach (var problem in problems)
        {
            Notification?.Invoke(this, problem);
        }
        RequestSave();
        return problems;
    }

    // ---- Persistence ---------------------------------------------------------------------------

    public void RequestSave()
    {
        _saveDebounce.Stop();
        _saveDebounce.Start();
    }

    public void SaveNow()
    {
        _saveDebounce.Stop();
        try
        {
            _store.Save(Config);
            _saveErrorReported = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write(ex, "Zapis konfiguracji");
            if (!_saveErrorReported)
            {
                _saveErrorReported = true;
                Notification?.Invoke(this, "Nie udało się zapisać ustawień: " + ex.Message);
            }
        }
    }

    // ---- Screens -------------------------------------------------------------------------------

    private void OnExplorerRestarted()
    {
        foreach (var (_, window) in _fences)
        {
            DesktopPinning.Repin(window);
        }
        if (!_fencesVisible && _iconsHiddenByToggle)
        {
            DesktopIcons.SetVisible(false);
        }
    }

    private static Bounds PrimaryWorkArea()
    {
        var area = SystemParameters.WorkArea;
        return new Bounds(area.X, area.Y, area.Width, area.Height);
    }

    /// <summary>Work areas of all monitors in WPF units (the app is system-DPI aware).</summary>
    private static List<Bounds> WorkAreas()
    {
        var scale = DpiScale();
        var areas = System.Windows.Forms.Screen.AllScreens
            .OrderByDescending(s => s.Primary)
            .Select(s => new Bounds(s.WorkingArea.X / scale, s.WorkingArea.Y / scale, s.WorkingArea.Width / scale, s.WorkingArea.Height / scale))
            .ToList();
        return areas.Count > 0 ? areas : new List<Bounds> { PrimaryWorkArea() };
    }

    private static double DpiScale()
    {
        var primaryPixels = System.Windows.Forms.Screen.PrimaryScreen?.Bounds.Width ?? 0;
        return primaryPixels > 0 && SystemParameters.PrimaryScreenWidth > 0 ? primaryPixels / SystemParameters.PrimaryScreenWidth : 1;
    }

    private static void EnsureOnScreen(FenceConfig fence)
    {
        var height = fence.IsRolledUp ? 32 : fence.Height;
        var fixedBounds = LayoutMath.EnsureVisible(new Bounds(fence.X, fence.Y, fence.Width, height), WorkAreas(), 32);
        fence.X = fixedBounds.X;
        fence.Y = fixedBounds.Y;
        fence.Width = Math.Max(fixedBounds.Width, FenceConfig.MinWidth);
        if (!fence.IsRolledUp)
        {
            fence.Height = Math.Max(fixedBounds.Height, FenceConfig.MinHeight);
        }
    }

    public void Dispose()
    {
        SaveNow();
        _missingCheck.Stop();
        _autoSort.Dispose();
        _doubleClick.Dispose();
        _showDesktop.Dispose();
        _messages.Dispose();
        if (_iconsHiddenByToggle)
        {
            DesktopIcons.SetVisible(true);
        }
        foreach (var (vm, window) in _fences)
        {
            window.CloseForGood();
            vm.Dispose();
        }
        _fences.Clear();
        _search?.CloseForGood();
        _settingsWindow?.Close();
    }
}
