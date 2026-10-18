using System.Text.Json;
using NaviLite.Desktop.Interop;
using NaviLite.Desktop.Models;

namespace NaviLite.Desktop.Services;

public sealed partial class NativeCore
{
    public Task<List<SqlHistoryEntry>> ReadHistoryAsync(ulong handle) => Task.Run(() => Read<List<SqlHistoryEntry>>(NativeMethods.navilite_history_list(handle)));
    public Task ClearHistoryAsync(ulong handle) => Task.Run(() => Read<JsonElement>(NativeMethods.navilite_history_clear(handle)));
}
