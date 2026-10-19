using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;
using Zagrody.App.Interop;
using Zagrody.App.Services;
using Zagrody.App.ViewModels;
using Zagrody.Core.Layout;
using Zagrody.Core.Models;

namespace Zagrody.App.Views;

public partial class FenceWindow : Window
{
    /// <summary>Clipboard format carrying the id of the fence a drag started in.</summary>
    public const string SourceFenceFormat = "Zagrody.SourceFence";

    private const double TitleHeight = 32;
    private const double ResizeBorder = 6;

    private readonly FenceViewModel _vm;
    private readonly FenceManager _manager;
    private readonly DispatcherTimer _peekCollapse;
    private bool _isPeeking;
    private bool _applyingBounds;
    private bool _allowClose;
    private bool _menuOpen;
    private Point? _dragStart;
    private ItemViewModel? _pressedItem;
    private bool _deferredSelect;
    private long _lastTitleClick;

    internal FenceWindow(FenceViewModel vm, FenceManager manager)
    {
        InitializeComponent();
        _vm = vm;
        _manager = manager;
        DataContext = vm;

        _peekCollapse = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _peekCollapse.Tick += (_, _) => TryEndPeek();

        _applyingBounds = true;
        Left = vm.Config.X;
        Top = vm.Config.Y;
        Width = vm.Config.Width;
        Height = vm.Config.Height;
        _applyingBounds = false;
        ApplyRollState();
        ApplyLockState();

        SourceInitialized += OnSourceInitialized;
        LocationChanged += (_, _) => StoreBounds();
        SizeChanged += (_, _) => StoreBounds();
        MouseEnter += (_, _) => BeginPeek();
        MouseLeave += (_, _) => _peekCollapse.Start();
        PreviewTextInput += OnPreviewTextInput;
        PreviewKeyDown += OnPreviewKeyDown;
        DragEnter += OnDragOver;
        DragOver += OnDragOver;
        DragLeave += OnDragLeave;
        Drop += OnDrop;
        _vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    internal FenceViewModel ViewModel => _vm;

    /// <summary>Closes for real; a plain Close() (e.g. Alt+F4 on a focused fence) is ignored.</summary>
    public void CloseForGood()
    {
        _allowClose = true;
        DesktopPinning.Release(this);
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = !_allowClose;
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _vm.PropertyChanged -= OnViewModelPropertyChanged;
        _peekCollapse.Stop();
        base.OnClosed(e);
    }

    public void ApplyBlur(bool enabled)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            WindowEffects.SetBlur(hwnd, enabled);
        }
    }

    public void BeginRename()
    {
        if (_vm.IsLocked)
        {
            return;
        }
        _vm.IsRenaming = true;
        RenameBox.Text = _vm.Title;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            Activate();
            RenameBox.Focus();
            RenameBox.SelectAll();
        });
    }

    /// <summary>Brings attention to the fence and selects an item, used by global search.</summary>
    public void Reveal(ItemViewModel? item)
    {
        _vm.IsRolledUp = false;
        _vm.SearchText = string.Empty;
        _vm.IsSearchVisible = false;
        Activate();
        if (item is not null && _vm.Items.FirstOrDefault(i => i.Path == item.Path) is { } match)
        {
            ItemsList.SelectedItem = match;
            ItemsList.ScrollIntoView(match);
            (ItemsList.ItemContainerGenerator.ContainerFromItem(match) as ListBoxItem)?.Focus();
        }
        Flash();
    }

    public void Flash()
    {
        _vm.IsDropTarget = true;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _vm.IsDropTarget = false;
        };
        timer.Start();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        DesktopPinning.Pin(this);
        ApplyBlur(_manager.Config.Settings.UseBlurEffect);
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle).AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg is NativeMethods.WM_MOVING or NativeMethods.WM_SIZING)
        {
            // The proposed rectangle (screen pixels) is adjusted in place while the user drags.
            var rect = Marshal.PtrToStructure<NativeMethods.RECT>(lParam);
            var proposed = new Bounds(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
            var snapped = msg == NativeMethods.WM_MOVING
                ? SnapMove(proposed)
                : SnapResize(proposed, ToEdges(wParam.ToInt32()));
            rect.Left = (int)Math.Round(snapped.X);
            rect.Top = (int)Math.Round(snapped.Y);
            rect.Right = (int)Math.Round(snapped.Right);
            rect.Bottom = (int)Math.Round(snapped.Bottom);
            Marshal.StructureToPtr(rect, lParam, false);
            handled = true;
            return new IntPtr(1);
        }
        return IntPtr.Zero;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(FenceViewModel.IsRolledUp):
                _isPeeking = false;
                ApplyRollState();
                break;
            case nameof(FenceViewModel.IsLocked):
                ApplyLockState();
                break;
            case nameof(FenceViewModel.IsSearchVisible):
                ApplyRollState();
                if (_vm.IsSearchVisible)
                {
                    Dispatcher.BeginInvoke(DispatcherPriority.Input, () => SearchBox.Focus());
                }
                break;
        }
    }

    // ---- Geometry ----------------------------------------------------------------------------

    private bool IsCollapsed => _vm.IsRolledUp && !_isPeeking;

    private void ApplyRollState()
    {
        var collapsed = IsCollapsed;
        _applyingBounds = true;
        ContentArea.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        TitleSeparator.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        SearchBox.Visibility = !collapsed && _vm.IsSearchVisible ? Visibility.Visible : Visibility.Collapsed;
        MinHeight = collapsed ? TitleHeight : FenceConfig.MinHeight;
        MaxHeight = collapsed ? TitleHeight : double.PositiveInfinity;
        Height = collapsed ? TitleHeight : _vm.Config.Height;
        RollButton.Content = _vm.IsRolledUp ? "" : "";
        _applyingBounds = false;
        ApplyLockState();
    }

    private void ApplyLockState()
    {
        if (WindowChrome.GetWindowChrome(this) is { } chrome)
        {
            chrome.ResizeBorderThickness = _vm.IsLocked
                ? new Thickness(0)
                : IsCollapsed ? new Thickness(ResizeBorder, 0, ResizeBorder, 0) : new Thickness(ResizeBorder);
        }
    }

    private void StoreBounds()
    {
        if (_applyingBounds || !IsLoaded)
        {
            return;
        }
        _vm.Config.X = Left;
        _vm.Config.Y = Top;
        _vm.Config.Width = ActualWidth;
        if (!IsCollapsed && !_vm.IsRolledUp)
        {
            _vm.Config.Height = ActualHeight;
        }
        _vm.RaiseChanged();
    }

    // ---- Magnetic snapping (all in screen pixels) -----------------------------------------------

    private const double SnapThreshold = 12;

    private double Scale => VisualTreeHelper.GetDpi(this).DpiScaleX;

    private SnapOptions SnapOptions()
    {
        var s = _manager.Config.Settings;
        var scale = Scale;
        return new SnapOptions(
            Gap: s.FenceGap * scale,
            Threshold: SnapThreshold * scale,
            Grid: s.SnapToGrid ? s.GridSize * scale : 0,
            SnapToNeighbours: s.SnapToFences);
    }

    private Bounds SnapMove(Bounds proposed) =>
        LayoutMath.SnapMove(proposed, _manager.OtherFenceRects(this), FenceManager.WorkAreasInPixels(), SnapOptions());

    private Bounds SnapResize(Bounds proposed, ResizeEdges edges) =>
        LayoutMath.SnapResize(proposed, edges, _manager.OtherFenceRects(this), FenceManager.WorkAreasInPixels(), SnapOptions(),
            MinWidth * Scale, MinHeight * Scale);

    /// <summary>Maps WM_SIZING's WMSZ_* codes to the edges being dragged.</summary>
    private static ResizeEdges ToEdges(int wmsz) => wmsz switch
    {
        1 => ResizeEdges.Left,
        2 => ResizeEdges.Right,
        3 => ResizeEdges.Top,
        4 => ResizeEdges.Top | ResizeEdges.Left,
        5 => ResizeEdges.Top | ResizeEdges.Right,
        6 => ResizeEdges.Bottom,
        7 => ResizeEdges.Bottom | ResizeEdges.Left,
        8 => ResizeEdges.Bottom | ResizeEdges.Right,
        _ => ResizeEdges.None,
    };

    // ---- Roll-up peeking -----------------------------------------------------------------------

    private void BeginPeek()
    {
        _peekCollapse.Stop();
        if (_vm.IsRolledUp && !_isPeeking && _manager.Config.Settings.PeekRolledUpOnHover)
        {
            _isPeeking = true;
            ApplyRollState();
        }
    }

    private void TryEndPeek()
    {
        _peekCollapse.Stop();
        if (!_isPeeking)
        {
            return;
        }
        if (IsMouseOver || _menuOpen || _vm.IsDropTarget || IsKeyboardFocusWithin && _vm.IsSearchVisible)
        {
            _peekCollapse.Start();
            return;
        }
        _isPeeking = false;
        ApplyRollState();
    }

    // ---- Title bar -----------------------------------------------------------------------------

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_vm.IsRenaming)
        {
            return;
        }
        // DragMove runs a modal move loop that swallows WPF's click counting, so double-clicks are timed by hand.
        var now = Environment.TickCount64;
        if (now - _lastTitleClick <= NativeMethods.GetDoubleClickTime())
        {
            _lastTitleClick = 0;
            _vm.IsRolledUp = !_vm.IsRolledUp;
            e.Handled = true;
            return;
        }
        _lastTitleClick = now;

        if (!_vm.IsLocked && e.ButtonState == MouseButtonState.Pressed)
        {
            var before = new Point(Left, Top);
            DragMove();
            if (before != new Point(Left, Top))
            {
                _lastTitleClick = 0; // a real drag is not the first half of a double-click
            }
        }
    }

    private void TitleBar_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        ShowMenu(BuildFenceMenu());
        e.Handled = true;
    }

    private void SearchButton_Click(object sender, RoutedEventArgs e) => ToggleSearch();

    private void LockButton_Click(object sender, RoutedEventArgs e) => _vm.IsLocked = !_vm.IsLocked;

    private void RollButton_Click(object sender, RoutedEventArgs e) => _vm.IsRolledUp = !_vm.IsRolledUp;

    private void MenuButton_Click(object sender, RoutedEventArgs e) => ShowMenu(BuildFenceMenu(), (UIElement)sender);

    private void RenameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            EndRename(commit: true);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            EndRename(commit: false);
            e.Handled = true;
        }
    }

    private void RenameBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => EndRename(commit: true);

    private void EndRename(bool commit)
    {
        if (!_vm.IsRenaming)
        {
            return;
        }
        if (commit)
        {
            _vm.Title = RenameBox.Text;
        }
        _vm.IsRenaming = false;
    }

    // ---- Keyboard and search -------------------------------------------------------------------

    private void ToggleSearch()
    {
        _vm.IsSearchVisible = !_vm.IsSearchVisible;
        if (_vm.IsSearchVisible)
        {
            _vm.IsRolledUp = false;
        }
    }

    private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        // Typing anywhere in a fence starts filtering it.
        if (Keyboard.FocusedElement is TextBox || string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0]))
        {
            return;
        }
        _vm.IsSearchVisible = true;
        SearchBox.Focus();
        SearchBox.Text += e.Text;
        SearchBox.CaretIndex = SearchBox.Text.Length;
        e.Handled = true;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (!_vm.IsSearchVisible)
            {
                ToggleSearch();
            }
            SearchBox.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.F2 && Keyboard.FocusedElement is not TextBox)
        {
            BeginRename();
            e.Handled = true;
        }
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                _vm.IsSearchVisible = false;
                ItemsList.Focus();
                e.Handled = true;
                break;
            case Key.Enter:
                if (_vm.View.Cast<ItemViewModel>().FirstOrDefault() is { } first)
                {
                    ShellOps.Open(first.Path);
                }
                e.Handled = true;
                break;
            case Key.Down:
                if (_vm.View.Cast<ItemViewModel>().FirstOrDefault() is { } item)
                {
                    ItemsList.SelectedItem = item;
                    (ItemsList.ItemContainerGenerator.ContainerFromItem(item) as ListBoxItem)?.Focus();
                }
                e.Handled = true;
                break;
        }
    }

    private void ItemsList_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                OpenSelected();
                e.Handled = true;
                break;
            case Key.Delete:
                RemoveOrRecycleSelected();
                e.Handled = true;
                break;
            case Key.Escape when _vm.IsSearchVisible:
                _vm.IsSearchVisible = false;
                e.Handled = true;
                break;
        }
    }

    // ---- Items: open, select, drag out ---------------------------------------------------------

    private List<ItemViewModel> SelectedItems => ItemsList.SelectedItems.Cast<ItemViewModel>().ToList();

    private void OpenSelected()
    {
        foreach (var item in SelectedItems.Take(15))
        {
            ShellOps.Open(item.Path);
        }
    }

    private void ItemsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemAt(e.OriginalSource) is { } item)
        {
            ShellOps.Open(item.Path);
            e.Handled = true;
        }
    }

    private void ItemsList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(ItemsList);
        _pressedItem = ItemAt(e.OriginalSource);
        _deferredSelect = false;

        // Pressing on an already selected item must keep the multi-selection, so it can be dragged as a group.
        if (_pressedItem is not null
            && e.ClickCount == 1
            && Keyboard.Modifiers == ModifierKeys.None
            && ItemsList.SelectedItems.Count > 1
            && ItemsList.SelectedItems.Contains(_pressedItem))
        {
            _deferredSelect = true;
            e.Handled = true;
        }
    }

    private void ItemsList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_deferredSelect && _pressedItem is not null)
        {
            ItemsList.SelectedItems.Clear();
            ItemsList.SelectedItem = _pressedItem;
        }
        _deferredSelect = false;
        _dragStart = null;
        _pressedItem = null;
    }

    private void ItemsList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start || _pressedItem is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }
        var delta = e.GetPosition(ItemsList) - start;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        if (!ItemsList.SelectedItems.Contains(_pressedItem))
        {
            ItemsList.SelectedItems.Clear();
            ItemsList.SelectedItem = _pressedItem;
        }
        _dragStart = null;
        _deferredSelect = false;
        StartDragOut();
    }

    private void StartDragOut()
    {
        var paths = SelectedItems.Where(i => !i.IsMissing).Select(i => i.Path).ToArray();
        if (paths.Length == 0)
        {
            return;
        }

        var data = new DataObject();
        data.SetData(DataFormats.FileDrop, paths);
        data.SetData(SourceFenceFormat, _vm.Id.ToString());
        DragDrop.DoDragDrop(ItemsList, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);

        // If the drop target (e.g. Explorer) moved pinned files away, the references are stale now.
        if (!_vm.IsFolder)
        {
            _vm.RemovePaths(paths.Where(p => !File.Exists(p) && !Directory.Exists(p)));
        }
    }

    private static ListBoxItem? ContainerAt(object? source)
    {
        var current = source as DependencyObject;
        while (current is not null and not ListBoxItem)
        {
            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }
        return current as ListBoxItem;
    }

    private static ItemViewModel? ItemAt(object? source) => ContainerAt(source)?.DataContext as ItemViewModel;

    // ---- Drop target ---------------------------------------------------------------------------

    private static Guid? SourceFenceId(IDataObject data) =>
        data.GetDataPresent(SourceFenceFormat) && Guid.TryParse(data.GetData(SourceFenceFormat) as string, out var id) ? id : null;

    private DragDropEffects ComputeEffect(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths)
        {
            return DragDropEffects.None;
        }

        var sourceId = SourceFenceId(e.Data);
        var source = sourceId is { } id ? _manager.Find(id) : null;
        var ctrl = (e.KeyStates & DragDropKeyStates.ControlKey) != 0;
        var shift = (e.KeyStates & DragDropKeyStates.ShiftKey) != 0;

        DragDropEffects wanted;
        if (sourceId == _vm.Id)
        {
            // Reordering changes the fence's arrangement, which a lock freezes.
            wanted = _vm.IsFolder || _vm.IsLocked ? DragDropEffects.None : DragDropEffects.Move;
        }
        else if (_vm.IsFolder)
        {
            if (!_vm.FolderExists)
            {
                return DragDropEffects.None;
            }
            wanted = ctrl ? DragDropEffects.Copy
                : shift ? DragDropEffects.Move
                : ShellOps.IsSameVolume(paths[0], _vm.FolderPath!) ? DragDropEffects.Move : DragDropEffects.Copy;
        }
        else if (source is { IsFolder: false })
        {
            wanted = ctrl ? DragDropEffects.Copy : DragDropEffects.Move; // between collections: move the reference
        }
        else
        {
            wanted = DragDropEffects.Link; // pin, the file stays where it is
        }

        if ((e.AllowedEffects & wanted) != 0)
        {
            return wanted;
        }
        // Some sources only offer Copy; pinning works the same either way.
        return !_vm.IsFolder && (e.AllowedEffects & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy : e.AllowedEffects & wanted;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        BeginPeek();
        e.Effects = ComputeEffect(e);
        _vm.IsDropTarget = e.Effects != DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        _vm.IsDropTarget = false;
        _peekCollapse.Start();
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        _vm.IsDropTarget = false;
        var effect = ComputeEffect(e);
        if (effect == DragDropEffects.None || e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        var sourceId = SourceFenceId(e.Data);
        if (sourceId == _vm.Id)
        {
            _vm.EnsureManualOrder();
        }
        var index = DropIndex(e);

        // Report Copy back for moves: we move the files ourselves, and a Move result could make the
        // source (Explorer) delete what it thinks is a leftover original.
        e.Effects = effect == DragDropEffects.Move ? DragDropEffects.Copy : effect;
        e.Handled = true;

        // Run after the drag loop ends, so shell dialogs don't block the drag source.
        Dispatcher.BeginInvoke(() => _manager.HandleDrop(_vm, paths, sourceId, effect, index));
    }

    /// <summary>Position in a manually ordered collection where dropped items go, or null to append.</summary>
    private int? DropIndex(DragEventArgs e)
    {
        if (_vm.IsFolder || _vm.SortMode != ItemSortMode.Manual)
        {
            return null;
        }
        var position = e.GetPosition(ItemsList);
        var hit = ItemsList.InputHitTest(position) as DependencyObject;
        if (ContainerAt(hit) is not { DataContext: ItemViewModel item } container)
        {
            return null;
        }
        var index = _vm.IndexOfPath(item.Path);
        if (index < 0)
        {
            return null;
        }
        var local = e.GetPosition(container);
        return local.X > container.ActualWidth / 2 ? index + 1 : index;
    }

    // ---- Menus ---------------------------------------------------------------------------------

    private void ItemsList_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var item = ItemAt(e.OriginalSource);
        if (item is null)
        {
            ShowMenu(BuildFenceMenu());
        }
        else
        {
            if (!ItemsList.SelectedItems.Contains(item))
            {
                ItemsList.SelectedItems.Clear();
                ItemsList.SelectedItem = item;
            }
            ShowMenu(BuildItemMenu(SelectedItems));
        }
        e.Handled = true;
    }

    private void ShowMenu(ContextMenu menu, UIElement? anchor = null)
    {
        menu.PlacementTarget = anchor ?? this;
        menu.Placement = anchor is null ? System.Windows.Controls.Primitives.PlacementMode.MousePoint : System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.Opened += (_, _) => _menuOpen = true;
        menu.Closed += (_, _) =>
        {
            _menuOpen = false;
            _peekCollapse.Start();
        };
        menu.IsOpen = true;
    }

    private ContextMenu BuildItemMenu(IReadOnlyList<ItemViewModel> items)
    {
        var menu = new ContextMenu();
        var first = items[0];
        var existing = items.Where(i => !i.IsMissing).ToList();

        menu.Items.Add(MenuItem("Otwórz", OpenSelected, bold: true, enabled: existing.Count > 0));
        menu.Items.Add(MenuItem("Pokaż w Eksploratorze", () => ShellOps.ShowInExplorer(first.Path)));
        menu.Items.Add(MenuItem("Kopiuj ścieżkę", () => Clipboard.SetText(string.Join(Environment.NewLine, items.Select(i => i.Path)))));

        var otherCollections = _manager.Fences.Where(f => f != _vm && !f.IsFolder).ToList();
        if (otherCollections.Count > 0 && existing.Count > 0)
        {
            var pin = new MenuItem { Header = "Przypnij też do" };
            foreach (var fence in otherCollections)
            {
                pin.Items.Add(MenuItem(fence.Title, () => fence.AddPaths(existing.Select(i => i.Path))));
            }
            menu.Items.Add(pin);
        }

        menu.Items.Add(new Separator());
        if (_vm.IsFolder)
        {
            menu.Items.Add(MenuItem("Przenieś do kosza", RemoveOrRecycleSelected, enabled: existing.Count > 0));
        }
        else
        {
            menu.Items.Add(MenuItem(items.Count > 1 ? $"Usuń z zagrody ({items.Count})" : "Usuń z zagrody", RemoveOrRecycleSelected));
            if (_vm.Items.Any(i => i.IsMissing))
            {
                menu.Items.Add(MenuItem("Usuń brakujące elementy", _vm.RemoveMissing));
            }
        }
        return menu;
    }

    private void RemoveOrRecycleSelected()
    {
        var items = SelectedItems;
        if (items.Count == 0)
        {
            return;
        }
        if (!_vm.IsFolder)
        {
            // Only the reference goes away; the file itself is untouched.
            _vm.RemovePaths(items.Select(i => i.Path));
            return;
        }

        var question = items.Count == 1
            ? $"Przenieść „{items[0].DisplayName}” do kosza?"
            : $"Przenieść {items.Count} elementy do kosza?";
        if (MessageBox.Show(question, "Zagrody", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            ShellOps.Recycle(items.Select(i => i.Path));
            _vm.Reload();
        }
    }

    private static readonly string[] PaletteNames =
    {
        "Niebieski", "Fioletowy", "Różowy", "Czerwony", "Pomarańczowy", "Zielony", "Turkusowy", "Grafitowy",
    };

    private ContextMenu BuildFenceMenu()
    {
        // A locked fence keeps its place, size and settings; only viewing actions stay available.
        var editable = !_vm.IsLocked;
        var menu = new ContextMenu();
        menu.Items.Add(MenuItem(_vm.IsLocked ? "Odblokuj zagrodę" : "Zablokuj zagrodę (położenie, rozmiar i ustawienia)",
            () => _vm.IsLocked = !_vm.IsLocked, isChecked: _vm.IsLocked));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("Zmień nazwę", BeginRename, enabled: editable, gesture: "F2"));

        var colors = new MenuItem { Header = "Kolor", IsEnabled = editable };
        foreach (var (hex, name) in FenceViewModel.Palette.Zip(PaletteNames))
        {
            var swatch = new Border
            {
                Width = 14,
                Height = 14,
                CornerRadius = new CornerRadius(3),
                Background = (Brush)new BrushConverter().ConvertFromInvariantString(hex)!,
            };
            var item = MenuItem(name, () => _vm.AccentColor = hex, isChecked: string.Equals(_vm.AccentColor, hex, StringComparison.OrdinalIgnoreCase));
            item.Icon = swatch;
            colors.Items.Add(item);
        }
        colors.Items.Add(new Separator());
        foreach (var (label, opacity) in new[] { ("Tło: przezroczyste", 0.25), ("Tło: średnie", 0.55), ("Tło: pełne", 0.85) })
        {
            colors.Items.Add(MenuItem(label, () => _vm.BackgroundOpacity = opacity, isChecked: Math.Abs(_vm.BackgroundOpacity - opacity) < 0.01));
        }
        menu.Items.Add(colors);

        var icons = new MenuItem { Header = "Rozmiar ikon", IsEnabled = editable };
        foreach (var (label, size) in new[] { ("Małe", 32), ("Średnie", 48), ("Duże", 64), ("Bardzo duże", 96) })
        {
            icons.Items.Add(MenuItem(label, () => _vm.IconSize = size, isChecked: _vm.IconSize == size));
        }
        menu.Items.Add(icons);

        var sort = new MenuItem { Header = "Sortuj", IsEnabled = editable };
        var modes = new List<(string, ItemSortMode)>
        {
            ("Według nazwy", ItemSortMode.Name),
            ("Według typu", ItemSortMode.Type),
            ("Według daty modyfikacji", ItemSortMode.DateModified),
            ("Według rozmiaru", ItemSortMode.Size),
        };
        if (!_vm.IsFolder)
        {
            modes.Insert(0, ("Ręcznie (przeciągnij, aby ułożyć)", ItemSortMode.Manual));
        }
        foreach (var (label, mode) in modes)
        {
            sort.Items.Add(MenuItem(label, () => _vm.SortMode = mode, isChecked: _vm.SortMode == mode));
        }
        sort.Items.Add(new Separator());
        sort.Items.Add(MenuItem("Malejąco", () => _vm.SortDescending = !_vm.SortDescending, isChecked: _vm.SortDescending));
        menu.Items.Add(sort);

        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem(_vm.IsRolledUp ? "Rozwiń" : "Zwiń", () => _vm.IsRolledUp = !_vm.IsRolledUp));

        if (_vm.IsFolder)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItem("Otwórz folder", () => ShellOps.Open(_vm.FolderPath!), enabled: _vm.FolderExists));
            menu.Items.Add(MenuItem("Zmień folder…", ChooseFolder, enabled: editable));
            menu.Items.Add(MenuItem("Pokazuj ukryte pliki", () => _vm.ShowHiddenFiles = !_vm.ShowHiddenFiles, isChecked: _vm.ShowHiddenFiles, enabled: editable));
        }
        else
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItem("Pokazuj zawartość folderu…", ChooseFolder, enabled: editable));
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("Nowa zagroda", () => _manager.CreateFence(FenceMode.Collection, null)));
        menu.Items.Add(MenuItem("Usuń tę zagrodę…", () => _manager.DeleteFence(_vm), enabled: editable));
        return menu;
    }

    private void ChooseFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Wybierz folder, który pokaże zagroda" };
        if (_vm.FolderExists)
        {
            dialog.InitialDirectory = _vm.FolderPath;
        }
        // No owner: a dialog owned by a window pinned to the bottom could end up behind other windows.
        if (dialog.ShowDialog() != true)
        {
            return;
        }
        if (!_vm.IsFolder && _vm.Config.Items.Count > 0
            && MessageBox.Show("Ta zagroda pokaże teraz zawartość folderu, a przypięte elementy zostaną z niej usunięte (same pliki pozostaną na miejscu). Kontynuować?",
                "Zagrody", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }
        _vm.Config.Items.Clear();
        _vm.SetFolder(dialog.FolderName);
        if (_vm.Title == new FenceConfig().Title)
        {
            _vm.Title = Path.GetFileName(dialog.FolderName.TrimEnd('\\')) is { Length: > 0 } name ? name : dialog.FolderName;
        }
    }

    private static MenuItem MenuItem(string header, Action action, bool? isChecked = null, bool enabled = true, bool bold = false, string? gesture = null)
    {
        var item = new MenuItem
        {
            Header = header,
            IsEnabled = enabled,
            IsCheckable = false,
            IsChecked = isChecked ?? false,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            InputGestureText = gesture ?? string.Empty,
        };
        item.Click += (_, _) => action();
        return item;
    }
}
