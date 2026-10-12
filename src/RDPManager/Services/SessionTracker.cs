using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using RDPManager.Docking;
using RDPManager.Models;

namespace RDPManager.Services;

public class RdpSession
{
    public RdpSession(Connection connection, Process process)
    {
        Connection = connection;
        Process = process;
    }

    public Connection Connection { get; }
    public Process Process { get; }
    public DateTime StartedAt { get; } = DateTime.Now;
    public string StartedAtText => StartedAt.ToString("HH:mm");
}

/// <summary>Keeps the list of mstsc processes started by this app.</summary>
public class SessionTracker
{
    public ObservableCollection<RdpSession> Sessions { get; } = [];

    public void Add(RdpSession session)
    {
        Sessions.Add(session);
        session.Process.EnableRaisingEvents = true;
        session.Process.Exited += (_, _) =>
            Application.Current.Dispatcher.BeginInvoke(() => Sessions.Remove(session));
        if (session.Process.HasExited)
            Sessions.Remove(session);
    }

    public bool IsActive(Connection connection) => Sessions.Any(s => s.Connection.Id == connection.Id);

    public void Activate(RdpSession session)
    {
        session.Process.Refresh();
        var hwnd = session.Process.MainWindowHandle;
        if (hwnd == IntPtr.Zero)
            return;
        if (NativeMethods.IsIconic(hwnd))
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
        NativeMethods.SetForegroundWindow(hwnd);
    }

    /// <summary>Asks mstsc to close; it shows its own disconnect confirmation.</summary>
    public void Close(RdpSession session)
    {
        session.Process.Refresh();
        if (!session.Process.HasExited)
            session.Process.CloseMainWindow();
    }
}
