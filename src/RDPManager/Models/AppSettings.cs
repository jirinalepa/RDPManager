namespace RDPManager.Models;

public enum DockEdge
{
    Left,
    Right,
    Top,
    Bottom,
}

public enum AppTheme
{
    System,
    Light,
    Dark,
}

public class AppSettings
{
    // Dock
    public DockEdge Edge { get; set; } = DockEdge.Left;
    /// <summary>Device name of the monitor (e.g. \\.\DISPLAY2); empty = primary.</summary>
    public string MonitorDeviceName { get; set; } = "";
    /// <summary>Panel width in DIPs.</summary>
    public int DockSize { get; set; } = 300;
    public int ShowDelayMs { get; set; } = 100;
    public int HideDelayMs { get; set; } = 700;
    public bool HideAfterConnect { get; set; } = true;
    public bool HotkeyEnabled { get; set; } = true;
    public string Hotkey { get; set; } = "Ctrl+Alt+R";

    // KeePass
    public string KeePassDatabasePath { get; set; } = "";
    public string KeePassKeyFilePath { get; set; } = "";
    public int KeePassAutoLockMinutes { get; set; } = 30;
    public bool KeePassLockOnWorkstationLock { get; set; } = true;

    // General
    public bool StartWithWindows { get; set; }
    public AppTheme Theme { get; set; } = AppTheme.System;
    /// <summary>How long injected credentials stay in Windows Credential Manager after mstsc starts.</summary>
    public int CredentialCleanupSeconds { get; set; } = 60;
}
