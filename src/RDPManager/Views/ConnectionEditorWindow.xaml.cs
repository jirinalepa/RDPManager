using System.ComponentModel;
using System.Windows;
using RDPManager.Models;
using RDPManager.Properties;
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
        Title = TitleBar.Title = isNew ? Strings.Editor_TitleNew : Localization.Format(Strings.Editor_TitleEdit, connection.Name);
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

    private void SectionList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (SectionHost == null)
            return;
        for (var i = 0; i < SectionHost.Children.Count; i++)
            SectionHost.Children[i].Visibility = i == SectionList.SelectedIndex ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateCredentialUi()
    {
        var keePass = Result.CredentialMode == CredentialMode.KeePass;
        KeePassPanel.Visibility = keePass ? Visibility.Visible : Visibility.Collapsed;
        UsernameLabel.Text = keePass ? Strings.Editor_UsernameOverride : Strings.Editor_Username;
        UsernameHint.Text = Result.CredentialMode switch
        {
            CredentialMode.KeePass => Strings.Editor_Hint_KeePass,
            CredentialMode.Default => Strings.Editor_Hint_Default,
            _ => Strings.Editor_Hint_Username,
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
            await Dialogs.ErrorAsync(Strings.Editor_Error_Host);
            return;
        }
        if (Result.Port is < 1 or > 65535)
        {
            await Dialogs.ErrorAsync(Strings.Editor_Error_Port);
            return;
        }
        if (Result.CredentialMode == CredentialMode.KeePass && string.IsNullOrEmpty(Result.KeePassEntryUuid))
        {
            await Dialogs.ErrorAsync(Strings.Editor_Error_KeePassEntry);
            return;
        }
        if (string.IsNullOrEmpty(Result.Name))
            Result.Name = Result.Host;

        Result.PropertyChanged -= OnConnectionChanged;
        DialogResult = true;
    }
}
