using System.Text.Json;
using NaviLite.Desktop.Interop;
using NaviLite.Desktop.Models;

namespace NaviLite.Desktop.Services;

public sealed unsafe partial class NativeCore
{
    public Task<string> ExportConnectionsAsync() => Task.Run(() => Read<string>(NativeMethods.navilite_profiles_export()));

    public Task<ConnectionImportPreview> PreviewConnectionImportAsync(string document) => Task.Run(() =>
    {
        byte[] bytes = Utf8(document);
        fixed (byte* pointer = bytes)
            return Read<ConnectionImportPreview>(NativeMethods.navilite_profiles_import_preview(pointer));
    });

    public Task<ConnectionImportResult> ImportConnectionsAsync(string document, IReadOnlyList<int> selectedIndices) => Task.Run(() =>
    {
        byte[] bytes = Utf8(document), selection = Utf8(JsonSerializer.Serialize(selectedIndices));
        fixed (byte* pointer = bytes, indices = selection)
            return Read<ConnectionImportResult>(NativeMethods.navilite_profiles_import(pointer, indices));
    });
}
