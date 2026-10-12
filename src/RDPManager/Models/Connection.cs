using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RDPManager.Models;

public enum CredentialMode
{
    /// <summary>No credentials are supplied; mstsc uses the current/saved Windows credentials or asks.</summary>
    Default,
    /// <summary>Only the user name is pre-filled, the password is entered in mstsc.</summary>
    Username,
    /// <summary>User name and password are taken from a KeePass entry.</summary>
    KeePass,
}

public enum AudioMode
{
    Local = 0,
    Remote = 1,
    None = 2,
}

public partial class Connection : ObservableObject
{
    [ObservableProperty] private Guid _id = Guid.NewGuid();
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _host = "";
    [ObservableProperty] private int _port = 3389;
    [ObservableProperty] private string _group = "";
    [ObservableProperty] private string _notes = "";

    [ObservableProperty] private CredentialMode _credentialMode = CredentialMode.Default;
    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string? _keePassEntryUuid;
    [ObservableProperty] private string? _keePassEntryTitle;

    [ObservableProperty] private bool _fullScreen = true;
    [ObservableProperty] private bool _useMultimon;
    [ObservableProperty] private string _selectedMonitors = "";
    [ObservableProperty] private int _width = 1920;
    [ObservableProperty] private int _height = 1080;

    [ObservableProperty] private bool _redirectClipboard = true;
    [ObservableProperty] private bool _redirectDrives;
    [ObservableProperty] private bool _redirectPrinters;
    [ObservableProperty] private AudioMode _audioMode = AudioMode.Local;

    [ObservableProperty] private string _gatewayHost = "";
    [ObservableProperty] private bool _adminSession;
    [ObservableProperty] private string _extraRdpSettings = "";

    public string DisplayAddress => Port == 3389 ? Host : $"{Host}:{Port}";

    partial void OnHostChanged(string value) => OnPropertyChanged(nameof(DisplayAddress));
    partial void OnPortChanged(int value) => OnPropertyChanged(nameof(DisplayAddress));

    public Connection Clone() =>
        JsonSerializer.Deserialize<Connection>(JsonSerializer.Serialize(this))!;

    public void CopyFrom(Connection other)
    {
        foreach (var p in typeof(Connection).GetProperties())
            if (p.CanWrite && p.CanRead)
                p.SetValue(this, p.GetValue(other));
    }
}
