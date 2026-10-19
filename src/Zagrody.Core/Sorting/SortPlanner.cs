using Zagrody.Core.Models;
using Zagrody.Core.Rules;

namespace Zagrody.Core.Sorting;

public abstract record SortAction(string SourcePath, Guid FenceId, string RuleName);

/// <summary>Physically move the item into a folder fence.</summary>
public sealed record MoveAction(string SourcePath, Guid FenceId, string RuleName, string DestinationPath)
    : SortAction(SourcePath, FenceId, RuleName);

/// <summary>Leave the item where it is and add a reference to a collection fence.</summary>
public sealed record PinAction(string SourcePath, Guid FenceId, string RuleName)
    : SortAction(SourcePath, FenceId, RuleName);

/// <summary>Turns rule matches into concrete actions without touching the disk, so it can drive a preview.</summary>
public static class SortPlanner
{
    public static IReadOnlyList<SortAction> Plan(IEnumerable<FileCandidate> candidates, RuleEngine engine, AppConfig config) =>
        Plan(candidates, engine, config, File.Exists, Directory.Exists);

    internal static IReadOnlyList<SortAction> Plan(
        IEnumerable<FileCandidate> candidates,
        RuleEngine engine,
        AppConfig config,
        Func<string, bool> fileExists,
        Func<string, bool> directoryExists)
    {
        var actions = new List<SortAction>();
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool Taken(string path) => reserved.Contains(path) || fileExists(path) || directoryExists(path);
        var fenceFolders = config.Fences
            .Where(f => f.Mode == FenceMode.Folder && !string.IsNullOrWhiteSpace(f.FolderPath))
            .Select(f => f.FolderPath!)
            .ToList();

        foreach (var item in candidates)
        {
            var rule = engine.Match(item);
            if (rule is null || config.FindFence(rule.TargetFenceId) is not { } fence)
            {
                continue;
            }

            // A folder that backs a fence (or holds one) must stay put, or that fence would lose its contents.
            if (item.IsDirectory && fenceFolders.Any(folder => PathUtil.IsSameOrInside(folder, item.FullPath)))
            {
                continue;
            }

            if (fence.Mode == FenceMode.Collection)
            {
                if (!fence.Items.Contains(item.FullPath, StringComparer.OrdinalIgnoreCase))
                {
                    actions.Add(new PinAction(item.FullPath, fence.Id, rule.Name));
                }
                continue;
            }

            if (string.IsNullOrWhiteSpace(fence.FolderPath))
            {
                continue;
            }

            var destinationDir = PathUtil.Normalize(fence.FolderPath);
            var sourceDir = PathUtil.Normalize(Path.GetDirectoryName(item.FullPath) ?? string.Empty);
            if (PathUtil.AreSame(sourceDir, destinationDir))
            {
                continue;
            }
            if (item.IsDirectory && PathUtil.IsSameOrInside(destinationDir, item.FullPath))
            {
                continue;
            }

            var destination = PathUtil.UniqueDestination(destinationDir, item.Name, Taken);
            reserved.Add(destination);
            actions.Add(new MoveAction(item.FullPath, fence.Id, rule.Name, destination));
        }

        return actions;
    }
}
