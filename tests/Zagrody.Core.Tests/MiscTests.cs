using Zagrody.Core.Input;
using Zagrody.Core.Layout;
using Zagrody.Core.Models;
using Zagrody.Core.Search;

namespace Zagrody.Core.Tests;

public class FuzzyMatcherTests
{
    [Fact]
    public void Ranks_prefix_over_word_start_over_scattered()
    {
        var prefix = FuzzyMatcher.Score("wor", "Word 2021");
        var wordStart = FuzzyMatcher.Score("wor", "Microsoft Word");
        var scattered = FuzzyMatcher.Score("wrd", "Word");

        Assert.NotNull(scattered);
        Assert.True(prefix > wordStart);
        Assert.True(wordStart > scattered);
    }

    [Fact]
    public void Ignores_case_and_polish_diacritics()
    {
        Assert.NotNull(FuzzyMatcher.Score("zolw", "Żółw.png"));
        Assert.NotNull(FuzzyMatcher.Score("LODZ", "łódź"));
    }

    [Fact]
    public void Returns_null_when_letters_are_missing_or_out_of_order()
    {
        Assert.Null(FuzzyMatcher.Score("xyz", "Word"));
        Assert.Null(FuzzyMatcher.Score("dw", "Word"));
    }

    [Fact]
    public void Empty_query_matches_everything()
    {
        Assert.Equal(0, FuzzyMatcher.Score("  ", "cokolwiek"));
    }
}

public class ItemSorterTests
{
    private sealed record Item(string DisplayName, string Extension, bool IsDirectory, DateTime LastWriteTimeUtc, long Size) : ISortableItem;

    private static readonly Item[] Items =
    {
        new("b.txt", ".txt", false, new DateTime(2026, 1, 3), 30),
        new("Zdjęcia", "", true, new DateTime(2026, 1, 1), 0),
        new("a.pdf", ".pdf", false, new DateTime(2026, 1, 2), 10),
    };

    [Fact]
    public void Folders_first_then_by_name()
    {
        var sorted = ItemSorter.Sort(Items, ItemSortMode.Name, false).Select(i => i.DisplayName);

        Assert.Equal(new[] { "Zdjęcia", "a.pdf", "b.txt" }, sorted);
    }

    [Fact]
    public void Descending_by_size_keeps_folders_first()
    {
        var sorted = ItemSorter.Sort(Items, ItemSortMode.Size, true).Select(i => i.DisplayName);

        Assert.Equal(new[] { "Zdjęcia", "b.txt", "a.pdf" }, sorted);
    }

    [Fact]
    public void Manual_keeps_given_order()
    {
        var sorted = ItemSorter.Sort(Items, ItemSortMode.Manual, false);

        Assert.Equal(Items, sorted);
    }
}

