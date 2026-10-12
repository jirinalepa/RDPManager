using System.Windows;
using RDPManager.Properties;
using Wpf.Ui.Controls;
using MessageBox = Wpf.Ui.Controls.MessageBox;
using MessageBoxResult = Wpf.Ui.Controls.MessageBoxResult;

namespace RDPManager.Views;

public static class Dialogs
{
    /// <summary>
    /// Dialogs must stay above a full-screen RDP window, so they are topmost and owned by the dock
    /// when it is visible (an owner that was never shown cannot be used).
    /// </summary>
    public static void Prepare(Window dialog)
    {
        dialog.Topmost = true;
        var owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w != dialog)
                    ?? Application.Current.MainWindow;
        if (owner != null && owner != dialog && owner.IsVisible)
        {
            dialog.Owner = owner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    public static bool? ShowDialog(Window dialog)
    {
        Prepare(dialog);
        return dialog.ShowDialog();
    }

    public static Task ErrorAsync(string message, string title = "RDP Manager") =>
        CreateBox(title, message, Strings.Common_OK, null, null).ShowDialogAsync();

    public static async Task<bool> ConfirmAsync(string message, string confirmText, string title = "RDP Manager")
    {
        var box = CreateBox(title, message, Strings.Common_Cancel, confirmText, null);
        box.PrimaryButtonAppearance = ControlAppearance.Primary;
        return await box.ShowDialogAsync() == MessageBoxResult.Primary;
    }

    /// <returns>Primary, Secondary or None (cancel).</returns>
    public static Task<MessageBoxResult> ChooseAsync(string message, string primary, string secondary, string title = "RDP Manager")
    {
        var box = CreateBox(title, message, Strings.Common_Cancel, primary, secondary);
        box.PrimaryButtonAppearance = ControlAppearance.Primary;
        return box.ShowDialogAsync();
    }

    private static MessageBox CreateBox(string title, string message, string close, string? primary, string? secondary)
    {
        var box = new MessageBox
        {
            Title = title,
            Content = new System.Windows.Controls.TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 420,
            },
            CloseButtonText = close,
            IsPrimaryButtonEnabled = primary != null,
            IsSecondaryButtonEnabled = secondary != null,
            PrimaryButtonText = primary ?? "",
            SecondaryButtonText = secondary ?? "",
        };
        Prepare(box);
        return box;
    }
}
