using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using NaviLite.Desktop.Models;
using NaviLite.Desktop.Services;

namespace NaviLite.Desktop.ViewModels;

public sealed partial class MainViewModel : INotifyPropertyChanged
{
    private readonly NativeCore _core = new();
    private ConnectionProfile? _selectedProfile;
    private ConnectionProfile _draft = new();
    private string _portText = "3306";
    private string? _password;
    private bool _isBusy;
    private bool _coreReady;
    private ulong _handle;
    private string _status = "Loading native core...";
    private string _serverSummary = "No active connection";
    private string _activeConnection = "Connect to explore your databases";
    private string _coreVersion = "Starting...";
    private readonly AsyncCommand[] _commands;

    public MainViewModel()
    {
        NewCommand = new(() => RunAsync(() => { SelectedProfile = null; ResetDraft(new()); return Task.CompletedTask; }), () => CanEdit);
        SaveCommand = new(() => RunAsync(SaveAsync), () => CanEdit);
        DeleteCommand = new(() => RunAsync(DeleteAsync), () => CanEdit && SelectedProfile is not null);
        TestCommand = new(() => RunAsync(TestAsync), () => CanEdit);
        ConnectCommand = new(() => RunAsync(ConnectAsync), () => CanEdit);
        DisconnectCommand = new(() => RunAsync(DisconnectAsync), () => !IsBusy && IsConnected);
        RefreshCommand = new(() => RunAsync(RefreshDatabasesAsync), () => !IsBusy && IsConnected);
        OpenTableCommand = new(OpenSelectedAsync, () => CanBrowse && SelectedNode?.CanInspectDefinition == true);
        InspectDefinitionCommand = new(InspectSelectedDefinitionAsync, () => CanBrowse && SelectedNode?.CanInspectDefinition == true);
        ReloadDefinitionCommand = new(ReloadDefinitionAsync, () => CanBrowse && Definition is not null);
        CopyDefinitionCommand = new(CopyDefinitionAsync, () => CanExportDefinition);
        RefreshBranchCommand = new(RefreshBranchAsync, () => CanBrowse && SelectedNode?.IsCategory == true);
        NextPageCommand = new(() => RunAsync(NextPageAsync), () => CanBrowse && Page?.HasMore == true);
        PreviousPageCommand = new(() => RunAsync(PreviousPageAsync), () => CanBrowse && _pageHistory.Count > 0);
        ReloadPageCommand = new(() => RunAsync(ReloadPageAsync), () => CanBrowse && Page is not null);
        ApplyQueryCommand = new(() => RunAsync(ApplyQueryAsync), () => CanQuery);
        ResetQueryCommand = new(() => RunAsync(ResetQueryAsync), () => CanQuery);
        PreviousRecordCommand = new(() => SelectRecordAsync(-1), () => CanQuery && SelectedRecord is { Index: > 0 });
        NextRecordCommand = new(() => SelectRecordAsync(1), () => CanQuery && SelectedRecord is not null && SelectedRecord.Index + 1 < Page!.Rows.Count);
        ToggleFormCommand = new(() => { DataViewIndex = DataViewIndex == 1 ? 0 : 1; SelectedWorkspaceTab = 1; return Task.CompletedTask; }, () => CanQuery);
        RefreshHistoryCommand = new(() => RunAsync(() => Task.CompletedTask), () => CanBrowse);
        ClearHistoryCommand = new(() => RunAsync(ClearHistoryAsync), () => CanBrowse);
        AddFilterCommand = new(AddFilterAsync, () => CanQuery && DraftFilters.Count < 16);
        AddSortCommand = new(AddSortAsync, () => CanQuery && DraftSorts.Count < 8);
        AnalyzeSqlCommand = new(() => AnalyzeSqlAsync(false), () => CanUseSqlEditor);
        SelectCurrentSqlCommand = new(() => AnalyzeSqlAsync(true), () => CanUseSqlEditor);
        RunSqlCommand = new(RunSqlAsync, () => CanRunSql);
        _commands = [NewCommand, SaveCommand, DeleteCommand, TestCommand, ConnectCommand, DisconnectCommand, RefreshCommand, OpenTableCommand, NextPageCommand, PreviousPageCommand, ReloadPageCommand, ApplyQueryCommand, ResetQueryCommand, PreviousRecordCommand, NextRecordCommand, ToggleFormCommand, RefreshHistoryCommand, ClearHistoryCommand, AddFilterCommand, AddSortCommand, InspectDefinitionCommand, ReloadDefinitionCommand, CopyDefinitionCommand, RefreshBranchCommand, AnalyzeSqlCommand, SelectCurrentSqlCommand];
        _commands = [.. _commands, RunSqlCommand];
        ResetQueryState();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? PasswordClearRequested;
    public event Action<string>? PasswordRestoreRequested;
    public ObservableCollection<ConnectionProfile> Profiles { get; } = [];
    public ObservableCollection<SchemaNode> Databases { get; } = [];
    public ObservableCollection<string> Activity { get; } = [];
    public string[] Environments { get; } = ["local", "staging", "production"];
    public AsyncCommand NewCommand { get; }
    public AsyncCommand SaveCommand { get; }
    public AsyncCommand DeleteCommand { get; }
    public AsyncCommand TestCommand { get; }
    public AsyncCommand ConnectCommand { get; }
    public AsyncCommand DisconnectCommand { get; }
    public AsyncCommand RefreshCommand { get; }
    public string AppVersion => typeof(MainViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? throw new InvalidOperationException("The application version is missing.");
    public string WindowTitle => $"NaviLite · v{AppVersion}";
    public bool IsBusy => _isBusy;
    public bool IsConnected => _handle != 0;
    public bool CanEdit => _coreReady && !IsBusy && !IsConnected;
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string CoreVersion { get => _coreVersion; private set => Set(ref _coreVersion, value); }
    public string ServerSummary { get => _serverSummary; private set => Set(ref _serverSummary, value); }
    public string ActiveConnection { get => _activeConnection; private set => Set(ref _activeConnection, value); }
    public ConnectionProfile Draft { get => _draft; private set => Set(ref _draft, value); }
    public string PortText { get => _portText; set => Set(ref _portText, value); }
    public ConnectionProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (Set(ref _selectedProfile, value) && value is not null) ResetDraft(value.Clone());
            RefreshCommands();
        }
    }

