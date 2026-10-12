using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using RDPManager.Models;
using RDPManager.Properties;
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
                    await Dialogs.ErrorAsync(Localization.Format(Strings.Error_NoKeePassEntry, connection.Name));
                    return;
                }
                if (!EnsureKeePassUnlocked())
                    return;
                credential = KeePass.GetCredential(connection.KeePassEntryUuid);
                if (credential == null)
                {
                    await Dialogs.ErrorAsync(Localization.Format(Strings.Error_KeePassEntryMissing, connection.KeePassEntryTitle));
                    return;
                }
                if (!string.IsNullOrWhiteSpace(connection.Username))
                    credential = credential with { UserName = connection.Username };

                var target = CredentialInjector.TargetForHost(connection.Host);
                if (CredentialInjector.GetState(target) == CredentialInjector.State.Foreign)
                {
                    var choice = await Dialogs.ChooseAsync(
                        Localization.Format(Strings.Prompt_ForeignCredential, connection.Host),
                        Strings.Prompt_UseKeePass, Strings.Prompt_KeepSaved);
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
            await Dialogs.ErrorAsync(Localization.Format(Strings.Error_ConnectFailed, connection.Host, ex.Message));
        }
    }

    public bool EnsureKeePassUnlocked()
    {
        if (KeePass.IsOpen)
            return true;
        if (string.IsNullOrWhiteSpace(Settings.KeePassDatabasePath))
        {
            _ = Dialogs.ErrorAsync(Strings.Error_KeePassPathNotSet);
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
        copy.Name += Strings.Connection_CopySuffix;
        AddConnection(copy);
    }

    public async Task DeleteConnectionAsync(Connection connection)
    {
        if (await Dialogs.ConfirmAsync(Localization.Format(Strings.Prompt_DeleteConnection, connection.Name), Strings.Common_Delete))
        {
            Connections.Remove(connection);
            SaveConnections();
        }
    }

    public async Task ImportRdpFilesAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Strings.Import_Title,
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
                    errors.Add(Localization.Format(Strings.Import_MissingAddress, Path.GetFileName(file)));
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }
        SaveConnections();
        if (errors.Count > 0)
            await Dialogs.ErrorAsync(Localization.Format(Strings.Import_Errors, string.Join("\n", errors)));
    }

    public async void OpenSettings()
    {
        var window = new SettingsWindow(this, Settings);
        if (Dialogs.ShowDialog(window) != true)
            return;
        var keePassChanged = window.Result.KeePassDatabasePath != Settings.KeePassDatabasePath
                             || window.Result.KeePassKeyFilePath != Settings.KeePassKeyFilePath;
        var languageChanged = Localization.Resolve(window.Result.Language).Name != Localization.Resolve(Settings.Language).Name;
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
            _ = Dialogs.ErrorAsync(Localization.Format(Strings.Error_Autostart, ex.Message));
        }
        ApplyTheme();
        SettingsChanged?.Invoke(this, EventArgs.Empty);

        if (languageChanged)
        {
            // New dialogs follow the new language immediately; the panel and tray menu need a restart.
            Localization.Apply(Settings.Language);
            if (await Dialogs.ConfirmAsync(Strings.Prompt_RestartForLanguage, Strings.Prompt_Restart))
                ((App)Application.Current).RestartApp();
        }
    }

    public void SaveConnections()
    {
        try
        {
            _store.SaveConnections(Connections);
        }
        catch (Exception ex)
        {
            _ = Dialogs.ErrorAsync(Localization.Format(Strings.Error_SaveConnections, ex.Message));
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
