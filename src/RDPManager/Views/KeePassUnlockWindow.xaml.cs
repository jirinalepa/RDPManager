using System.IO;
using System.Windows;
using KeePassLib.Keys;
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
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Soubor s klíčem", Filter = "Všechny soubory|*.*" };
        if (dialog.ShowDialog(this) == true)
            KeyFileBox.Text = dialog.FileName;
    }

    private async void Ok_Click(object sender, RoutedEventArgs e)
    {
        var password = PasswordBox.Password;
        var keyFile = KeyFileBox.Text.Trim();
        if (string.IsNullOrEmpty(password) && string.IsNullOrEmpty(keyFile))
        {
            ShowError("Zadej hlavní heslo nebo soubor s klíčem.");
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
            ShowError("Nesprávné heslo nebo soubor s klíčem.");
            PasswordBox.Password = "";
            PasswordBox.Focus();
        }
        catch (Exception ex)
        {
            ShowError("Databázi se nepodařilo otevřít: " + ex.Message);
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