    public void SetPassword(string password) => _password = password;

    public async Task WaitForIdleAsync()
    {
        while (IsBusy) await Task.Delay(50);
    }

    public void SetCertificate(string path)
    {
        Draft.CaCertificate = path;
    }

    public Task InitializeAsync() => RunAsync(async () =>
    {
        var info = await _core.GetInfoAsync();
        if (info.AbiVersion != 1 || info.Version != AppVersion)
            throw new InvalidOperationException("The native core version differs from the desktop app. Run scripts/Build.ps1 again.");
        _coreReady = true;
        CoreVersion = $"Rust core {info.Version} · ABI {info.AbiVersion}";
        await LoadProfilesAsync();
        Status = "Ready. Select a saved connection or create a new one.";
        Log($"NaviLite {AppVersion} ready.");
    });

    private void ResetDraft(ConnectionProfile profile)
    {
        Draft = profile;
        PortText = profile.Port.ToString();
        ClearPassword();
    }

    private void ClearPassword()
    {
        _password = null;
        PasswordClearRequested?.Invoke();
    }

    private ConnectionProfile Configuration()
    {
        var profile = Draft.Clone();
        profile.Name = profile.Name.Trim();
        profile.Host = profile.Host.Trim();
        profile.Username = profile.Username.Trim();
        profile.CaCertificate = !profile.UseTls || string.IsNullOrWhiteSpace(profile.CaCertificate) ? null : profile.CaCertificate.Trim();
        if (!ushort.TryParse(PortText, out var port) || port == 0)
            throw new InvalidOperationException("Port must be a number between 1 and 65535.");
        profile.Port = port;
        if (string.IsNullOrEmpty(profile.Name) || string.IsNullOrEmpty(profile.Host) || string.IsNullOrEmpty(profile.Username))
            throw new InvalidOperationException("Connection name, host, and username are required.");
        return profile;
    }

