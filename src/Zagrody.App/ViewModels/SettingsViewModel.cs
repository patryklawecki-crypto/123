using System.Collections.ObjectModel;
using System.IO;
using Zagrody.Core.Models;
using Zagrody.Core.Sorting;

namespace Zagrody.App.ViewModels;

public sealed record FenceOption(Guid Id, string Title);

public sealed record KindOption(RuleItemKind Value, string Label);

public sealed record RulePreset(string Name, string Extensions, RuleItemKind Kind = RuleItemKind.Files);

/// <summary>Editable copy of a <see cref="SortRule"/>; nothing reaches the live config until Save.</summary>
public sealed class RuleRow : ObservableObject
{
    private bool _enabled = true;
    private string _name = "Nowa reguła";
    private Guid _targetFenceId;
    private RuleItemKind _appliesTo = RuleItemKind.Files;
    private string _extensionsText = string.Empty;
    private string? _namePattern;
    private int? _olderThanDays;
    private double? _minSizeMb;

    public Guid Id { get; init; } = Guid.NewGuid();
    public bool Enabled { get => _enabled; set => SetField(ref _enabled, value); }
    public string Name { get => _name; set => SetField(ref _name, value); }
    public Guid TargetFenceId { get => _targetFenceId; set => SetField(ref _targetFenceId, value); }
    public RuleItemKind AppliesTo { get => _appliesTo; set => SetField(ref _appliesTo, value); }
    public string ExtensionsText { get => _extensionsText; set => SetField(ref _extensionsText, value); }
    public string? NamePattern { get => _namePattern; set => SetField(ref _namePattern, value); }
    public int? OlderThanDays { get => _olderThanDays; set => SetField(ref _olderThanDays, value); }
    public double? MinSizeMb { get => _minSizeMb; set => SetField(ref _minSizeMb, value); }

    public static RuleRow From(SortRule rule) => new()
    {
        Id = rule.Id,
        Enabled = rule.Enabled,
        Name = rule.Name,
        TargetFenceId = rule.TargetFenceId,
        AppliesTo = rule.AppliesTo,
        ExtensionsText = ExtensionList.Format(rule.Extensions),
        NamePattern = rule.NamePattern,
        OlderThanDays = rule.OlderThanDays,
        MinSizeMb = rule.MinSizeMb,
    };

    public SortRule ToRule() => new()
    {
        Id = Id,
        Enabled = Enabled,
        Name = string.IsNullOrWhiteSpace(Name) ? "Reguła" : Name.Trim(),
        TargetFenceId = TargetFenceId,
        AppliesTo = AppliesTo,
        Extensions = ExtensionList.Parse(ExtensionsText),
        NamePattern = string.IsNullOrWhiteSpace(NamePattern) ? null : NamePattern.Trim(),
        OlderThanDays = OlderThanDays is > 0 ? OlderThanDays : null,
        MinSizeMb = MinSizeMb is > 0 ? MinSizeMb : null,
    };
}

public sealed class SettingsViewModel : ObservableObject
{
    public static IReadOnlyList<KindOption> Kinds { get; } = new[]
    {
        new KindOption(RuleItemKind.Files, "Pliki"),
        new KindOption(RuleItemKind.Folders, "Foldery"),
        new KindOption(RuleItemKind.Any, "Pliki i foldery"),
    };

    public static IReadOnlyList<RulePreset> Presets { get; } = new[]
    {
        new RulePreset("Obrazy", "jpg, jpeg, png, gif, webp, bmp, heic, svg"),
        new RulePreset("Dokumenty", "pdf, doc, docx, odt, rtf, txt, md"),
        new RulePreset("Arkusze i prezentacje", "xls, xlsx, ods, csv, ppt, pptx, odp"),
        new RulePreset("Archiwa", "zip, rar, 7z, tar, gz"),
        new RulePreset("Instalatory", "exe, msi, msix"),
        new RulePreset("Wideo", "mp4, mkv, avi, mov, webm"),
        new RulePreset("Muzyka", "mp3, flac, wav, ogg, m4a"),
        new RulePreset("Skróty do programów", "lnk, url"),
    };

