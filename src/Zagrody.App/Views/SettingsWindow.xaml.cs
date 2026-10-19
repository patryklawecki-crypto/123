using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Zagrody.App.Interop;
using Zagrody.App.Services;
using Zagrody.App.ViewModels;

namespace Zagrody.App.Views;

public partial class SettingsWindow : Window
{
    private readonly FenceManager _manager;
    private readonly SettingsViewModel _vm;

    internal SettingsWindow(FenceManager manager)
    {
        InitializeComponent();
        _manager = manager;
        _vm = new SettingsViewModel(manager.Config);
        DataContext = _vm;
        VersionText.Text = $"Wersja {typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3)}";
        PathsText.Text = $"Ustawienia: {AppPaths.ConfigFile}\nDziennik błędów: {ErrorLog.Path}";

        // Cells hold always-editable controls, which take the click before the grid can select the row.
        RulesGrid.PreviewGotKeyboardFocus += (_, e) =>
        {
            if (e.NewFocus is DependencyObject focused && FindRow(focused) is { } row)
            {
                RulesGrid.SelectedItem = row.Item;
            }
        };
    }

    private static DataGridRow? FindRow(DependencyObject element)
    {
        var current = element;
        while (current is not null and not DataGridRow)
        {
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return current as DataGridRow;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var errors = _vm.Validate(MessageWindow.IsValidHotkey);
        if (errors.Count > 0)
        {
            MessageBox.Show(this, string.Join("\n\n", errors), "Popraw ustawienia", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var problems = _manager.ApplySettings(_vm.BuildSettings(), _vm.BuildRules());
        if (problems.Count > 0)
        {
            MessageBox.Show(this, "Zapisano, ale:\n\n" + string.Join("\n", problems), "Zagrody", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        Close();
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Folder, w którym sortować nowe elementy", InitialDirectory = AppPaths.Desktop };
        if (dialog.ShowDialog(this) == true)
        {
            _vm.AddFolder(dialog.FolderName);
        }
    }

    private void RemoveFolder_Click(object sender, RoutedEventArgs e) => _vm.RemoveSelectedFolder();

    private void AddRule_Click(object sender, RoutedEventArgs e) => _vm.AddRule(null);

    private void Preset_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PresetBox.SelectedItem is RulePreset preset)
        {
            _vm.AddRule(preset);
            PresetBox.SelectedItem = null;
        }
    }

    private void RemoveRule_Click(object sender, RoutedEventArgs e) => _vm.RemoveSelectedRule();

    private void MoveUp_Click(object sender, RoutedEventArgs e) => _vm.MoveSelectedRule(-1);

    private void MoveDown_Click(object sender, RoutedEventArgs e) => _vm.MoveSelectedRule(1);

    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        var plan = _manager.PreviewSort(_vm.BuildRules(), _vm.BuildSettings());
        _vm.ShowPreview(plan, id => _manager.Find(id)?.Title ?? "?");
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e) => ShellOps.Open(AppPaths.DataDirectory);
}
