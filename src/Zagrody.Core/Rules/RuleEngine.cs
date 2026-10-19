using Zagrody.Core.Models;

namespace Zagrody.Core.Rules;

/// <summary>Finds the first enabled rule, in list order, whose conditions all hold for an item.</summary>
public sealed class RuleEngine
{
    private readonly IReadOnlyList<SortRule> _rules;
    private readonly TimeProvider _clock;

    public RuleEngine(IEnumerable<SortRule> rules, TimeProvider? clock = null)
    {
        _rules = rules.Where(r => r.Enabled && r.HasConditions).ToList();
        _clock = clock ?? TimeProvider.System;
    }

    public SortRule? Match(FileCandidate item)
    {
        if (item.IsTransientOrSystem)
        {
            return null;
        }
        return _rules.FirstOrDefault(rule => Matches(rule, item));
    }

    public bool Matches(SortRule rule, FileCandidate item)
    {
        if (!rule.HasConditions)
        {
            return false;
        }

        var kindOk = rule.AppliesTo switch
        {
            RuleItemKind.Files => !item.IsDirectory,
            RuleItemKind.Folders => item.IsDirectory,
            _ => true,
        };
        if (!kindOk)
        {
            return false;
        }

        if (rule.Extensions.Count > 0 && !rule.Extensions.Contains(item.Extension))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(rule.NamePattern) && !GlobPattern.IsMatch(rule.NamePattern, item.Name))
        {
            return false;
        }

        if (rule.OlderThanDays is int days && days > 0)
        {
            var age = _clock.GetUtcNow().UtcDateTime - item.LastWriteTimeUtc;
            if (age < TimeSpan.FromDays(days))
            {
                return false;
            }
        }

        if (rule.MinSizeMb is double minMb && minMb > 0)
        {
            if (item.IsDirectory || item.Size < minMb * 1024 * 1024)
            {
                return false;
            }
        }

        return true;
    }
}
