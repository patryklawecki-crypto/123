using System.Drawing;
using System.Drawing.Drawing2D;
using Microsoft.Win32;
using Zagrody.App.Interop;
using Zagrody.Core.Models;
using Forms = System.Windows.Forms;

namespace Zagrody.App.Services;

/// <summary>Notification-area icon: the app's only permanent entry point besides the fences themselves.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly FenceManager _manager;
    private readonly Action _exit;
    private readonly Forms.NotifyIcon _icon;
    private readonly Icon _image;
    private readonly Forms.ToolStripMenuItem _search;
    private readonly Forms.ToolStripMenuItem _toggle;
    private readonly Forms.ToolStripMenuItem _desktopIcons;
    private readonly Forms.ToolStripMenuItem _lockAll;
    private readonly Forms.ToolStripMenuItem _undo;

    public TrayIcon(FenceManager manager, Action exit)
    {
        _manager = manager;
        _exit = exit;
        _image = CreateIcon();

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Nowa zagroda", null, (_, _) => _manager.CreateFence(FenceMode.Collection, null));
        menu.Items.Add("Nowa zagroda z folderu…", null, (_, _) => NewFolderFence());
        menu.Items.Add(new Forms.ToolStripSeparator());
        _search = new Forms.ToolStripMenuItem("Szukaj…", null, (_, _) => _manager.ShowSearch());
        _toggle = new Forms.ToolStripMenuItem("Pokaż / ukryj zagrody", null, (_, _) => _manager.ToggleVisibility());
        _desktopIcons = new Forms.ToolStripMenuItem("Ukryj ikony pulpitu", null, (_, _) => DesktopIcons.SetVisible(!DesktopIcons.IsVisible));
        _lockAll = new Forms.ToolStripMenuItem("Zablokuj wszystkie zagrody", null, (_, _) => _manager.AllLocked = !_manager.AllLocked);
        menu.Items.AddRange(new Forms.ToolStripItem[] { _search, _toggle, _desktopIcons, _lockAll });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Posortuj teraz", null, (_, _) => _manager.SortNow());
        _undo = new Forms.ToolStripMenuItem("Cofnij ostatnie sortowanie", null, (_, _) => _manager.UndoSort());
        menu.Items.Add(_undo);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Ustawienia…", null, (_, _) => _manager.ShowSettings());
        menu.Items.Add("Zakończ", null, (_, _) => _exit());
        menu.Opening += (_, _) => RefreshMenu();

        _icon = new Forms.NotifyIcon
        {
            Icon = _image,
            Text = "Zagrody",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                _manager.ToggleVisibility();
            }
        };
        _manager.Notification += (_, message) => ShowMessage(message);
    }

    public void ShowMessage(string message) =>
        _icon.ShowBalloonTip(4000, "Zagrody", message, Forms.ToolTipIcon.None);

    private void RefreshMenu()
    {
        var settings = _manager.Config.Settings;
        _search.ShortcutKeyDisplayString = settings.SearchHotkey;
        _toggle.ShortcutKeyDisplayString = settings.ToggleHotkey;
        _toggle.Checked = !_manager.FencesVisible;
        _desktopIcons.Checked = !DesktopIcons.IsVisible;
        _lockAll.Checked = _manager.AllLocked;
        _undo.Enabled = _manager.CanUndoSort;
    }

    private void NewFolderFence()
    {
        var dialog = new OpenFolderDialog { Title = "Wybierz folder, który pokaże nowa zagroda" };
        if (dialog.ShowDialog() == true)
        {
            _manager.CreateFence(FenceMode.Folder, dialog.FolderName);
        }
    }

    /// <summary>Draws the icon at runtime (four small "fences" on a blue tile) so no binary asset is needed.</summary>
    private static Icon CreateIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var background = RoundedRect(new RectangleF(1, 1, 30, 30), 7);
            using var blue = new SolidBrush(Color.FromArgb(59, 130, 246));
            g.FillPath(blue, background);
            using var white = new SolidBrush(Color.FromArgb(235, 255, 255, 255));
            foreach (var (x, y, w, h) in new[] { (6f, 6f, 9f, 11f), (17f, 6f, 9f, 7f), (6f, 19f, 9f, 7f), (17f, 15f, 9f, 11f) })
            {
                using var tile = RoundedRect(new RectangleF(x, y, w, h), 2);
                g.FillPath(white, tile);
            }
        }
        var handle = bitmap.GetHicon();
        using var temp = Icon.FromHandle(handle);
        var icon = (Icon)temp.Clone(); // the clone owns its own handle, so the original can be destroyed
        NativeMethods.DestroyIcon(handle);
        return icon;
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _image.Dispose();
    }
}
