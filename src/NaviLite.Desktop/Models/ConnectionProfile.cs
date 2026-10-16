using System.ComponentModel;
using System.Text.Json.Serialization;

namespace NaviLite.Desktop.Models;

public sealed class ConnectionProfile : INotifyPropertyChanged
{
    private bool _useTls;
    private string? _caCertificate;
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Local MySQL";
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 3306;
    public string Username { get; set; } = "root";
    public string Environment { get; set; } = "local";
    public bool RememberPassword { get; set; }
    public bool UseTls
    {
        get => _useTls;
        set { _useTls = value; PropertyChanged?.Invoke(this, new(nameof(UseTls))); }
    }
    public string? CaCertificate
    {
        get => _caCertificate;
        set { _caCertificate = value; PropertyChanged?.Invoke(this, new(nameof(CaCertificate))); }
    }

    [JsonIgnore]
    public string Endpoint => $"{Username}@{Host}:{Port}";
    [JsonIgnore]
    public string EnvironmentColor => Environment switch
    {
        "production" => "#DC5656",
        "staging" => "#D49A2A",
        _ => "#299F79"
    };

    public ConnectionProfile Clone()
    {
        var clone = (ConnectionProfile)MemberwiseClone();
        clone.PropertyChanged = null;
        return clone;
    }
}

public sealed record CoreInfo(string Version, int AbiVersion);
public sealed record ConnectionInfo(ulong Handle, string ServerVersion, ulong ConnectionId, ulong LatencyMs, bool Tls);
