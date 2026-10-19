using Zagrody.Core.Models;
using Zagrody.Core.Rules;
using Zagrody.Core.Sorting;

namespace Zagrody.Core.Tests;

public class SortingTests
{
    [Fact]
    public void Plans_moves_for_folder_fences_and_pins_for_collections()
    {
        using var dir = new TempDir();
        var images = new FenceConfig { Mode = FenceMode.Folder, FolderPath = dir.Combine("Obrazy") };
        var docs = new FenceConfig { Mode = FenceMode.Collection };
        var config = new AppConfig
        {
            Fences = { images, docs },
            Rules =
            {
                new SortRule { TargetFenceId = images.Id, Extensions = { ".jpg" } },
                new SortRule { TargetFenceId = docs.Id, Extensions = { ".docx" } },
            },
        };
        var photo = FileCandidate.FromPath(dir.CreateFile("Pulpit/foto.jpg"))!;
        var doc = FileCandidate.FromPath(dir.CreateFile("Pulpit/cv.docx"))!;
        var other = FileCandidate.FromPath(dir.CreateFile("Pulpit/inne.exe"))!;

        var plan = SortPlanner.Plan(new[] { photo, doc, other }, new RuleEngine(config.Rules), config);

        Assert.Equal(2, plan.Count);
        var move = Assert.IsType<MoveAction>(plan[0]);
        Assert.Equal(Path.Combine(images.FolderPath!, "foto.jpg"), move.DestinationPath);
        Assert.IsType<PinAction>(plan[1]);
    }

    [Fact]
    public void Avoids_name_collisions_including_within_the_same_batch()
    {
        using var dir = new TempDir();
        var target = dir.Combine("Cel");
        dir.CreateFile("Cel/a.txt");
        var fence = new FenceConfig { Mode = FenceMode.Folder, FolderPath = target };
        var config = new AppConfig { Fences = { fence }, Rules = { new SortRule { TargetFenceId = fence.Id, Extensions = { ".txt" } } } };
        var first = FileCandidate.FromPath(dir.CreateFile("x/a.txt"))!;
        var second = FileCandidate.FromPath(dir.CreateFile("y/a.txt"))!;

        var plan = SortPlanner.Plan(new[] { first, second }, new RuleEngine(config.Rules), config).Cast<MoveAction>().ToList();

        Assert.Equal(Path.Combine(target, "a (2).txt"), plan[0].DestinationPath);
        Assert.Equal(Path.Combine(target, "a (3).txt"), plan[1].DestinationPath);
    }

    [Fact]
    public void Skips_items_already_in_place()
    {
        using var dir = new TempDir();
        var fence = new FenceConfig { Mode = FenceMode.Folder, FolderPath = dir.Combine("Cel") };
        var pinned = new FenceConfig { Mode = FenceMode.Collection };
        var pinnedFile = dir.CreateFile("p.md");
        pinned.Items.Add(pinnedFile);
        var config = new AppConfig
        {
            Fences = { fence, pinned },
            Rules =
            {
                new SortRule { TargetFenceId = fence.Id, Extensions = { ".txt" } },
                new SortRule { TargetFenceId = pinned.Id, Extensions = { ".md" } },
            },
        };
        var alreadyThere = FileCandidate.FromPath(dir.CreateFile("Cel/a.txt"))!;

        var plan = SortPlanner.Plan(new[] { alreadyThere, FileCandidate.FromPath(pinnedFile)! }, new RuleEngine(config.Rules), config);

        Assert.Empty(plan);
    }

    [Fact]
    public void Never_moves_a_folder_into_itself()
    {
        using var dir = new TempDir();
        var outer = dir.Combine("Projekty");
        Directory.CreateDirectory(Path.Combine(outer, "archiwum"));
        var fence = new FenceConfig { Mode = FenceMode.Folder, FolderPath = Path.Combine(outer, "archiwum") };
        var config = new AppConfig { Fences = { fence }, Rules = { new SortRule { TargetFenceId = fence.Id, NamePattern = "Proj*", AppliesTo = RuleItemKind.Folders } } };

        var plan = SortPlanner.Plan(new[] { FileCandidate.FromPath(outer)! }, new RuleEngine(config.Rules), config);

        Assert.Empty(plan);
    }

