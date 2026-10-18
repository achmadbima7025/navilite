using System.Text.Json;
using NaviLite.Desktop.Interop;
using NaviLite.Desktop.Models;

namespace NaviLite.Desktop.Services;

public sealed unsafe partial class NativeCore
{
    public Task<string> CopyRowsAsInsertAsync(NativePage page, IReadOnlyList<NativeRow> rows) => Task.Run(() =>
        page.WithPageLease(pageId =>
        {
            if (rows.Count is < 1 or > 1000 || rows.Any(row => !row.BelongsTo(page)))
                throw new InvalidOperationException("Select records in the current data page.");
            using var buffer = new LimitedMemoryStream(8 * 1024 * 1024, "SQL copy input is limited to 8 MiB. Select fewer records.");
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartArray();
                foreach (var row in rows)
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("index", row.Index);
                    writer.WriteStartArray("cells");
                    for (int column = 0; column < page.Columns.Count; column++)
                    {
                        var cell = page.PreviewValue(row.Index, column);
                        writer.WriteStartObject();
                        if (cell.IsNull) writer.WriteNull("value");
                        else writer.WriteString("value", cell.Text);
                        writer.WriteBoolean("truncated", cell.IsTruncated);
                        writer.WriteEndObject();
                        if (writer.BytesCommitted + writer.BytesPending > 8 * 1024 * 1024)
                            throw new InvalidOperationException("SQL copy input is limited to 8 MiB. Select fewer records.");
                    }
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.Flush();
            }
            // The terminator is excluded from the native payload-size limit.
            byte[] payload = new byte[checked((int)buffer.Length + 1)];
            buffer.GetBuffer().AsSpan(0, (int)buffer.Length).CopyTo(payload);
            fixed (byte* pointer = payload)
                return Read<string>(NativeMethods.navilite_rows_insert_sql(pageId, pointer));
        }));
}
