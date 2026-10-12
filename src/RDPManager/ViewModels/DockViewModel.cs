using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using RDPManager.Models;
using RDPManager.Services;

namespace RDPManager.ViewModels;

public partial class DockViewModel : ObservableObject
{
    public DockViewModel(AppController controller)
    {
        Controller = controller;

        var view = (ListCollectionView)CollectionViewSource.GetDefaultView(controller.Connections);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Connection.Group)));
        view.SortDescriptions.Add(new SortDescription(nameof(Connection.Group), ListSortDirection.Ascending));
        view.SortDescriptions.Add(new SortDescription(nameof(Connection.Name), ListSortDirection.Ascending));
        view.IsLiveGrouping = true;
        view.IsLiveSorting = true;
        view.IsLiveFiltering = true;
        view.LiveGroupingProperties.Add(nameof(Connection.Group));
        view.LiveSortingProperties.Add(nameof(Connection.Group));
        view.LiveSortingProperties.Add(nameof(Connection.Name));
        view.LiveFilteringProperties.Add(nameof(Connection.Name));
        view.LiveFilteringProperties.Add(nameof(Connection.Host));
        view.Filter = Matches;
        ConnectionsView = view;

        controller.Connections.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
        Sessions.CollectionChanged += OnSessionsChanged;
        controller.KeePass.StateChanged += (_, _) => OnPropertyChanged(nameof(IsKeePassOpen));
    }

    public AppController Controller { get; }

    public ListCollectionView ConnectionsView { get; }

    public ObservableCollection<RdpSession> Sessions => Controller.Sessions.Sessions;

    public bool HasSessions => Sessions.Count > 0;

    public bool IsEmpty => Controller.Connections.Count == 0;

    public bool IsKeePassOpen => Controller.KeePass.IsOpen;

    [ObservableProperty] private string _searchText = "";

    [ObservableProperty] private bool _isPinned;

    partial void OnSearchTextChanged(string value) => ConnectionsView.Refresh();

    private bool Matches(object item)
    {
        if (item is not Connection c || string.IsNullOrWhiteSpace(SearchText))
            return true;
        var terms = SearchText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return terms.All(t =>
            c.Name.Contains(t, StringComparison.CurrentCultureIgnoreCase) ||
            c.Host.Contains(t, StringComparison.CurrentCultureIgnoreCase) ||
            c.Group.Contains(t, StringComparison.CurrentCultureIgnoreCase) ||
            c.Notes.Contains(t, StringComparison.CurrentCultureIgnoreCase));
    }

    private void OnSessionsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        OnPropertyChanged(nameof(HasSessions));
}
