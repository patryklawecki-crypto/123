namespace Zagrody.Core.Models;

public enum RuleItemKind
{
    Files,
    Folders,
    Any,
}

/// <summary>
/// Routes matching items from watched folders into a fence. All conditions that are set
/// must hold; a rule without any condition never matches, so an empty rule can't sweep a folder.
/// </summary>
public sealed class SortRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Nowa reguła";
    public bool Enabled { get; set; } = true;
    public Guid TargetFenceId { get; set; }
    public RuleItemKind AppliesTo { get; set; } = RuleItemKind.Files;

    /// <summary>Extensions such as ".jpg"; matching is case-insensitive.</summary>
    public List<string> Extensions { get; set; } = new();

    /// <summary>Wildcard patterns separated by ';', e.g. "faktura*;*rachunek*".</summary>
    public string? NamePattern { get; set; }

    /// <summary>Only items not modified for at least this many days.</summary>
    public int? OlderThanDays { get; set; }

    /// <summary>Only files at least this large, in megabytes.</summary>
    public double? MinSizeMb { get; set; }

    public bool HasConditions =>
        Extensions.Count > 0
        || !string.IsNullOrWhiteSpace(NamePattern)
        || OlderThanDays is > 0
        || MinSizeMb is > 0;

    internal void Normalize()
    {
        Name = string.IsNullOrWhiteSpace(Name) ? "Reguła" : Name.Trim();
        Extensions = ExtensionList.Normalize(Extensions ?? new());
        NamePattern = string.IsNullOrWhiteSpace(NamePattern) ? null : NamePattern.Trim();
        if (OlderThanDays is <= 0)
        {
            OlderThanDays = null;
        }
        if (MinSizeMb is not { } size || !double.IsFinite(size) || size <= 0)
        {
            MinSizeMb = null;
        }
    }
}
