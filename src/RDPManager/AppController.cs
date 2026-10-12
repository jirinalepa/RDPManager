using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using RDPManager.Models;
using RDPManager.Services;
using RDPManager.Views;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using MessageBoxResult = Wpf.Ui.Controls.MessageBoxResult;

namespace RDPManager;

/// <summary>Owns configuration and services and implements the user-facing actions.</summary>
public class AppController : IDisposable
{
    private readonly ConfigStore _store = new();

    public AppController()
    {
        Settings = _store.LoadSettings();
        Connections = new ObservableCollection<Connection>(_store.LoadConnections());
        KeePass = new KeePassService(() => Settings);
        Sessions = new SessionTracker();
        Launcher = new RdpLauncher(() => Settings, Sessions);
    }

    public AppSettings Settings { get; private set; }
    public ObservableCollection<Connection> Connections { get; }
    public KeePassService KeePass { get; }
    public SessionTracker Sessions { get; }
    public RdpLauncher Launcher { get; }

    public event EventHandler? SettingsChanged;
    /// <summary>Raised after a session was started (the dock hides itself if configured).</summary>
    public event EventHandler? Connected;

    public IEnumerable<string> Groups => Connections.Select(c => c.Group)
        .Where(g => !string.IsNullOrWhiteSpace(g)).Distinct(StringComparer.CurrentCultureIgnoreCase).Order();

    public void ApplyTheme()
    {
        switch (Settings.Theme)
        {
            case AppTheme.Light:
                ApplicationThemeManager.Apply(ApplicationTheme.Light, WindowBackdropType.None, true);
                break;
            case AppTheme.Dark:
                ApplicationThemeManager.Apply(ApplicationTheme.Dark, WindowBackdropType.None, true);
                break;
            default:
                ApplicationThemeManager.ApplySystemTheme(true);
                break;
        }
    }

    public async Task ConnectAsync(Connection connection, bool admin = false)
    {
        try
        {
            KeePassCredential? credential = null;
            if (connection.CredentialMode == CredentialMode.KeePass)
            {
                if (string.IsNullOrEmpty(connection.KeePassEntryUuid))
                {
                    await Dialogs.ErrorAsync($"Spojení „{connection.Name}“ nemá vybraný záznam z KeePassu.");
                    return;
                }
                if (!EnsureKeePassUnlocked())
                    return;
                credential = KeePass.GetCredential(connection.KeePassEntryUuid);
                if (credential == null)
                {
                    await Dialogs.ErrorAsync($"Záznam „{connection.KeePassEntryTitle}“ už v KeePass databázi neexistuje.");
                    return;
                }
                if (!string.IsNullOrWhiteSpace(connection.Username))
                    credential = credential with { UserName = connection.Username };

                var target = CredentialInjector.TargetForHost(connection.Host);
                if (CredentialInjector.GetState(target) == CredentialInjector.State.Foreign)
                {
                    var choice = await Dialogs.ChooseAsync(
                        $"Pro {connection.Host} už je ve Windows uložené pověření (Správce pověření).\n\n" +
                        "„Použít KeePass“ ho nahradí údaji z KeePassu a po připojení ho odstraní.\n" +
                        "„Ponechat uložené“ použije stávající uložené pověření.",
                        "Použít KeePass", "Ponechat uložené");
                    if (choice == MessageBoxResult.None)
                        return;
                    if (choice == MessageBoxResult.Secondary)
                        credential = null;
                }
            }

            Launcher.Launch(connection, admin, credential);
            Connected?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            await Dialogs.ErrorAsync($"Připojení k {connection.Host} se nezdařilo:\n{ex.Message}");
        }
    }

    public bool EnsureKeePassUnlocked()
    {
        if (KeePass.IsOpen)
            return true;
        if (string.IsNullOrWhiteSpace(Settings.KeePassDatabasePath))
        {
            _ = Dialogs.ErrorAsync("Nejdřív nastav cestu ke KeePass databázi (Nastavení → KeePass).");
            return false;
        }
        return Dialogs.ShowDialog(new KeePassUnlockWindow(this)) == true;
    }

    public void AddConnection(Connection? template = null)
    {
        var connection = template ?? new Connection();
        var editor = new ConnectionEditorWindow(this, connection, isNew: true);
        if (Dialogs.ShowDialog(editor) == true)
        {
            Connections.Add(editor.Result);
            SaveConnections();
        }
    }

    public void EditConnection(Connection connection)
    {
        var editor = new ConnectionEditorWindow(this, connection.Clone(), isNew: false);
        if (Dialogs.ShowDialog(editor) == true)
        {
            connection.CopyFrom(editor.Result);
            SaveConnections();
        }
    }

    public void DuplicateConnection(Connection connection)
    {
        var copy = connection.Clone();
        copy.Id = Guid.NewGuid();
        copy.Name += " (kopie)";
        AddConnection(copy);
    }

    public async Task DeleteConnectionAsync(Connection connection)
    {
        if (await Dialogs.ConfirmAsync($"Opravdu smazat spojení „{connection.Name}“?", "Smazat"))
        {
            Connections.Remove(connection);
            SaveConnections();
        }
    }

    public async Task ImportRdpFilesAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Importovat .rdp soubory",
            Filter = "Remote Desktop (*.rdp)|*.rdp",
            Multiselect = true,
        };
        if (dialog.ShowDialog() != true)
            return;
        var errors = new List<string>();
        foreach (var file in dialog.FileNames)
        {
            try
            {
                var c = RdpFileImporter.Import(file);
                if (!string.IsNullOrWhiteSpace(c.Host))
                    Connections.Add(c);
                else
                    errors.Add($"{Path.GetFileName(file)}: chybí adresa");
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }
        SaveConnections();
        if (errors.Count > 0)
            await Dialogs.ErrorAsync("Některé soubory se nepodařilo importovat:\n" + string.Join("\n", errors));
    }

    public void OpenSettings()
    {
        var window = new SettingsWindow(this, Settings);
        if (Dialogs.ShowDialog(window) != true)
            return;
        var keePassChanged = window.Result.KeePassDatabasePath != Settings.KeePassDatabasePath
                             || window.Result.KeePassKeyFilePath != Settings.KeePassKeyFilePath;
        Settings = window.Result;
        _store.SaveSettings(Settings);
        if (keePassChanged)
            KeePass.Lock();
        try
        {
            Autostart.Apply(Settings.StartWithWindows);
        }
        catch (Exception ex)
        {
            _ = Dialogs.ErrorAsync("Nepodařilo se nastavit spouštění s Windows: " + ex.Message);
        }
        ApplyTheme();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SaveConnections()
    {
        try
        {
            _store.SaveConnections(Connections);
        }
        catch (Exception ex)
        {
            _ = Dialogs.ErrorAsync("Uložení spojení selhalo: " + ex.Message);
        }
    }

    public static void CopyToClipboard(string text)
    {
        try { Clipboard.SetText(text); }
        catch (System.Runtime.InteropServices.COMException) { }
    }

    public void Dispose()
    {
        KeePass.Dispose();
        try { CredentialInjector.CleanupLeftovers(); }
        catch (Exception) { }
    }
}
