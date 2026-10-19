using Zagrody.Core.Input;

namespace Zagrody.Core.Models;

public sealed class AppSettings
{
    public const string DefaultToggleHotkey = "Ctrl+Alt+H";
    public const string DefaultSearchHotkey = "Ctrl+Alt+Space";

    public bool StartWithWindows { get; set; }
    public bool UseBlurEffect { get; set; } = true;
    public bool SnapToGrid { get; set; } = true;
    public int GridSize { get; set; } = 10;

    /// <summary>Fences stick to each other and to screen edges while being moved or resized.</summary>
    public bool SnapToFences { get; set; } = true;

    /// <summary>Space kept between snapped fences, in pixels at 100% scaling.</summary>
    public int FenceGap { get; set; } = 8;

    /// <summary>Double-clicking empty desktop hides fences and desktop icons, like Stardock Fences.</summary>
    public bool DoubleClickDesktopToHide { get; set; } = true;

    /// <summary>Peek into a rolled-up fence while the mouse is over it.</summary>
    public bool PeekRolledUpOnHover { get; set; } = true;

    public string ToggleHotkey { get; set; } = DefaultToggleHotkey;
    public string SearchHotkey { get; set; } = DefaultSearchHotkey;

    public bool AutoSortEnabled { get; set; }

    /// <summary>How long a new file has to sit still before it is sorted.</summary>
    public int AutoSortDelaySeconds { get; set; } = 5;

    /// <summary>Folders scanned by sorting rules. Empty means the user's desktop.</summary>
    public List<string> WatchedFolders { get; set; } = new();

    internal void Normalize()
    {
        GridSize = Math.Clamp(GridSize, 2, 100);
        FenceGap = Math.Clamp(FenceGap, 0, 64);
        AutoSortDelaySeconds = Math.Clamp(AutoSortDelaySeconds, 1, 3600);
        WatchedFolders = (WatchedFolders ?? new())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!Hotkey.TryParse(ToggleHotkey, out _))
        {
            ToggleHotkey = DefaultToggleHotkey;
        }
        if (!Hotkey.TryParse(SearchHotkey, out _))
        {
            SearchHotkey = DefaultSearchHotkey;
        }
    }
}
