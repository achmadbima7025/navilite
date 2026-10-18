using System.IO;
using System.Text;
using System.Text.Json;
using NaviLite.Desktop.Models;

namespace NaviLite.Desktop.Services;

public enum RowCopyFormat { Csv, Tsv, Json, SqlInsert }

public static class RowCopy
{
    public static string Format(NativePage page, IReadOnlyList<NativeRow> rows, RowCopyFormat format)
    {
        if (format is not (RowCopyFormat.Csv or RowCopyFormat.Tsv or RowCopyFormat.Json))
            throw new ArgumentException("SQL row copying must use the native SQL formatter.", nameof(format));
        if (rows.Count == 0 || rows.Any(row => !row.BelongsTo(page)))
            throw new InvalidOperationException("Select records in the current data page.");
        using var buffer = new LimitedMemoryStream(8 * 1024 * 1024, "Clipboard output is limited to 8 MiB. Select fewer records.");
        if (format == RowCopyFormat.Json)
        {
            using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true });
            writer.WriteStartArray();
            foreach (var row in rows)
            {
                writer.WriteStartObject();
                for (int column = 0; column < page.Columns.Count; column++)
                {
                    var value = Read(page, row, column);
                    writer.WritePropertyName(page.Columns[column].Name);
                    if (value.IsNull) writer.WriteNullValue();
                    else writer.WriteStringValue(value.IsBinary ? "0x" + value.Text : value.Text);
                }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.Flush();
        }
        else
        {
            char delimiter = format == RowCopyFormat.Csv ? ',' : '\t';
            using var writer = new StreamWriter(buffer, new UTF8Encoding(false), leaveOpen: true) { NewLine = "\r\n" };
            for (int column = 0; column < page.Columns.Count; column++)
            {
                if (column > 0) writer.Write(delimiter);
                Field(writer, page.Columns[column].Name);
            }
            writer.WriteLine();
            foreach (var row in rows)
            {
                for (int column = 0; column < page.Columns.Count; column++)
                {
                    if (column > 0) writer.Write(delimiter);
                    var value = Read(page, row, column);
                    if (value.IsNull) writer.Write("\\N");
                    else Field(writer, value.IsBinary ? "0x" + value.Text : value.Text);
                }
                writer.WriteLine();
            }
            writer.Flush();
        }
        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, checked((int)buffer.Length));
    }

    private static CellValue Read(NativePage page, NativeRow row, int column)
    {
        var value = page.PreviewValue(row.Index, column);
        if (value.IsTruncated)
            throw new InvalidOperationException($"{row.Label}, column {page.Columns[column].Name}, is shortened. No rows were copied. Inspect and save the cell value, or select records with complete values.");
        return value;
    }

    private static void Field(TextWriter writer, string text)
    {
        if (text.Contains('\0'))
            throw new InvalidOperationException("CSV/TSV clipboard copying cannot represent embedded NUL characters. Choose JSON or save the cell's loaded bytes in the inspector.");
        writer.Write('"');
        writer.Write(text.Replace("\"", "\"\""));
        writer.Write('"');
    }

}
