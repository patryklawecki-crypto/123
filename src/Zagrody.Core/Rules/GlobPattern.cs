using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace Zagrody.Core.Rules;

/// <summary>Case-insensitive '*' and '?' wildcards; several patterns can be joined with ';'.</summary>
public static class GlobPattern
{
    private static readonly ConcurrentDictionary<string, Regex> Cache = new(StringComparer.Ordinal);

    public static bool IsMatch(string patterns, string name)
    {
        foreach (var pattern in patterns.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Cache.GetOrAdd(pattern, Compile).IsMatch(name))
            {
                return true;
            }
        }
        return false;
    }

    private static Regex Compile(string pattern)
    {
        var regex = new StringBuilder("^");
        foreach (var c in pattern)
        {
            regex.Append(c switch
            {
                '*' => ".*",
                '?' => ".",
                _ => Regex.Escape(c.ToString()),
            });
        }
        regex.Append('$');
        return new Regex(regex.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
    }
}