    private RuleRow? _selectedRule;
    private string? _selectedFolder;
    private string _previewText = "Kliknij „Podgląd”, żeby zobaczyć, co zostałoby posortowane — nic nie zostanie przeniesione.";

    public SettingsViewModel(AppConfig config)
    {
        var s = config.Settings;
        StartWithWindows = s.StartWithWindows;
        UseBlurEffect = s.UseBlurEffect;
        SnapToGrid = s.SnapToGrid;
        GridSize = s.GridSize;
        SnapToFences = s.SnapToFences;
        FenceGap = s.FenceGap;
        DoubleClickDesktopToHide = s.DoubleClickDesktopToHide;
        PeekRolledUpOnHover = s.PeekRolledUpOnHover;
        ToggleHotkey = s.ToggleHotkey;
        SearchHotkey = s.SearchHotkey;
        AutoSortEnabled = s.AutoSortEnabled;
        AutoSortDelaySeconds = s.AutoSortDelaySeconds;
        WatchedFolders = new ObservableCollection<string>(s.WatchedFolders);
        Fences = config.Fences.Select(f => new FenceOption(f.Id, f.Mode == FenceMode.Folder ? $"{f.Title} (folder)" : f.Title)).ToList();
        Rules = new ObservableCollection<RuleRow>(config.Rules.Select(RuleRow.From));
    }

    public bool StartWithWindows { get; set; }
    public bool UseBlurEffect { get; set; }
    public bool SnapToGrid { get; set; }
    public int GridSize { get; set; }
    public bool SnapToFences { get; set; }
    public int FenceGap { get; set; }
    public bool DoubleClickDesktopToHide { get; set; }
    public bool PeekRolledUpOnHover { get; set; }
    public string ToggleHotkey { get; set; }
    public string SearchHotkey { get; set; }
    public bool AutoSortEnabled { get; set; }
    public int AutoSortDelaySeconds { get; set; }

    public ObservableCollection<string> WatchedFolders { get; }
    public IReadOnlyList<FenceOption> Fences { get; }
    public ObservableCollection<RuleRow> Rules { get; }
    public bool HasFences => Fences.Count > 0;

    public string WatchedFoldersHint => WatchedFolders.Count == 0 ? "Brak — obserwowany jest pulpit." : string.Empty;

    public RuleRow? SelectedRule { get => _selectedRule; set => SetField(ref _selectedRule, value); }
    public string? SelectedFolder { get => _selectedFolder; set => SetField(ref _selectedFolder, value); }
    public string PreviewText { get => _previewText; set => SetField(ref _previewText, value); }

