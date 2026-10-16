using NaviLite.Desktop.Interop;
using NaviLite.Desktop.Models;
using System.Text.Json;

namespace NaviLite.Desktop.Services;

public sealed unsafe partial class NativeCore
{
    public Task<List<SchemaObject>> ListObjectsAsync(ulong handle, SchemaNode node) => Task.Run(() =>
    {
        byte[] database = Utf8(node.Database), table = Utf8(node.Table), category = Utf8(node.Kind);
        fixed (byte* db = database, name = table, kind = category)
            return Read<List<SchemaObject>>(NativeMethods.navilite_schema_objects(handle, db, name, kind));
    });

    public Task<string> GetDdlAsync(ulong handle, string database, string table) => Task.Run(() =>
    {
        byte[] databaseBytes = Utf8(database), tableBytes = Utf8(table);
        fixed (byte* db = databaseBytes, name = tableBytes)
            return Read<string>(NativeMethods.navilite_object_ddl(handle, db, name));
    });

    public Task<NativePage> ReadPageAsync(ulong handle, string database, string table, ulong offset, PageQuery? query = null, uint pageSize = 1000) => Task.Run(() =>
    {
        byte[] databaseBytes = Utf8(database), tableBytes = Utf8(table), options = Utf8(JsonSerializer.Serialize(query ?? new(), JsonOptions));
        fixed (byte* db = databaseBytes, name = tableBytes, queryOptions = options)
            return new NativePage(NativeMethods.navilite_table_page_query(handle, db, name, offset, pageSize, queryOptions));
    });
}
