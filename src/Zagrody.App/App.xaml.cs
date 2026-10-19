using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using Zagrody.App.Services;
using Zagrody.Core.Storage;

namespace Zagrody.App;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private FenceManager? _manager;
    private TrayIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, $"Zagrody.SingleInstance.{Environment.UserName}", out var isFirst);
        if (!isFirst)
        {
            MessageBox.Show("Zagrody już działają — znajdziesz je w zasobniku obok zegara.", "Zagrody", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // WPF bindings default to en-US; use the system culture so "1,5" parses as a number in Polish.
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                ErrorLog.Write(ex, "Nieobsłużony wyjątek");
            }
        };

        var firstRun = !File.Exists(AppPaths.ConfigFile);
        var store = new ConfigStore(AppPaths.ConfigFile);
        var config = store.Load();

        _manager = new FenceManager(store, config);
        _tray = new TrayIcon(_manager, ExitApp);
        _manager.Start();

        if (firstRun)
        {
            _tray.ShowMessage($"Zagrody działają w zasobniku. Prawy przycisk na ikonie dodaje zagrody, {config.Settings.SearchHotkey} otwiera wyszukiwarkę.");
        }
        if (store.LoadWarning is { } warning)
        {
            MessageBox.Show(warning, "Zagrody", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ExitApp()
    {
        _manager?.Dispose();
        _manager = null;
        _tray?.Dispose();
        _tray = null;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Also reached on Windows logoff/shutdown, where ExitApp() was never called.
        _manager?.Dispose();
        _tray?.Dispose();
        if (_singleInstance is not null)
        {
            try
            {
                _singleInstance.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Not owned (second instance that is shutting down right away).
            }
            _singleInstance.Dispose();
        }
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ErrorLog.Write(e.Exception, "Błąd w interfejsie");
        MessageBox.Show($"Wystąpił nieoczekiwany błąd:\n\n{e.Exception.Message}\n\nSzczegóły zapisano w:\n{ErrorLog.Path}",
            "Zagrody", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
