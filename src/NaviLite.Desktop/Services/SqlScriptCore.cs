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
}
