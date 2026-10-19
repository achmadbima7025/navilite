using System.Text.Json;
using NaviLite.Desktop.Interop;
using NaviLite.Desktop.Models;

namespace NaviLite.Desktop.Services;

public sealed unsafe partial class NativeCore
{
    public Task<SqlScriptAnalysis> AnalyzeSqlScriptAsync(string script, int caretUtf16, bool backslashEscapes, bool ansiQuotes) => Task.Run(() =>
    {
        byte[] payload = Utf8(JsonSerializer.Serialize(new { script, caretUtf16, backslashEscapes, ansiQuotes }, JsonOptions));
        fixed (byte* pointer = payload)
            return Read<SqlScriptAnalysis>(NativeMethods.navilite_sql_script_analyze(pointer));
    });

    public Task<NativePage> ReadSqlQueryAsync(ulong handle, string script, int caretUtf16, int selectionStartUtf16, int selectionLengthUtf16,
        string database, int rowLimit, bool backslashEscapes, bool ansiQuotes) => Task.Run(() =>
    {
        byte[] payload = Utf8(JsonSerializer.Serialize(new { script, caretUtf16, selectionStartUtf16, selectionLengthUtf16, database, rowLimit, backslashEscapes, ansiQuotes }, JsonOptions));
        fixed (byte* pointer = payload)
            return new NativePage(new FfiPage { buffer = NativeMethods.navilite_sql_query_read(handle, pointer), page_id = 0 });
    });
}
