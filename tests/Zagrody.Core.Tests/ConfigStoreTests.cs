using Zagrody.Core.Models;
using Zagrody.Core.Storage;

namespace Zagrody.Core.Tests;

public class ConfigStoreTests
{
    [Fact]
    public void Missing_file_gives_empty_config()
    {
        using var dir = new TempDir();
        var store = new ConfigStore(dir.Combine("config.json"));

        var config = store.Load();

        Assert.Empty(config.Fences);
        Assert.Null(store.LoadWarning);
    }

    [Fact]
    public void Round_trips_fences_rules_and_settings()
    {
        using var dir = new TempDir();
        var store = new ConfigStore(dir.Combine("sub", "config.json"));
        var fence = new FenceConfig { Title = "Gry", Mode = FenceMode.Folder, FolderPath = dir.Path, X = 10, Y = 20, SortMode = ItemSortMode.Size };
        var config = new AppConfig
        {
            Fences = { fence },
            Rules = { new SortRule { Name = "Obrazy", TargetFenceId = fence.Id, Extensions = { ".jpg" } } },
            Settings = { AutoSortEnabled = true, SearchHotkey = "Win+Shift+F" },
        };

        store.Save(config);
        var loaded = store.Load();

        var f = Assert.Single(loaded.Fences);
        Assert.Equal(fence.Id, f.Id);
        Assert.Equal("Gry", f.Title);
        Assert.Equal(FenceMode.Folder, f.Mode);
        Assert.Equal(ItemSortMode.Size, f.SortMode);
        Assert.Equal(".jpg", Assert.Single(Assert.Single(loaded.Rules).Extensions));
        Assert.True(loaded.Settings.AutoSortEnabled);
        Assert.Equal("Win+Shift+F", loaded.Settings.SearchHotkey);
    }

    [Fact]
    public void Second_save_keeps_backup_of_previous_version()
    {
        using var dir = new TempDir();
        var store = new ConfigStore(dir.Combine("config.json"));
        store.Save(new AppConfig { Fences = { new FenceConfig { Title = "A" } } });
        store.Save(new AppConfig { Fences = { new FenceConfig { Title = "B" } } });

        Assert.True(File.Exists(store.BackupPath));
        Assert.Contains("\"A\"", File.ReadAllText(store.BackupPath));
        Assert.Contains("\"B\"", File.ReadAllText(store.Path));
    }

    [Fact]
    public void Corrupt_file_falls_back_to_backup_and_is_quarantined()
    {
        using var dir = new TempDir();
        var store = new ConfigStore(dir.Combine("config.json"));
        store.Save(new AppConfig { Fences = { new FenceConfig { Title = "Stara" } } });
        store.Save(new AppConfig { Fences = { new FenceConfig { Title = "Nowa" } } });
        File.WriteAllText(store.Path, "{ to nie jest json");

        var loaded = store.Load();

        Assert.Equal("Stara", Assert.Single(loaded.Fences).Title);
        Assert.NotNull(store.LoadWarning);
        Assert.Single(Directory.GetFiles(dir.Path, "config.json.corrupt-*"));
    }

    [Fact]
    public void Corrupt_file_without_backup_gives_empty_config_with_warning()
    {
        using var dir = new TempDir();
        var store = new ConfigStore(dir.Combine("config.json"));
        File.WriteAllText(store.Path, "[1,2");

        var loaded = store.Load();

        Assert.Empty(loaded.Fences);
        Assert.NotNull(store.LoadWarning);
    }

    [Fact]
    public void Normalize_repairs_bad_values()
    {
        var json = """
        {
          // hand-edited
          "fences": [
            { "id": "00000000-0000-0000-0000-000000000000", "title": "  ", "width": -5, "height": "NaN",
              "iconSize": 999, "accentColor": "red", "mode": "Folder", "items": ["a", "A", "", "b"] },
          ],
          "rules": [ { "name": "sierota", "targetFenceId": "11111111-1111-1111-1111-111111111111", "extensions": ["jpg"] } ],
          "settings": { "gridSize": 0, "toggleHotkey": "H" }
        }
        """;

        var config = ConfigStore.Deserialize(json.Replace("\"NaN\"", "0"));

        var fence = Assert.Single(config.Fences);
        Assert.NotEqual(Guid.Empty, fence.Id);
        Assert.Equal("Zagroda", fence.Title);
        Assert.Equal(FenceConfig.MinWidth, fence.Width);
        Assert.Equal(FenceConfig.MaxIconSize, fence.IconSize);
        Assert.Equal("#3B82F6", fence.AccentColor);
        Assert.Equal(FenceMode.Collection, fence.Mode); // folder mode without a folder makes no sense
        Assert.Equal(new[] { "a", "b" }, fence.Items);
        Assert.Empty(config.Rules); // its target fence doesn't exist
        Assert.Equal(2, config.Settings.GridSize);
        Assert.Equal(AppSettings.DefaultToggleHotkey, config.Settings.ToggleHotkey);
    }
}
