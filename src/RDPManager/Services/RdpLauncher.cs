using System.Diagnostics;
using System.IO;
using System.Text;
using RDPManager.Models;
using RDPManager.Properties;

namespace RDPManager.Services;

/// <summary>Builds a temporary .rdp file, optionally injects credentials and starts mstsc.</summary>
public class RdpLauncher
{
    private static readonly string TempDirectory = Path.Combine(Path.GetTempPath(), "RDPManager");

    private readonly Func<AppSettings> _settings;
    private readonly SessionTracker _sessions;

    public RdpLauncher(Func<AppSettings> settings, SessionTracker sessions)
    {
        _settings = settings;
        _sessions = sessions;
    }

    /// <param name="credential">Credential to inject for the host (and gateway), or null to let mstsc decide.</param>
    public RdpSession Launch(Connection connection, bool admin, KeePassCredential? credential)
    {
        Directory.CreateDirectory(TempDirectory);
        var rdpPath = Path.Combine(TempDirectory, $"{Guid.NewGuid():N}.rdp");
        var userName = credential?.UserName ?? connection.Username;
        File.WriteAllText(rdpPath, BuildRdpFile(connection, admin, userName), Encoding.Unicode);

        var injected = new List<string>();
        try
        {
            if (credential != null)
            {
                foreach (var host in new[] { connection.Host, connection.GatewayHost }.Where(h => !string.IsNullOrWhiteSpace(h)))
                {
                    // The gateway credential target is the plain host name, the session one uses TERMSRV/.
                    var target = host == connection.Host ? CredentialInjector.TargetForHost(host) : host;
                    CredentialInjector.Write(target, credential.UserName, credential.Password);
                    injected.Add(target);
                }
            }

            var process = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "mstsc.exe"),
                Arguments = $"\"{rdpPath}\"",
                UseShellExecute = false,
            }) ?? throw new InvalidOperationException(Strings.Error_MstscStart);

            var session = new RdpSession(connection, process);
            _sessions.Add(session);
            _ = CleanupAsync(process, rdpPath, injected);
            return session;
        }
        catch
        {
            injected.ForEach(CredentialInjector.Release);
            TryDelete(rdpPath);
            throw;
        }
    }

    /// <summary>
    /// mstsc reads the saved credential during connection setup, so it is removed once the session
    /// had time to authenticate (or mstsc exited).
    /// </summary>
    private async Task CleanupAsync(Process process, string rdpPath, List<string> injected)
    {
        var timeout = TimeSpan.FromSeconds(Math.Max(10, _settings().CredentialCleanupSeconds));
        await Task.WhenAny(process.WaitForExitAsync(), Task.Delay(timeout));
        foreach (var target in injected)
        {
            try { CredentialInjector.Release(target); }
            catch (Exception ex) { Debug.WriteLine(ex); }
        }
        TryDelete(rdpPath);
    }

    public static void CleanupTempFiles()
    {
        if (!Directory.Exists(TempDirectory))
            return;
        foreach (var file in Directory.EnumerateFiles(TempDirectory, "*.rdp"))
            TryDelete(file);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static string BuildRdpFile(Connection c, bool admin, string? userName)
    {
        var s = new RdpSettings();
        s.Set("full address:s", c.Port == 3389 ? c.Host : $"{c.Host}:{c.Port}");
        s.Set("screen mode id:i", c.FullScreen ? "2" : "1");
        s.Set("use multimon:i", c.UseMultimon ? "1" : "0");
        if (c.UseMultimon && !string.IsNullOrWhiteSpace(c.SelectedMonitors))
            s.Set("selectedmonitors:s", c.SelectedMonitors.Trim());
        if (!c.FullScreen)
        {
            s.Set("desktopwidth:i", c.Width.ToString());
            s.Set("desktopheight:i", c.Height.ToString());
        }
        s.Set("dynamic resolution:i", "1");
        s.Set("session bpp:i", "32");
        s.Set("connection type:i", "7");
        s.Set("networkautodetect:i", "1");
        s.Set("bandwidthautodetect:i", "1");
        s.Set("displayconnectionbar:i", "1");
        s.Set("pinconnectionbar:i", "0");
        s.Set("autoreconnection enabled:i", "1");
        s.Set("authentication level:i", "2");
        s.Set("enablecredsspsupport:i", "1");
        s.Set("prompt for credentials:i", "0");
        if (!string.IsNullOrWhiteSpace(userName))
            s.Set("username:s", userName);
        if (admin || c.AdminSession)
            s.Set("administrative session:i", "1");

        s.Set("redirectclipboard:i", c.RedirectClipboard ? "1" : "0");
        s.Set("redirectprinters:i", c.RedirectPrinters ? "1" : "0");
        s.Set("drivestoredirect:s", c.RedirectDrives ? "*" : "");
        s.Set("audiomode:i", ((int)c.AudioMode).ToString());

        if (!string.IsNullOrWhiteSpace(c.GatewayHost))
        {
            s.Set("gatewayhostname:s", c.GatewayHost.Trim());
            s.Set("gatewayusagemethod:i", "1");
            s.Set("gatewayprofileusagemethod:i", "1");
            s.Set("gatewaycredentialssource:i", "4");
            s.Set("promptcredentialonce:i", "1");
        }
        else
        {
            s.Set("gatewayusagemethod:i", "0");
        }

        // Raw lines from the connection override the generated ones.
        foreach (var line in c.ExtraRdpSettings.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            s.SetLine(line);

        return s.ToString();
    }

    /// <summary>Ordered "name:type:value" lines keyed by "name:type".</summary>
    private sealed class RdpSettings
    {
        private readonly List<KeyValuePair<string, string>> _items = [];

        public void Set(string key, string value)
        {
            var i = _items.FindIndex(kv => kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (i >= 0)
                _items[i] = new(key, value);
            else
                _items.Add(new(key, value));
        }

        public void SetLine(string line)
        {
            var parts = line.Split(':', 3);
            if (parts.Length == 3)
                Set($"{parts[0]}:{parts[1]}", parts[2]);
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            foreach (var (key, value) in _items)
                sb.Append(key).Append(':').Append(value).Append("\r\n");
            return sb.ToString();
        }
    }
}