    public void AddFolder(string folder)
    {
        if (!WatchedFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
        {
            WatchedFolders.Add(folder);
            OnPropertyChanged(nameof(WatchedFoldersHint));
        }
    }

    public void RemoveSelectedFolder()
    {
        if (SelectedFolder is { } folder)
        {
            WatchedFolders.Remove(folder);
            OnPropertyChanged(nameof(WatchedFoldersHint));
        }
    }

    public void AddRule(RulePreset? preset)
    {
        var target = SelectedRule?.TargetFenceId is { } id && id != Guid.Empty ? id : Fences.FirstOrDefault()?.Id ?? Guid.Empty;
        if (preset is not null && Fences.FirstOrDefault(f => f.Title.StartsWith(preset.Name, StringComparison.CurrentCultureIgnoreCase)) is { } named)
        {
            target = named.Id; // a fence called like the preset is the obvious target
        }
        var row = new RuleRow
        {
            Name = preset?.Name ?? "Nowa reguła",
            ExtensionsText = preset?.Extensions ?? string.Empty,
            AppliesTo = preset?.Kind ?? RuleItemKind.Files,
            TargetFenceId = target,
        };
        Rules.Add(row);
        SelectedRule = row;
    }

    public void RemoveSelectedRule()
    {
        if (SelectedRule is { } rule)
        {
            var index = Rules.IndexOf(rule);
            Rules.Remove(rule);
            SelectedRule = Rules.Count == 0 ? null : Rules[Math.Min(index, Rules.Count - 1)];
        }
    }

    public void MoveSelectedRule(int delta)
    {
        if (SelectedRule is not { } rule)
        {
            return;
        }
        var index = Rules.IndexOf(rule);
        var target = index + delta;
        if (target >= 0 && target < Rules.Count)
        {
            Rules.Move(index, target);
        }
    }

    public List<SortRule> BuildRules() => Rules.Select(r => r.ToRule()).ToList();

    /// <returns>Messages describing what has to be fixed before saving; empty when valid.</returns>
    public List<string> Validate(Func<string, bool> isValidHotkey)
    {
        var errors = new List<string>();
        if (!isValidHotkey(ToggleHotkey))
        {
            errors.Add($"Nieprawidłowy skrót „{ToggleHotkey}”. Przykład: Ctrl+Alt+H.");
        }
        if (!isValidHotkey(SearchHotkey))
        {
            errors.Add($"Nieprawidłowy skrót „{SearchHotkey}”. Przykład: Ctrl+Alt+Space.");
        }
        if (string.Equals(ToggleHotkey.Replace(" ", ""), SearchHotkey.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Oba skróty klawiszowe są takie same.");
        }
        foreach (var folder in WatchedFolders.Where(f => !Directory.Exists(f)))
        {
            errors.Add($"Obserwowany folder nie istnieje: {folder}");
        }
        foreach (var rule in BuildRules())
        {
            if (Fences.All(f => f.Id != rule.TargetFenceId))
            {
                errors.Add($"Reguła „{rule.Name}” nie ma wybranej zagrody docelowej.");
            }
            else if (!rule.HasConditions)
            {
                errors.Add($"Reguła „{rule.Name}” nie ma żadnego warunku (rozszerzenia, wzorca nazwy, wieku ani rozmiaru) — nigdy niczego nie złapie.");
            }
        }
        return errors;
    }

    public AppSettings BuildSettings() => new()
    {
        StartWithWindows = StartWithWindows,
        UseBlurEffect = UseBlurEffect,
        SnapToGrid = SnapToGrid,
        GridSize = Math.Clamp(GridSize, 2, 100),
        SnapToFences = SnapToFences,
        FenceGap = Math.Clamp(FenceGap, 0, 64),
        DoubleClickDesktopToHide = DoubleClickDesktopToHide,
        PeekRolledUpOnHover = PeekRolledUpOnHover,
        ToggleHotkey = ToggleHotkey.Trim(),
        SearchHotkey = SearchHotkey.Trim(),
        AutoSortEnabled = AutoSortEnabled,
        AutoSortDelaySeconds = Math.Clamp(AutoSortDelaySeconds, 1, 3600),
        WatchedFolders = WatchedFolders.ToList(),
    };

    public void ShowPreview(IReadOnlyList<SortAction> plan, Func<Guid, string> fenceTitle)
    {
        if (plan.Count == 0)
        {
            PreviewText = "Żaden element w obserwowanych folderach nie pasuje do reguł.";
            return;
        }
        var lines = plan.Take(200).Select(a => a switch
        {
            MoveAction m => $"→ {Path.GetFileName(m.SourcePath)}  ⇒  {fenceTitle(m.FenceId)} (przeniesienie, reguła „{m.RuleName}”)",
            _ => $"→ {Path.GetFileName(a.SourcePath)}  ⇒  {fenceTitle(a.FenceId)} (przypięcie, reguła „{a.RuleName}”)",
        });
        var more = plan.Count > 200 ? $"\n… i {plan.Count - 200} więcej" : string.Empty;
        PreviewText = $"Pasujące elementy: {plan.Count}\n" + string.Join("\n", lines) + more;
    }
}
