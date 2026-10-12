using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using KeePassLib;
using KeePassLib.Keys;
using KeePassLib.Serialization;
using KeePassLib.Utility;
using Microsoft.Win32;
using RDPManager.Models;
using RDPManager.Properties;

namespace RDPManager.Services;

public record KeePassEntryInfo(string Uuid, string Title, string UserName, string Url, string GroupPath);

public record KeePassCredential(string UserName, string Password);

/// <summary>
/// Read-only access to a KeePass 2.x database. The master key lives only in memory (protected by
/// KeePassLib) and is dropped on lock, on idle timeout and when Windows is locked.
/// </summary>
public sealed partial class KeePassService : IDisposable
{
    private readonly Func<AppSettings> _settings;
    private readonly DispatcherTimer _idleTimer;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private PwDatabase? _db;
    private DateTime _loadedFileTime;
    private DateTime _lastUse;

    public KeePassService(Func<AppSettings> settings)
    {
        _settings = settings;
        _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _idleTimer.Tick += (_, _) => CheckIdle();
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    public event EventHandler? StateChanged;

    public bool IsOpen => _db is { IsOpen: true };

    public string DatabasePath => _settings().KeePassDatabasePath;

    /// <summary>
    /// Opens the database; key derivation runs on a worker thread. Throws on a wrong key
    /// (InvalidCompositeKeyException) or an unreadable file. Must be called on the UI thread.
    /// </summary>
    public async Task OpenAsync(string password, string? keyFilePath)
    {
        var path = DatabasePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException(Strings.Error_KeePassDatabaseNotFound, path);

        var db = await Task.Run(() =>
        {
            var key = new CompositeKey();
            if (!string.IsNullOrEmpty(password))
                key.AddUserKey(new KcpPassword(password));
            if (!string.IsNullOrWhiteSpace(keyFilePath))
                key.AddUserKey(new KcpKeyFile(keyFilePath));

            var opened = new PwDatabase();
            opened.Open(IOConnectionInfo.FromPath(path), key, null);
            return opened;
        });

        _db?.Close();
        _db = db;
        _loadedFileTime = File.GetLastWriteTimeUtc(path);
        Touch();
        _idleTimer.Start();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Lock()
    {
        if (_db == null)
            return;
        _db.Close();
        _db = null;
        _idleTimer.Stop();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<KeePassEntryInfo> GetEntries()
    {
        var db = GetDatabase();
        var recycleBin = db.RecycleBinEnabled ? db.RootGroup.FindGroup(db.RecycleBinUuid, true) : null;
        var result = new List<KeePassEntryInfo>();
        foreach (var e in db.RootGroup.GetEntries(true))
        {
            if (recycleBin != null && IsInGroup(e, recycleBin))
                continue;
            result.Add(new KeePassEntryInfo(
                e.Uuid.ToHexString(),
                e.Strings.ReadSafe(PwDefs.TitleField),
                e.Strings.ReadSafe(PwDefs.UserNameField),
                e.Strings.ReadSafe(PwDefs.UrlField),
                e.ParentGroup?.GetFullPath(" / ", false) ?? ""));
        }
        return result.OrderBy(e => e.GroupPath).ThenBy(e => e.Title).ToList();
    }

    public KeePassCredential? GetCredential(string uuidHex)
    {
        var db = GetDatabase();
        var entry = FindEntry(db, uuidHex);
        if (entry == null)
            return null;
        return new KeePassCredential(
            Resolve(db, entry.Strings.ReadSafe(PwDefs.UserNameField), 0),
            Resolve(db, entry.Strings.ReadSafe(PwDefs.PasswordField), 0));
    }

    private PwDatabase GetDatabase()
    {
        if (_db == null)
            throw new InvalidOperationException(Strings.Error_KeePassLocked);
        ReloadIfChanged();
        Touch();
        return _db;
    }

    /// <summary>Re-reads the file with the same key when KeePass saved it in the meantime.</summary>
    private void ReloadIfChanged()
    {
        var path = DatabasePath;
        if (_db == null || !File.Exists(path) || File.GetLastWriteTimeUtc(path) == _loadedFileTime)
            return;
        var db = new PwDatabase();
        db.Open(IOConnectionInfo.FromPath(path), _db.MasterKey, null);
        _db.Close();
        _db = db;
        _loadedFileTime = File.GetLastWriteTimeUtc(path);
    }

    private static PwEntry? FindEntry(PwDatabase db, string uuidHex)
    {
        try
        {
            return db.RootGroup.FindEntry(new PwUuid(MemUtil.HexStringToByteArray(uuidHex)), true);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static bool IsInGroup(PwEntry entry, PwGroup group)
    {
        for (var g = entry.ParentGroup; g != null; g = g.ParentGroup)
            if (g == group)
                return true;
        return false;
    }

    [GeneratedRegex(@"\{REF:([UP])@I:([0-9A-Fa-f]{32})\}")]
    private static partial Regex RefRegex();

    /// <summary>Resolves KeePass field references of the form {REF:U@I:uuid} / {REF:P@I:uuid}.</summary>
    private static string Resolve(PwDatabase db, string value, int depth)
    {
        if (depth > 5 || !value.Contains("{REF:", StringComparison.OrdinalIgnoreCase))
            return value;
        return RefRegex().Replace(value, m =>
        {
            var target = FindEntry(db, m.Groups[2].Value);
            if (target == null)
                return m.Value;
            var field = m.Groups[1].Value == "U" ? PwDefs.UserNameField : PwDefs.PasswordField;
            return Resolve(db, target.Strings.ReadSafe(field), depth + 1);
        });
    }

    private void Touch() => _lastUse = DateTime.UtcNow;

    private void CheckIdle()
    {
        var minutes = _settings().KeePassAutoLockMinutes;
        if (minutes > 0 && DateTime.UtcNow - _lastUse > TimeSpan.FromMinutes(minutes))
            Lock();
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock && _settings().KeePassLockOnWorkstationLock)
            _dispatcher.BeginInvoke(Lock);
    }

    public void Dispose()
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        Lock();
    }
}
