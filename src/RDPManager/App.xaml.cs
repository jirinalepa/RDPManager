using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using RDPManager.Models;
using RDPManager.Properties;
using RDPManager.Services;
using RDPManager.Views;
using Forms = System.Windows.Forms;

namespace RDPManager;

public partial class App : Application
{
    private const string MutexName = @"Local\RDPManager.SingleInstance";
    private const string ShowEventName = @"Local\RDPManager.Show";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private AppController? _controller;
    private DockWindow? _dock;
    private Forms.NotifyIcon? _tray;
    private int _openMenus;

    /// <summary>Number of open context menus; the dock must not hide while one is open.</summary>
    public bool HasOpenMenus => _openMenus > 0;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, MutexName, out var isFirst);
        if (!isFirst)
        {
            // Another instance is running: ask it to show its panel and quit.
            if (EventWaitHandle.TryOpenExisting(ShowEventName, out var existing))
                existing.Set();
            Shutdown();
            return;
        }

        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            _ = Dialogs.ErrorAsync(Localization.Format(Strings.Error_Unexpected, args.Exception.Message));
        };
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent,
            new RoutedEventHandler((_, _) => _openMenus++));
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.ClosedEvent,
            new RoutedEventHandler((_, _) => _openMenus = Math.Max(0, _openMenus - 1)));

        try { CredentialInjector.CleanupLeftovers(); } catch (Exception) { }
        RdpLauncher.CleanupTempFiles();

        _controller = new AppController();
        Localization.Apply(_controller.Settings.Language);
        _controller.ApplyTheme();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        _dock = new DockWindow(_controller);
        MainWindow = _dock;
        _dock.Start();

        CreateTray();
        ListenForSecondInstance();

        if (_controller.Connections.Count == 0)
            _dock.ShowFromCommand();
        else
            _tray!.ShowBalloonTip(3000, "RDP Manager",
                Localization.Format(Strings.Tray_Balloon, _controller.Settings.Hotkey),
                Forms.ToolTipIcon.None);
    }

    private void CreateTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(Strings.Tray_ShowPanel, null, (_, _) => _dock!.ShowFromCommand());
        menu.Items.Add(Strings.Tray_NewConnection, null, (_, _) => _controller!.AddConnection());
        menu.Items.Add(Strings.Tray_Import, null, async (_, _) => await _controller!.ImportRdpFilesAsync());
        menu.Items.Add(Strings.Tray_Settings, null, (_, _) => _controller!.OpenSettings());
        var lockItem = new Forms.ToolStripMenuItem(Strings.Dock_LockKeePass_Tooltip, null, (_, _) => _controller!.KeePass.Lock());
        menu.Items.Add(lockItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Localization.Format(Strings.Tray_About, AppInfo.Version), null, (_, _) => AppInfo.OpenRepository());
        menu.Items.Add(Strings.Tray_Exit, null, (_, _) => ExitApp());
        menu.Opening += (_, _) => lockItem.Enabled = _controller!.KeePass.IsOpen;

        _tray = new Forms.NotifyIcon
        {
            Icon = AppIcon.Icon,
            Text = AppInfo.ProductWithVersion,
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.MouseClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left)
                _dock!.ShowFromCommand();
        };
    }

    private void ListenForSecondInstance()
    {
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var thread = new Thread(() =>
        {
            while (_showEvent.WaitOne())
                Dispatcher.BeginInvoke(() => _dock?.ShowFromCommand());
        })
        {
            IsBackground = true,
            Name = "SecondInstanceListener",
        };
        thread.Start();
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General && _controller?.Settings.Theme == AppTheme.System)
            Dispatcher.BeginInvoke(() => _controller.ApplyTheme());
    }

    public void ExitApp()
    {
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        _dock?.Dispose();
        _controller?.Dispose();
        Shutdown();
    }

    /// <summary>Starts a fresh instance (e.g. after a language change) and exits this one.</summary>
    public void RestartApp()
    {
        // Release single-instance ownership first so the new process does not just hand over to us.
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        _mutex = null;
        if (Environment.ProcessPath is { } exe)
            System.Diagnostics.Process.Start(exe);
        ExitApp();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
