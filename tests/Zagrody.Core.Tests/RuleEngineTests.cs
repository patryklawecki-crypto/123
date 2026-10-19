using Microsoft.Extensions.Time.Testing;
using Zagrody.Core.Models;
using Zagrody.Core.Rules;

namespace Zagrody.Core.Tests;

public class RuleEngineTests
{
    private static readonly DateTime Now = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
    private static readonly FakeTimeProvider Clock = new(new DateTimeOffset(Now));

    private static FileCandidate File(string name, long size = 100, int ageDays = 0, FileAttributes attrs = FileAttributes.Normal) =>
        new(Path.Combine("C:", "Desktop", name), false, size, Now.AddDays(-ageDays), attrs);

    private static FileCandidate Folder(string name) =>
        new(Path.Combine("C:", "Desktop", name), true, 0, Now);

    [Fact]
    public void Matches_by_extension_case_insensitively()
    {
        var rule = new SortRule { Extensions = ExtensionList.Parse("jpg, PNG") };
        var engine = new RuleEngine(new[] { rule }, Clock);

        Assert.Same(rule, engine.Match(File("Wakacje.JPG")));
        Assert.Same(rule, engine.Match(File("a.png")));
        Assert.Null(engine.Match(File("a.gif")));
    }

    [Fact]
    public void First_matching_rule_wins()
    {
        var specific = new SortRule { Name = "faktury", Extensions = { ".pdf" }, NamePattern = "faktura*" };
        var general = new SortRule { Name = "pdf", Extensions = { ".pdf" } };
        var engine = new RuleEngine(new[] { specific, general }, Clock);

        Assert.Same(specific, engine.Match(File("Faktura_10_2026.pdf")));
        Assert.Same(general, engine.Match(File("instrukcja.pdf")));
    }

    [Fact]
    public void All_conditions_must_hold()
    {
        var rule = new SortRule { Extensions = { ".zip" }, OlderThanDays = 30, MinSizeMb = 1 };
        var engine = new RuleEngine(new[] { rule }, Clock);
        const long mb = 1024 * 1024;

        Assert.NotNull(engine.Match(File("old-big.zip", 2 * mb, 31)));
        Assert.Null(engine.Match(File("new-big.zip", 2 * mb, 5)));
        Assert.Null(engine.Match(File("old-small.zip", 10, 31)));
    }

    [Fact]
    public void Rule_without_conditions_or_disabled_never_matches()
    {
        var empty = new SortRule();
        var disabled = new SortRule { Enabled = false, Extensions = { ".txt" } };
        var engine = new RuleEngine(new[] { empty, disabled }, Clock);

        Assert.Null(engine.Match(File("notatka.txt")));
    }

    [Fact]
    public void Respects_item_kind()
    {
        var files = new SortRule { NamePattern = "projekt*", AppliesTo = RuleItemKind.Files };
        var folders = new SortRule { NamePattern = "projekt*", AppliesTo = RuleItemKind.Folders };
        var engine = new RuleEngine(new[] { files, folders }, Clock);

        Assert.Same(files, engine.Match(File("projekt.docx")));
        Assert.Same(folders, engine.Match(Folder("projekt-x")));
    }

    [Theory]
    [InlineData("film.mp4.crdownload")]
    [InlineData("~$raport.docx")]
    [InlineData("desktop.ini")]
    [InlineData("pobieranie.part")]
    public void Never_touches_transient_or_system_files(string name)
    {
        var rule = new SortRule { NamePattern = "*" };
        var engine = new RuleEngine(new[] { rule }, Clock);

        Assert.Null(engine.Match(File(name)));
    }

    [Fact]
    public void Never_touches_hidden_files()
    {
        var engine = new RuleEngine(new[] { new SortRule { NamePattern = "*" } }, Clock);

        Assert.Null(engine.Match(File("sekret.txt", attrs: FileAttributes.Hidden)));
    }

    [Theory]
    [InlineData("*.txt", "a.TXT", true)]
    [InlineData("rap?rt*", "raport 2026.docx", true)]
    [InlineData("a;b*", "bcd", true)]
    [InlineData("a.b", "aXb", false)]
    [InlineData("[x]*", "[x]plik", true)]
    public void Glob_patterns(string pattern, string name, bool expected)
    {
        Assert.Equal(expected, GlobPattern.IsMatch(pattern, name));
    }
}
