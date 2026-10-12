using System.Windows;
using System.Windows.Input;
using RDPManager.Services;
using RDPManager.Properties;
using Wpf.Ui.Controls;

namespace RDPManager.Views;

public partial class KeePassEntryPickerWindow : FluentWindow
{
    private readonly IReadOnlyList<KeePassEntryInfo> _entries;

    public KeePassEntryPickerWindow(KeePassService keePass, string? selectedUuid, string? host)
    {
        InitializeComponent();
        _entries = keePass.GetEntries();

        var current = _entries.FirstOrDefault(e => e.Uuid == selectedUuid);
        // Without a previous choice, pre-filter by host when some entry mentions it.
        if (current == null && !string.IsNullOrWhiteSpace(host) && _entries.Any(e => MatchesHost(e, host)))
            SearchBox.Text = host;
        ApplyFilter();
        if (current != null)
        {
            EntryList.SelectedItem = current;
            EntryList.ScrollIntoView(current);
        }
        else if (EntryList.Items.Count > 0)
        {
            EntryList.SelectedIndex = 0;
        }
        Loaded += (_, _) => SearchBox.Focus();
    }

    public KeePassEntryInfo? SelectedEntry { get; private set; }

    private static bool MatchesHost(KeePassEntryInfo e, string host) =>
        e.Title.Contains(host, StringComparison.OrdinalIgnoreCase) || e.Url.Contains(host, StringComparison.OrdinalIgnoreCase);

    private void ApplyFilter()
    {
        var terms = SearchBox.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var filtered = _entries.Where(e => terms.All(t =>
            e.Title.Contains(t, StringComparison.CurrentCultureIgnoreCase) ||
            e.UserName.Contains(t, StringComparison.CurrentCultureIgnoreCase) ||
            e.Url.Contains(t, StringComparison.CurrentCultureIgnoreCase) ||
            e.GroupPath.Contains(t, StringComparison.CurrentCultureIgnoreCase))).ToList();
        EntryList.ItemsSource = filtered;
        CountText.Text = Localization.Format(Strings.Picker_Count, filtered.Count, _entries.Count);
        if (EntryList.SelectedItem == null && filtered.Count > 0)
            EntryList.SelectedIndex = 0;
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (IsInitialized)
            ApplyFilter();
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Down or Key.Up && EntryList.Items.Count > 0)
        {
            var index = EntryList.SelectedIndex + (e.Key == Key.Down ? 1 : -1);
            EntryList.SelectedIndex = Math.Clamp(index, 0, EntryList.Items.Count - 1);
            EntryList.ScrollIntoView(EntryList.SelectedItem);
            e.Handled = true;
        }
    }

    private void EntryList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Select_Click(sender, e);

    private void Select_Click(object sender, RoutedEventArgs e)
    {
        if (EntryList.SelectedItem is not KeePassEntryInfo entry)
            return;
        SelectedEntry = entry;
        DialogResult = true;
    }
}