    [Fact]
    public void Never_moves_a_folder_that_backs_another_fence()
    {
        using var dir = new TempDir();
        var backing = dir.Combine("Pulpit", "Gry");
        Directory.CreateDirectory(backing);
        var games = new FenceConfig { Mode = FenceMode.Folder, FolderPath = backing };
        var archive = new FenceConfig { Mode = FenceMode.Folder, FolderPath = dir.Combine("Archiwum") };
        var config = new AppConfig { Fences = { games, archive }, Rules = { new SortRule { TargetFenceId = archive.Id, NamePattern = "*", AppliesTo = RuleItemKind.Folders } } };

        var plan = SortPlanner.Plan(new[] { FileCandidate.FromPath(backing)! }, new RuleEngine(config.Rules), config);

        Assert.Empty(plan);
    }

    [Fact]
    public void Executes_moves_and_undo_restores_them()
    {
        using var dir = new TempDir();
        var source = dir.CreateFile("Pulpit/zdjecie.png", "obraz");
        var destination = dir.Combine("Obrazy", "zdjecie.png");
        var organizer = new FileOrganizer();

        var report = organizer.Execute(new SortAction[] { new MoveAction(source, Guid.Empty, "r", destination) }, _ => { });

        Assert.Single(report.Moved);
        Assert.False(File.Exists(source));
        Assert.Equal("obraz", File.ReadAllText(destination));
        Assert.True(organizer.CanUndo);

        var undo = organizer.UndoLast();

        Assert.Single(undo.Moved);
        Assert.True(File.Exists(source));
        Assert.False(File.Exists(destination));
        Assert.False(organizer.CanUndo);
    }

    [Fact]
    public void Undo_does_not_overwrite_a_new_file_at_the_original_path()
    {
        using var dir = new TempDir();
        var source = dir.CreateFile("Pulpit/a.txt", "stary");
        var organizer = new FileOrganizer();
        organizer.Execute(new SortAction[] { new MoveAction(source, Guid.Empty, "r", dir.Combine("Cel", "a.txt")) }, _ => { });
        File.WriteAllText(source, "nowy");

        organizer.UndoLast();

        Assert.Equal("nowy", File.ReadAllText(source));
        Assert.Equal("stary", File.ReadAllText(dir.Combine("Pulpit", "a (2).txt")));
    }

    [Fact]
    public void Failures_are_reported_not_thrown()
    {
        using var dir = new TempDir();
        var organizer = new FileOrganizer();

        var report = organizer.Execute(new SortAction[] { new MoveAction(dir.Combine("nie-ma.txt"), Guid.Empty, "r", dir.Combine("Cel", "x.txt")) }, _ => { });

        Assert.Single(report.Failures);
        Assert.False(organizer.CanUndo);
    }

    [Fact]
    public void Pins_go_through_callback()
    {
        var pinned = new List<string>();
        var organizer = new FileOrganizer();

        var report = organizer.Execute(new SortAction[] { new PinAction("C:/x.txt", Guid.Empty, "r") }, p => pinned.Add(p.SourcePath));

        Assert.Equal(new[] { "C:/x.txt" }, pinned);
        Assert.Single(report.Pinned);
    }

    [Theory]
    [InlineData(".gitignore", ".gitignore (2)")]
    [InlineData("raport.final.pdf", "raport.final (2).pdf")]
    [InlineData("folder", "folder (2)")]
    public void Unique_destination_naming(string name, string expected)
    {
        var taken = new HashSet<string> { Path.Combine("d", name) };

        Assert.Equal(Path.Combine("d", expected), PathUtil.UniqueDestination("d", name, taken.Contains));
    }
}
