namespace Zagrody.Core.Models;

/// <summary>Root of everything persisted to config.json.</summary>
public sealed class AppConfig
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public List<FenceConfig> Fences { get; set; } = new();
    public List<SortRule> Rules { get; set; } = new();
    public AppSettings Settings { get; set; } = new();

    public FenceConfig? FindFence(Guid id) => Fences.FirstOrDefault(f => f.Id == id);

    /// <summary>
    /// Repairs values that may be missing or out of range after loading a hand-edited
    /// or older config, so the rest of the app can trust the model.
    /// </summary>
    public void Normalize()
    {
        Fences ??= new();
        Rules ??= new();
        Settings ??= new();

        Fences.RemoveAll(f => f is null);
        var seenIds = new HashSet<Guid>();
        foreach (var fence in Fences)
        {
            if (fence.Id == Guid.Empty || !seenIds.Add(fence.Id))
            {
                fence.Id = Guid.NewGuid();
                seenIds.Add(fence.Id);
            }
            fence.Normalize();
        }

        Rules.RemoveAll(r => r is null || FindFence(r.TargetFenceId) is null);
        foreach (var rule in Rules)
        {
            rule.Normalize();
        }

        Settings.Normalize();
        Version = CurrentVersion;
    }
}
