using System.IO;
using System.Windows;
using KeePassLib.Keys;
using RDPManager.Properties;
using Wpf.Ui.Controls;

namespace RDPManager.Views;

public partial class KeePassUnlockWindow : FluentWindow
{
    private readonly AppController _controller;

    public KeePassUnlockWindow(AppController controller)
    {
        InitializeComponent();
        _controller = controller;
        DatabaseText.Text = controller.Settings.KeePassDatabasePath;
        DatabaseText.ToolTip = controller.Settings.KeePassDatabasePath;
        KeyFileBox.Text = controller.Settings.KeePassKeyFilePath;
        Loaded += (_, _) => PasswordBox.Focus();
    }

    private void BrowseKeyFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = Strings.Common_KeyFile, Filter = Strings.Common_AllFilesFilter };
        if (dialog.ShowDialog(this) == true)
            KeyFileBox.Text = dialog.FileName;
    }

    private async void Ok_Click(object sender, RoutedEventArgs e)
    {
        var password = PasswordBox.Password;
        var keyFile = KeyFileBox.Text.Trim();
        if (string.IsNullOrEmpty(password) && string.IsNullOrEmpty(keyFile))
        {
            ShowError(Strings.Unlock_Error_Empty);
            return;
        }

        SetBusy(true);
        try
        {
            await _controller.KeePass.OpenAsync(password, keyFile);
            DialogResult = true;
        }
        catch (InvalidCompositeKeyException)
        {
            ShowError(Strings.Unlock_Error_WrongKey);
            PasswordBox.Password = "";
            PasswordBox.Focus();
        }
        catch (Exception ex)
        {
            ShowError(Localization.Format(Strings.Unlock_Error_Open, ex.Message));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void SetBusy(bool busy)
    {
        Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        OkButton.IsEnabled = !busy;
        if (busy)
            ErrorText.Visibility = Visibility.Collapsed;
    }
}
