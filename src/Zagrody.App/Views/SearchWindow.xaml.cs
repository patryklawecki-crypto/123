using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Zagrody.App.Interop;
using Zagrody.App.Services;
using Zagrody.App.ViewModels;
using Zagrody.Core.Search;

namespace Zagrody.App.Views;

public sealed record SearchResult(ItemViewModel Item, FenceViewModel Fence, int Score);

/// <summary>Spotlight-style launcher over the contents of every fence.</summary>
public partial class SearchWindow : Window
{
    private const int MaxResults = 60;
    private readonly FenceManager _manager;
    private bool _allowClose;

    internal SearchWindow(FenceManager manager)
    {
        InitializeComponent();
        _manager = manager;
        Deactivated += (_, _) => Hide();
    }

    /// <summary>Shows the launcher, or hides it when the hotkey is pressed again.</summary>
    public void ShowAndFocus()
    {
        if (IsVisible)
        {
            Hide();
            return;
        }
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + area.Height * 0.18;
        QueryBox.Text = string.Empty;
        UpdateResults();
        Show();
        Activate();
        NativeMethods.SetForegroundWindow(new WindowInteropHelper(this).Handle);
        QueryBox.Focus();
    }

    public void CloseForGood()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }

    private void UpdateResults()
    {
        var query = QueryBox.Text;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = _manager.Fences
            .SelectMany(fence => fence.Items.Select(item => (item, fence)))
            .Where(x => !x.item.IsMissing)
            .Select(x => FuzzyMatcher.Score(query, x.item.DisplayName) is { } score ? new SearchResult(x.item, x.fence, score) : null)
            .OfType<SearchResult>()
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Where(r => seen.Add(r.Item.Path)) // the same file can be pinned to several fences
            .Take(MaxResults)
            .ToList();

        Results.ItemsSource = results;
        Results.SelectedIndex = results.Count > 0 ? 0 : -1;
        Results.Visibility = results.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyHint.Visibility = results.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        EmptyHint.Text = _manager.Fences.Any(f => f.Items.Count > 0) ? "Brak wyników" : "Zagrody są puste — przeciągnij do nich pliki.";
    }

    private void QueryBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        Placeholder.Visibility = QueryBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateResults();
    }

    private void QueryBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var count = Results.Items.Count;
        switch (e.Key)
        {
            case Key.Escape:
                Hide();
                e.Handled = true;
                break;
            case Key.Down when count > 0:
                Results.SelectedIndex = (Results.SelectedIndex + 1) % count;
                Results.ScrollIntoView(Results.SelectedItem);
                e.Handled = true;
                break;
            case Key.Up when count > 0:
                Results.SelectedIndex = (Results.SelectedIndex - 1 + count) % count;
                Results.ScrollIntoView(Results.SelectedItem);
                e.Handled = true;
                break;
            case Key.Enter:
            case Key.System when e.SystemKey == Key.Enter: // Alt+Enter arrives as a system key
                Launch(Results.SelectedItem as SearchResult, Keyboard.Modifiers);
                e.Handled = true;
                break;
        }
    }

    private void Results_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        Launch(Results.SelectedItem as SearchResult, Keyboard.Modifiers);

    private void Launch(SearchResult? result, ModifierKeys modifiers)
    {
        if (result is null)
        {
            return;
        }
        Hide();
        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            _manager.Reveal(result.Fence, result.Item);
        }
        else if (modifiers.HasFlag(ModifierKeys.Control))
        {
            ShellOps.ShowInExplorer(result.Item.Path);
        }
        else
        {
            ShellOps.Open(result.Item.Path);
        }
    }
}
