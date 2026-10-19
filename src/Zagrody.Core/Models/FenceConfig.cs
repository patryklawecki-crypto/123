namespace Zagrody.Core.Models;

public enum FenceMode
{
    /// <summary>The fence holds references to files that stay where they are.</summary>
    Collection,

    /// <summary>The fence shows the live contents of a folder on disk.</summary>
    Folder,
}

public enum ItemSortMode
{
    Manual,
    Name,
    Type,
    DateModified,
    Size,
}

public sealed class FenceConfig
{
    public const double MinWidth = 140;
    public const double MinHeight = 90;
    public const int MinIconSize = 16;
    public const int MaxIconSize = 128;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "Nowa zagroda";
    public FenceMode Mode { get; set; } = FenceMode.Collection;

    /// <summary>Folder shown by a <see cref="FenceMode.Folder"/> fence.</summary>
    public string? FolderPath { get; set; }

    /// <summary>Absolute paths pinned to a <see cref="FenceMode.Collection"/> fence, in manual order.</summary>
    public List<string> Items { get; set; } = new();

    public double X { get; set; } = 100;
    public double Y { get; set; } = 100;
    public double Width { get; set; } = 340;
    public double Height { get; set; } = 240;

    public bool IsRolledUp { get; set; }
    public bool IsLocked { get; set; }
    public string AccentColor { get; set; } = "#3B82F6";

    /// <summary>Opacity of the tinted background, 0..1.</summary>
    public double BackgroundOpacity { get; set; } = 0.55;

    public ItemSortMode SortMode { get; set; } = ItemSortMode.Manual;
    public bool SortDescending { get; set; }
    public int IconSize { get; set; } = 48;
    public bool ShowHiddenFiles { get; set; }

    internal void Normalize()
    {
        Title = string.IsNullOrWhiteSpace(Title) ? "Zagroda" : Title.Trim();
        Items ??= new();
        Items = Items
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (Mode == FenceMode.Folder && string.IsNullOrWhiteSpace(FolderPath))
        {
            Mode = FenceMode.Collection;
        }

        Width = Sanitize(Width, 340, MinWidth);
        Height = Sanitize(Height, 240, MinHeight);
        X = double.IsFinite(X) ? X : 100;
        Y = double.IsFinite(Y) ? Y : 100;
        IconSize = Math.Clamp(IconSize, MinIconSize, MaxIconSize);
        BackgroundOpacity = double.IsFinite(BackgroundOpacity) ? Math.Clamp(BackgroundOpacity, 0, 1) : 0.55;
        if (!ColorParser.IsValidHex(AccentColor))
        {
            AccentColor = "#3B82F6";
        }

        // Manual order only means something for pinned items.
        if (Mode == FenceMode.Folder && SortMode == ItemSortMode.Manual)
        {
            SortMode = ItemSortMode.Name;
        }
    }

    private static double Sanitize(double value, double fallback, double min) =>
        double.IsFinite(value) ? Math.Max(value, min) : fallback;
}