public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+Space", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, "Space")]
    [InlineData(" win + shift + F ", HotkeyModifiers.Win | HotkeyModifiers.Shift, "F")]
    public void Parses(string text, HotkeyModifiers modifiers, string key)
    {
        Assert.True(Hotkey.TryParse(text, out var hotkey));
        Assert.Equal(modifiers, hotkey.Modifiers);
        Assert.Equal(key, hotkey.Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("H")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+A+B")]
    public void Rejects_invalid(string text)
    {
        Assert.False(Hotkey.TryParse(text, out _));
    }

    [Fact]
    public void Formats_in_canonical_order()
    {
        Hotkey.TryParse("shift+ctrl+K", out var hotkey);

        Assert.Equal("Ctrl+Shift+K", hotkey.ToString());
    }
}

public class LayoutMathTests
{
    private static readonly Bounds Primary = new(0, 0, 1920, 1040);
    private static readonly Bounds Second = new(1920, 0, 1280, 1000);

    [Fact]
    public void Keeps_visible_window_in_place()
    {
        var window = new Bounds(2000, 100, 300, 200);

        Assert.Equal(window, LayoutMath.EnsureVisible(window, new[] { Primary, Second }, 30));
    }

    [Fact]
    public void Moves_window_from_unplugged_monitor_onto_primary()
    {
        var window = new Bounds(4000, 300, 300, 200);

        var result = LayoutMath.EnsureVisible(window, new[] { Primary }, 30);

        Assert.Equal(new Bounds(1620, 300, 300, 200), result);
    }

    [Fact]
    public void Moves_window_whose_title_is_above_the_screen()
    {
        var result = LayoutMath.EnsureVisible(new Bounds(100, -50, 300, 200), new[] { Primary }, 30);

        Assert.Equal(0, result.Y);
    }

    [Fact]
    public void Finds_spot_that_does_not_overlap()
    {
        var occupied = new[] { new Bounds(16, 16, 300, 200) };

        var spot = LayoutMath.FindFreeSpot(Primary, 300, 200, occupied);

        Assert.DoesNotContain(occupied, o => o.Intersects(spot));
    }

    [Theory]
    [InlineData(14, 10, 10)]
    [InlineData(15, 10, 20)]
    [InlineData(7, 1, 7)]
    public void Snaps_to_grid(double value, int grid, double expected)
    {
        Assert.Equal(expected, LayoutMath.Snap(value, grid));
    }
}

public class ParsingTests
{
    [Fact]
    public void Extension_list_is_forgiving()
    {
        Assert.Equal(new[] { ".jpg", ".png", ".gif" }, ExtensionList.Parse("jpg, .PNG;*.gif  jpg"));
        Assert.Equal("jpg, png", ExtensionList.Format(new[] { ".jpg", ".png" }));
    }

    [Theory]
    [InlineData("#3B82F6", true)]
    [InlineData("#803B82F6", true)]
    [InlineData("3B82F6", false)]
    [InlineData("#XYZXYZ", false)]
    public void Color_validation(string value, bool valid)
    {
        Assert.Equal(valid, ColorParser.IsValidHex(value));
    }
}

public class SnappingTests
{
    private static readonly Bounds Screen = new(0, 0, 1920, 1040);
    private static readonly SnapOptions Options = new(Gap: 8, Threshold: 12, Grid: 10, SnapToNeighbours: true);

    [Fact]
    public void Sticks_beside_a_neighbour_with_the_gap()
    {
        var neighbour = new Bounds(100, 100, 300, 200);

        var result = LayoutMath.SnapMove(new Bounds(413, 130, 200, 150), new[] { neighbour }, new[] { Screen }, Options);

        Assert.Equal(408, result.X); // 400 + gap
    }

    [Fact]
    public void Stacks_below_a_neighbour_and_aligns_left_edges()
    {
        var neighbour = new Bounds(100, 100, 300, 200);

        var result = LayoutMath.SnapMove(new Bounds(105, 311, 300, 150), new[] { neighbour }, new[] { Screen }, Options);

        Assert.Equal(308, result.Y); // 300 + gap
        Assert.Equal(100, result.X); // aligned with the fence above
    }

    [Fact]
    public void Keeps_gap_from_screen_edges()
    {
        var result = LayoutMath.SnapMove(new Bounds(3, 1040 - 150 - 2, 200, 150), Array.Empty<Bounds>(), new[] { Screen }, Options);

        Assert.Equal(8, result.X);
        Assert.Equal(1040 - 150 - 8, result.Y);
    }

    [Fact]
    public void Falls_back_to_grid_when_nothing_is_near()
    {
        var result = LayoutMath.SnapMove(new Bounds(733, 517, 200, 150), Array.Empty<Bounds>(), new[] { Screen }, Options);

        Assert.Equal(new Bounds(730, 520, 200, 150), result);
    }

    [Fact]
    public void Far_neighbours_do_not_attract()
    {
        var neighbour = new Bounds(100, 600, 300, 200); // far below, no vertical overlap

        var result = LayoutMath.SnapMove(new Bounds(413, 100, 200, 150), new[] { neighbour }, new[] { Screen }, Options);

        Assert.Equal(410, result.X); // grid, not the neighbour's edge
    }

    [Fact]
    public void Resize_moves_only_dragged_edge_and_respects_minimum()
    {
        var neighbour = new Bounds(500, 100, 300, 200);

        var result = LayoutMath.SnapResize(new Bounds(100, 100, 385, 200), ResizeEdges.Right, new[] { neighbour }, new[] { Screen }, Options, 140, 90);

        Assert.Equal(100, result.X);
        Assert.Equal(492, result.Right); // 500 - gap

        var tiny = LayoutMath.SnapResize(new Bounds(100, 100, 150, 200), ResizeEdges.Left, Array.Empty<Bounds>(), new[] { Screen }, Options with { Grid = 0 }, 140, 90);
        Assert.True(tiny.Width >= 140);
    }
}
