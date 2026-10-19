using Zagrody.Core.Models;

namespace Zagrody.Core.Search;

public interface ISortableItem
{
    string DisplayName { get; }
    string Extension { get; }
    bool IsDirectory { get; }
    DateTime LastWriteTimeUtc { get; }
    long Size { get; }
}

public static class ItemSorter
{
    private static readonly StringComparer NameComparer = StringComparer.Create(System.Globalization.CultureInfo.CurrentCulture, ignoreCase: true);

    /// <summary>
    /// Sorts items for display. Folders stay on top for every mode except <see cref="ItemSortMode.Manual"/>,
    /// which keeps the given order.
    /// </summary>
    public static IReadOnlyList<T> Sort<T>(IEnumerable<T> items, ItemSortMode mode, bool descending)
        where T : ISortableItem
    {
        if (mode == ItemSortMode.Manual)
        {
            var list = items.ToList();
            if (descending)
            {
                list.Reverse();
            }
            return list;
        }

        var foldersFirst = items.OrderByDescending(i => i.IsDirectory);
        IOrderedEnumerable<T> sorted = mode switch
        {
            ItemSortMode.Type => Then(foldersFirst, i => i.Extension, descending, NameComparer)
                .ThenBy(i => i.DisplayName, NameComparer),
            ItemSortMode.DateModified => Then(foldersFirst, i => i.LastWriteTimeUtc, descending, Comparer<DateTime>.Default)
                .ThenBy(i => i.DisplayName, NameComparer),
            ItemSortMode.Size => Then(foldersFirst, i => i.Size, descending, Comparer<long>.Default)
                .ThenBy(i => i.DisplayName, NameComparer),
            _ => Then(foldersFirst, i => i.DisplayName, descending, NameComparer),
        };
        return sorted.ToList();
    }

    private static IOrderedEnumerable<T> Then<T, TKey>(IOrderedEnumerable<T> source, Func<T, TKey> key, bool descending, IComparer<TKey> comparer) =>
        descending ? source.ThenByDescending(key, comparer) : source.ThenBy(key, comparer);
}