    private async Task LoadProfilesAsync(string? selectedId = null)
    {
        var profiles = await _core.ListProfilesAsync();
        Profiles.Clear();
        foreach (var profile in profiles) Profiles.Add(profile);
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == selectedId);
    }

    private async Task SaveAsync()
    {
        var profile = Configuration();
        string? password = _password;
        await _core.SaveProfileAsync(profile, password);
        await LoadProfilesAsync(profile.Id);
        // Unsaved passwords stay available for the next connection attempt in this window only.
        if (!profile.RememberPassword)
        {
            _password = password;
            PasswordRestoreRequested?.Invoke(password ?? string.Empty);
        }
        Status = $"Saved {profile.Name}.";
        Log(Status);
    }

    private async Task DeleteAsync()
    {
        if (SelectedProfile is null) return;
        string name = SelectedProfile.Name;
        await _core.DeleteProfileAsync(SelectedProfile.Id);
        await LoadProfilesAsync();
        ResetDraft(new());
        Status = $"Deleted {name} and its saved credential.";
        Log(Status);
    }

    private async Task TestAsync()
    {
        Status = "Checking connection and authentication...";
        var info = await _core.TestConnectionAsync(Configuration(), _password);
        Status = $"Connection successful · {info.ServerVersion} · {info.LatencyMs} ms · {(info.Tls ? "TLS verified" : "TCP")}";
        Log(Status);
    }

    private async Task ConnectAsync()
    {
        var profile = Configuration();
        Status = $"Connecting to {profile.Name}...";
        var info = await _core.OpenConnectionAsync(profile, _password);
        _handle = info.Handle;
        Definition = null;
        ResetHistory();
        ClearPassword();
        ServerSummary = $"{info.ServerVersion} · session {info.ConnectionId} · {(info.Tls ? "TLS verified" : "TCP")} · {info.LatencyMs} ms";
        ActiveConnection = $"{profile.Name} / {profile.Endpoint}";
        NotifyConnection();
        Log($"Connected to {profile.Name}.");
        await RefreshDatabasesAsync();
        SelectedWorkspaceTab = 1;
    }

    public async Task DisconnectAsync()
    {
        ulong handle = _handle;
        _handle = 0;
        ClearPreview();
        ClearSqlResult();
        SqlDatabase = string.Empty;
        Databases.Clear();
        SelectedNode = null;
        ServerSummary = "No active connection";
        ActiveConnection = "Connect to explore your databases";
        NotifyConnection();
        await _core.CloseConnectionAsync(handle);
        Status = "Disconnected.";
        Log(Status);
    }

    private async Task RefreshDatabasesAsync()
    {
        Status = "Loading databases...";
        var databases = await _core.ListDatabasesAsync(_handle);
        Databases.Clear();
        foreach (string database in databases) Databases.Add(SchemaNode.DatabaseNode(database));
        SelectedNode = null;
        ApplyFilter();
        Status = $"Connected · {Databases.Count} databases available.";
        Log(Status);
    }

    private async Task RunAsync(Func<Task> action)
    {
        _isBusy = true;
        NotifyConnection();
        try { await action(); }
        catch (DllNotFoundException) { Report("Native core is missing. Run scripts/Build.ps1 before starting NaviLite."); }
        catch (BadImageFormatException) { Report("Native core architecture differs from the app. Build both components for Windows x64."); }
        catch (Exception error) { Report(error.Message); }
        finally { await LoadHistoryAsync(); _isBusy = false; NotifyConnection(); }
    }

    private void Report(string message) { Status = message; Log($"Error: {message}"); }
    private void Log(string message)
    {
        Activity.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");
        while (Activity.Count > 100) Activity.RemoveAt(Activity.Count - 1);
    }

    private void NotifyConnection()
    {
        Changed(nameof(IsBusy));
        Changed(nameof(IsConnected));
        Changed(nameof(CanEdit));
        Changed(nameof(CanBrowse));
        Changed(nameof(CanQuery));
        Changed(nameof(CanExportDefinition));
        Changed(nameof(CanExportConnections));
        Changed(nameof(CanUseSqlEditor));
        Changed(nameof(CanRunSql));
        RefreshCommands();
    }

    private void RefreshCommands()
    {
        if (_commands is null) return;
        foreach (var command in _commands) command.Refresh();
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Changed(name);
        return true;
    }

    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
