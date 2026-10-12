using System.ComponentModel;
using System.Windows;
using RDPManager.Models;
using Wpf.Ui.Controls;

namespace RDPManager.Views;

public partial class ConnectionEditorWindow : FluentWindow
{
    private readonly AppController _controller;

    public ConnectionEditorWindow(AppController controller, Connection connection, bool isNew)
    {
        InitializeComponent();
        _controller = controller;
        Result = connection;
        DataContext = connection;
        Title = TitleBar.Title = isNew ? "Nové spojení" : $"Upravit – {connection.Name}";
        GroupBox.ItemsSource = controller.Groups.ToList();

        connection.PropertyChanged += OnConnectionChanged;
        UpdateCredentialUi();
        Loaded += (_, _) => (string.IsNullOrEmpty(connection.Host) ? HostBox : NameBox).Focus();
    }

    public Connection Result { get; }

    private void OnConnectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Connection.CredentialMode))
            UpdateCredentialUi();
    }

    private void UpdateCredentialUi()
    {
        var keePass = Result.CredentialMode == CredentialMode.KeePass;
        KeePassPanel.Visibility = keePass ? Visibility.Visible : Visibility.Collapsed;
        UsernameLabel.Text = keePass ? "Přepsat uživatelské jméno (volitelné)" : "Uživatelské jméno";
        UsernameHint.Text = Result.CredentialMode switch
        {
            CredentialMode.KeePass => "Prázdné = použije se jméno ze záznamu v KeePassu.",
            CredentialMode.Default => "Volitelné. Pro přihlášení bez dotazu doménovým účtem musí politika povolit delegování výchozích pověření.",
            _ => "Heslo zadáš v okně připojení.",
        };
    }

    private void PickEntry_Click(object sender, RoutedEventArgs e)
    {
        if (!_controller.EnsureKeePassUnlocked())
            return;
        var picker = new KeePassEntryPickerWindow(_controller.KeePass, Result.KeePassEntryUuid, Result.Host);
        if (Dialogs.ShowDialog(picker) == true && picker.SelectedEntry is { } entry)
        {
            Result.KeePassEntryUuid = entry.Uuid;
            Result.KeePassEntryTitle = string.IsNullOrEmpty(entry.UserName) ? entry.Title : $"{entry.Title} ({entry.UserName})";
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        Result.Host = Result.Host.Trim();
        Result.Name = Result.Name.Trim();
        Result.Group = Result.Group.Trim();
        if (string.IsNullOrEmpty(Result.Host))
        {
            await Dialogs.ErrorAsync("Vyplň adresu serveru.");
            return;
        }
        if (Result.Port is < 1 or > 65535)
        {
            await Dialogs.ErrorAsync("Port musí být v rozsahu 1–65535.");
            return;
        }
        if (Result.CredentialMode == CredentialMode.KeePass && string.IsNullOrEmpty(Result.KeePassEntryUuid))
        {
            await Dialogs.ErrorAsync("Vyber záznam z KeePassu, nebo zvol jiný způsob přihlášení.");
            return;
        }
        if (string.IsNullOrEmpty(Result.Name))
            Result.Name = Result.Host;

        Result.PropertyChanged -= OnConnectionChanged;
        DialogResult = true;
    }
}
