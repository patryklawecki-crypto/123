namespace Zagrody.Core.Sorting;

public sealed record MoveRecord(string SourcePath, string DestinationPath);

public sealed record SortFailure(string Path, string Message);

public sealed class SortReport
{
    public List<MoveRecord> Moved { get; } = new();
    public List<PinAction> Pinned { get; } = new();
    public List<SortFailure> Failures { get; } = new();
    public int Total => Moved.Count + Pinned.Count;
}

/// <summary>Executes sort plans and keeps a short history so the last runs can be undone.</summary>
public sealed class FileOrganizer
{
    private const int MaxHistory = 20;
    private readonly LinkedList<IReadOnlyList<MoveRecord>> _history = new();

    public bool CanUndo => _history.Count > 0;

    /// <param name="pin">Applies a <see cref="PinAction"/>; called on the caller's thread.</param>
    public SortReport Execute(IEnumerable<SortAction> actions, Action<PinAction> pin)
    {
        var report = new SortReport();
        foreach (var action in actions)
        {
            try
            {
                switch (action)
                {
                    case PinAction p:
                        pin(p);
                        report.Pinned.Add(p);
                        break;
                    case MoveAction m:
                        report.Moved.Add(Move(m.SourcePath, m.DestinationPath));
                        break;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                report.Failures.Add(new SortFailure(action.SourcePath, ex.Message));
            }
        }

        if (report.Moved.Count > 0)
        {
            _history.AddLast(report.Moved.ToList());
            while (_history.Count > MaxHistory)
            {
                _history.RemoveFirst();
            }
        }
        return report;
    }

    /// <summary>Moves the most recent batch back to where it came from.</summary>
    public SortReport UndoLast()
    {
        var report = new SortReport();
        if (_history.Last?.Value is not { } last)
        {
            return report;
        }
        _history.RemoveLast();

        foreach (var record in last.Reverse())
        {
            try
            {
                if (!PathUtil.Exists(record.DestinationPath))
                {
                    report.Failures.Add(new SortFailure(record.DestinationPath, "Element już nie istnieje."));
                    continue;
                }
                report.Moved.Add(Move(record.DestinationPath, record.SourcePath));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                report.Failures.Add(new SortFailure(record.DestinationPath, ex.Message));
            }
        }
        return report;
    }

    /// <summary>
    /// Moves a file or folder. The destination is re-checked right before moving because
    /// something may have appeared there since the plan was made.
    /// </summary>
    public static MoveRecord Move(string source, string destination)
    {
        var directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);
        destination = PathUtil.UniqueDestination(directory, Path.GetFileName(destination), PathUtil.Exists);

        if (Directory.Exists(source))
        {
            Directory.Move(source, destination);
        }
        else
        {
            File.Move(source, destination, overwrite: false);
        }
        return new MoveRecord(source, destination);
    }
}
