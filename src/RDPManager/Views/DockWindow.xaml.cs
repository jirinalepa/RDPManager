using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using RDPManager.Docking;
using RDPManager.Models;
using RDPManager.Properties;
using RDPManager.Services;
using RDPManager.ViewModels;

namespace RDPManager.Views;

public partial class DockWindow : Window, IDisposable
{
    private const int HotkeyId = 0x5244;

    private readonly AppController _controller;
    private readonly DockViewModel _vm;
    private readonly EdgeDockController _dock;
    private HwndSource? _source;
    private bool _hotkeyRegistered;

    public DockWindow(AppController controller)
    {
        InitializeComponent();
        _controller = controller;
        _vm = new DockViewModel(controller);
        DataContext = _vm;
        LogoImage.Source = AppIcon.ImageSource;
        Icon = AppIcon.ImageSource;

        _dock = new EdgeDockController(this, Panel, SlideTransform, () => _controller.Settings)
        {
            IsBusy = IsBusy,
        };
        _controller.SettingsChanged += (_, _) =>
        {
            RegisterHotkey();
            _dock.Refresh();
        };
        _controller.Connected += (_, _) =>
        {
            if (_controller.Settings.HideAfterConnect && !_vm.IsPinned)
                _dock.Hide();
        };
    }

    public void Start()
    {
        _dock.Start();
        _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _source.AddHook(WndProc);
        RegisterHotkey();
    }

    /// <summary>Shows the panel with keyboard focus in the search box (hotkey, tray, second instance).</summary>
    public void ShowFromCommand()
    {
        _dock.Show(activate: true);
        FocusSearch();
    }

    private bool IsBusy() =>
        ((App)Application.Current).HasOpenMenus ||
        Application.Current.Windows.OfType<Window>().Any(w => w != this && w.IsVisible);

    private void FocusSearch()
    {
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
        SearchBox.SelectAll();
    }

    private void RegisterHotkey()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (_hotkeyRegistered)
        {
            NativeMethods.UnregisterHotKey(hwnd, HotkeyId);
            _hotkeyRegistered = false;
        }
        if (!_controller.Settings.HotkeyEnabled)
            return;
        var gesture = HotkeyGesture.Parse(_controller.Settings.Hotkey);
        if (gesture == null)
            return;
        _hotkeyRegistered = NativeMethods.RegisterHotKey(hwnd, HotkeyId, gesture.NativeModifiers, gesture.VirtualKey);
        if (!_hotkeyRegistered)
            _ = Dialogs.ErrorAsync(Localization.Format(Strings.Dock_HotkeyFailed, gesture));
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            if (_dock.IsShown && IsActive)
                _dock.Hide();
            else
                ShowFromCommand();
        }
        return IntPtr.Zero;
    }

    private Connection? SelectedOrFirst() =>
        ConnectionList.SelectedItem as Connection ?? _vm.ConnectionsView.Cast<Connection>().FirstOrDefault();

    private static Connection? ConnectionOf(object sender) => (sender as FrameworkElement)?.DataContext as Connection;

    private async Task ConnectAsync(Connection? connection, bool admin = false)
    {
        if (connection != null)
            await _controller.ConnectAsync(connection, admin);
    }

    // ---- Keyboard ----

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _vm.SearchText = "";
            _dock.Hide();
            e.Handled = true;
        }
        else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            FocusSearch();
            e.Handled = true;
        }
    }

    private async void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await ConnectAsync(SelectedOrFirst());
        }
        else if (e.Key == Key.Down && !ConnectionList.Items.IsEmpty)
        {
            e.Handled = true;
            if (ConnectionList.SelectedIndex < 0)
                ConnectionList.SelectedIndex = 0;
            ConnectionList.UpdateLayout();
            (ConnectionList.ItemContainerGenerator.ContainerFromItem(ConnectionList.SelectedItem) as UIElement)?.Focus();
        }
    }

    private async void ConnectionList_KeyDown(object sender, KeyEventArgs e)
    {
        if (ConnectionList.SelectedItem is not Connection c)
            return;
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                await ConnectAsync(c, admin: Keyboard.Modifiers == ModifierKeys.Shift);
                break;
            case Key.F2:
                e.Handled = true;
                _controller.EditConnection(c);
                break;
            case Key.Delete:
                e.Handled = true;
                await _controller.DeleteConnectionAsync(c);
                break;
        }
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        // Shown by hotkey/tray and the user clicked elsewhere without visiting the panel.
        if (_dock.AwaitingMouseEnter && !_vm.IsPinned && !IsBusy())
            _dock.Hide();
    }

    // ---- Mouse / menu actions ----

    private async void Item_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        await ConnectAsync(ConnectionOf(sender));
    }

    private async void Connect_Click(object sender, RoutedEventArgs e) => await ConnectAsync(ConnectionOf(sender));

    private async void ConnectAdmin_Click(object sender, RoutedEventArgs e) => await ConnectAsync(ConnectionOf(sender), admin: true);

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (ConnectionOf(sender) is { } c)
            _controller.EditConnection(c);
    }

    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (ConnectionOf(sender) is { } c)
            _controller.DuplicateConnection(c);
    }

    private void CopyHost_Click(object sender, RoutedEventArgs e)
    {
        if (ConnectionOf(sender) is { } c)
            AppController.CopyToClipboard(c.Host);
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (ConnectionOf(sender) is { } c)
            await _controller.DeleteConnectionAsync(c);
    }

    private void Add_Click(object sender, RoutedEventArgs e) => _controller.AddConnection();

    private async void Import_Click(object sender, RoutedEventArgs e) => await _controller.ImportRdpFilesAsync();

    private void Settings_Click(object sender, RoutedEventArgs e) => _controller.OpenSettings();

    private void LockKeePass_Click(object sender, RoutedEventArgs e) => _controller.KeePass.Lock();

    private void Pin_Changed(object sender, RoutedEventArgs e) => _dock.IsPinned = _vm.IsPinned;

    private void Session_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is RdpSession s)
        {
            _controller.Sessions.Activate(s);
            if (!_vm.IsPinned)
                _dock.Hide();
        }
    }

    private void SessionClose_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is RdpSession s)
            _controller.Sessions.Close(s);
        e.Handled = true;
    }

    public void Dispose()
    {
        if (_hotkeyRegistered)
            NativeMethods.UnregisterHotKey(new WindowInteropHelper(this).Handle, HotkeyId);
        _source?.RemoveHook(WndProc);
        _dock.Dispose();
    }
}
