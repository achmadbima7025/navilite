using NaviLite.Desktop.Models;
using NaviLite.Desktop.Services;

namespace NaviLite.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    public event Func<ConnectionImportPreview, IReadOnlyList<int>?>? ConnectionImportSelectionRequested;
    public bool CanExportConnections => CanEdit && Profiles.Count > 0;

    public Task ImportConnectionsAsync(string path)
    {
        if (!CanEdit) return Task.CompletedTask;
        return RunAsync(async () =>
        {
            Status = "Reading connection metadata...";
            string document = await ConnectionFiles.ReadAsync(path);
            var preview = await _core.PreviewConnectionImportAsync(document);
            var selection = ConnectionImportSelectionRequested?.Invoke(preview);
            if (selection is null || selection.Count == 0)
            {
                Status = "Connection import cancelled.";
                return;
            }
            Status = "Importing selected connections...";
            var result = await _core.ImportConnectionsAsync(document, selection);
            string? previous = SelectedProfile?.Id;
            await LoadProfilesAsync(result.FirstImportedId ?? previous);
            if (SelectedProfile is null) ResetDraft(new());
            SelectedWorkspaceTab = 0;
            Status = $"Imported {result.Imported} connections · {result.Skipped} duplicates skipped. Enter passwords before connecting.";
            Log(Status);
        });
    }

    public Task ExportConnectionsAsync(string path)
    {
        if (!CanExportConnections) return Task.CompletedTask;
        return RunAsync(async () =>
        {
            Status = "Exporting saved connection metadata...";
            string document = await _core.ExportConnectionsAsync();
            await ConnectionFiles.WriteAsync(path, document);
            Status = "Exported saved connection metadata. Passwords are excluded; CA certificates are recorded as file paths.";
            Log(Status);
        });
    }
}
