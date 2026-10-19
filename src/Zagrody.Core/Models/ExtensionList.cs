namespace Zagrody.Core.Models;

public static class ExtensionList
{
    private static readonly char[] Separators = { ',', ';', ' ', '\t', '\n', '\r' };

    /// <summary>Parses user input like "jpg, .PNG;gif" into [".jpg", ".png", ".gif"].</summary>
    public static List<string> Parse(string? text) =>
        Normalize((text ?? string.Empty).Split(Separators, StringSplitOptions.RemoveEmptyEntries));

    public static string Format(IEnumerable<string> extensions) =>
        string.Join(", ", extensions.Select(e => e.TrimStart('.')));

    public static List<string> Normalize(IEnumerable<string> extensions) =>
        extensions
            .Select(e => e.Trim().TrimStart('*').Trim())
            .Where(e => e.Length > 0 && e != ".")
            .Select(e => (e.StartsWith('.') ? e : "." + e).ToLowerInvariant())
            .Distinct()
            .ToList();
}
