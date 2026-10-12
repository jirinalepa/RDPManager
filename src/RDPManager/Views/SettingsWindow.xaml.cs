using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using RDPManager.Docking;
using RDPManager.Models;
using Wpf.Ui.Controls;

namespace RDPManager.Views;

public partial class SettingsWindow : FluentWindow
{
    private record MonitorOption(string DeviceName, string Label);

    public SettingsWindow(AppController controller, AppSettings settings)
    {
        InitializeComponent();
        // Edit a copy; the caller applies it only on save.
        Result = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        DataContext = Result;

        var monitors = new List<MonitorOption> { new("", "Hlavní monitor") };
        var index = 1;
        foreach (var s in EdgeDockController.Screens)
        {
            var b = s.Bounds;
            monitors.Add(new MonitorOption(s.DeviceName,
                $"Monitor {index++} – {b.Width}×{b.Height}{(s.Primary ? " (hlavní)" : "")}"));
        }
        MonitorBox.ItemsSource = monitors;
        MonitorBox.SelectedValue = monitors.Any(m => m.DeviceName == Result.MonitorDeviceName) ? Result.MonitorDeviceName : "";
        HotkeyBox.Text = Result.Hotkey;

        AboutLogo.Source = AppIcon.ImageSource;
        AboutProduct.Text = AppInfo.ProductWithVersion;
        AboutCopyright.Text = AppInfo.Copyright;
        AboutRepoText.Text = AppInfo.RepositoryUrl;
    }

    private void RepoLink_Click(object sender, RoutedEventArgs e) => AppInfo.OpenRepository();

    private void SectionList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (SectionHost == null)
            return;
        for (var i = 0; i < SectionHost.Children.Count; i++)
            SectionHost.Children[i].Visibility = i == SectionList.SelectedIndex ? Visibility.Visible : Visibility.Collapsed;
    }

    public AppSettings Result { get; }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Tab or Key.Escape || HotkeyGesture.IsModifierKey(key))
            return;
        if (Keyboard.Modifiers == ModifierKeys.None)
            return;
        HotkeyBox.Text = new HotkeyGesture(Keyboard.Modifiers, key).ToString();
    }

    private void BrowseDatabase_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "KeePass databáze", Filter = "KeePass (*.kdbx)|*.kdbx|Všechny soubory|*.*" };
        if (dialog.ShowDialog(this) == true)
        {
            Result.KeePassDatabasePath = dialog.FileName;
            Rebind();
        }
    }

    private void BrowseKeyFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Soubor s klíčem", Filter = "Všechny soubory|*.*" };
        if (dialog.ShowDialog(this) == true)
        {
            Result.KeePassKeyFilePath = dialog.FileName;
            Rebind();
        }
    }

    /// <summary>AppSettings is a plain class, so refresh bindings after changing it from code.</summary>
    private void Rebind()
    {
        DataContext = null;
        DataContext = Result;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (Result.HotkeyEnabled && HotkeyGesture.Parse(HotkeyBox.Text) == null)
        {
            await Dialogs.ErrorAsync("Klávesová zkratka musí obsahovat modifikátor (Ctrl/Alt/Shift/Win) a klávesu.");
            return;
        }
        Result.Hotkey = HotkeyBox.Text;
        Result.MonitorDeviceName = MonitorBox.SelectedValue as string ?? "";
        Result.DockSize = Math.Clamp(Result.DockSize, 200, 800);
        Result.ShowDelayMs = Math.Clamp(Result.ShowDelayMs, 0, 5000);
        Result.HideDelayMs = Math.Clamp(Result.HideDelayMs, 0, 10000);
        Result.KeePassAutoLockMinutes = Math.Max(0, Result.KeePassAutoLockMinutes);
        Result.CredentialCleanupSeconds = Math.Clamp(Result.CredentialCleanupSeconds, 10, 600);
        Result.KeePassDatabasePath = Result.KeePassDatabasePath.Trim().Trim('"');
        Result.KeePassKeyFilePath = Result.KeePassKeyFilePath.Trim().Trim('"');
        DialogResult = true;
    }
}
