using NaviLite.Desktop.Models;

namespace NaviLite.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    public AsyncCommand FormatSqlCommand { get; }
    public event Action<SqlFormatEdit, bool>? SqlFormattingRequested;

    private async Task FormatSqlAsync()
    {
        string original = SqlText;
        int caret = SqlCaretUtf16, selectionStart = SqlSelectionStartUtf16, selectionLength = SqlSelectionLengthUtf16;
        bool escapes = SqlBackslashEscapes, ansi = SqlAnsiQuotes;
        SqlFormatEdit? edit = null;
        await RunAsync(async () => edit = await _core.FormatSqlScriptAsync(original, caret, selectionStart, selectionLength, escapes, ansi));
        if (edit is null) return;
        if (SqlText != original || SqlBackslashEscapes != escapes || SqlAnsiQuotes != ansi)
        {
            Status = "The editor changed while formatting; the formatting result was discarded.";
            return;
        }
        if (!edit.Changed)
        {
            Status = "SQL layout is already formatted, or the selection contains only comments/whitespace.";
            return;
        }
        try
        {
            var apply = SqlFormattingRequested ?? throw new InvalidOperationException("The SQL editor is not available.");
            apply(edit, selectionLength != 0);
            Status = $"Formatted {edit.FormattedStatements} SELECT statement(s). Undo with Ctrl+Z. No SQL was executed.";
        }
        catch (Exception) { Report("The formatting edit could not be applied. Check the editor document before continuing."); }
    }
}
