using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using NaviLite.Desktop.Interop;

namespace NaviLite.Desktop.Models;

public sealed record PageColumn(string Name, string SqlType);
public sealed class NativeRow(NativePage page, int row)
{
    public string this[int column] => page.ReadCell(row, column).Text;
    public int Index => row;
    public string Label => $"Row {page.Offset + (ulong)row + 1:N0}";
    internal bool BelongsTo(NativePage? owner) => ReferenceEquals(page, owner);
    internal NativeCell Cell(int column) => page.ReadCell(row, column);
}

public sealed unsafe class NativePage : SafeHandle
{
    private readonly nuint _length;
    private int _directory;
    public override bool IsInvalid => handle == IntPtr.Zero;
    public IReadOnlyList<PageColumn> Columns { get; private set; } = [];
    public IReadOnlyList<NativeRow> Rows { get; private set; } = [];
    public ulong Offset { get; private set; }
    public ulong NextOffset { get; private set; }
    public bool HasMore { get; private set; }
    public bool HasStableOrder { get; private set; }
    public uint TruncatedCells { get; private set; }
    public ulong ByteLength => _length;

    internal NativePage(FfiBuffer result) : base(IntPtr.Zero, true)
    {
        _length = result.length;
        SetHandle((nint)result.data);
        try
        {
            if (IsInvalid || _length > 8 * 1024 * 1024) throw new InvalidDataException("Invalid native page allocation.");
            var bytes = new ReadOnlySpan<byte>((void*)handle, checked((int)_length));
            if (result.code != 0)
            {
                using var error = JsonDocument.Parse(bytes.ToArray());
                throw new InvalidOperationException(error.RootElement.GetProperty("message").GetString());
            }
            Parse(bytes);
        }
        catch { Dispose(); throw; }
    }

    private static uint U32(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, 4));
    private static int Position(ReadOnlySpan<byte> bytes, int offset) => checked((int)U32(bytes, offset));

    private static string ReadString(ReadOnlySpan<byte> bytes, ref int offset)
    {
        int length = Position(bytes, offset);
        offset = checked(offset + 4);
        string value = Encoding.UTF8.GetString(bytes.Slice(offset, length));
        offset = checked(offset + length);
        return value;
    }

    private void Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 40 || !bytes[..4].SequenceEqual("NVLR"u8) || U32(bytes, 4) != 1)
            throw new InvalidDataException("Unsupported native row frame.");
        int columnCount = Position(bytes, 8), rowCount = Position(bytes, 12);
        if (columnCount is < 1 or > 1024 || rowCount is < 0 or > 1000) throw new InvalidDataException("Invalid row frame dimensions.");
        Offset = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(16, 8));
        NextOffset = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(24, 8));
        if (NextOffset != checked(Offset + (ulong)rowCount)) throw new InvalidDataException("Invalid row frame pagination.");
        uint flags = U32(bytes, 32);
        HasMore = (flags & 1) != 0;
        HasStableOrder = (flags & 2) != 0;
        TruncatedCells = U32(bytes, 36);
        var columns = new PageColumn[columnCount];
        int cursor = 40;
        for (int column = 0; column < columnCount; column++) columns[column] = new(ReadString(bytes, ref cursor), ReadString(bytes, ref cursor));
        Columns = columns;
        _directory = cursor;
        int directoryEnd = checked(cursor + (rowCount + 1) * 4);
        if (directoryEnd > bytes.Length || Position(bytes, cursor) != directoryEnd || Position(bytes, cursor + rowCount * 4) != bytes.Length)
            throw new InvalidDataException("Invalid row offset directory.");
        for (int row = 0; row < rowCount; row++)
        {
            int start = Position(bytes, cursor + row * 4), end = Position(bytes, cursor + (row + 1) * 4);
            if (start < directoryEnd || end < start + columnCount * 4 || end > bytes.Length) throw new InvalidDataException("Invalid row boundary.");
            int cellCursor = columnCount * 4;
            for (int column = 0; column < columnCount; column++)
            {
                int cell = Position(bytes, start + column * 4);
                if (cell != cellCursor || cell > end - start - 5) throw new InvalidDataException("Invalid cell offset.");
                int length = Position(bytes, start + cell + 1);
                byte tag = bytes[start + cell];
                if ((tag & 127) > 2 || ((tag & 127) == 0 && length != 0)) throw new InvalidDataException("Invalid cell tag.");
                cellCursor = checked(cell + 5 + length);
                if (cellCursor > end - start) throw new InvalidDataException("Invalid cell length.");
            }
            if (start + cellCursor != end) throw new InvalidDataException("Invalid row payload size.");
        }
        Rows = Enumerable.Range(0, rowCount).Select(row => new NativeRow(this, row)).ToArray();
    }

    internal NativeCell ReadCell(int row, int column)
    {
        if ((uint)row >= (uint)Rows.Count || (uint)column >= (uint)Columns.Count) return new("");
        bool added = false;
        try
        {
            DangerousAddRef(ref added);
            var bytes = new ReadOnlySpan<byte>((void*)handle, checked((int)_length));
            int start = Position(bytes, _directory + row * 4);
            int cell = checked(start + Position(bytes, start + column * 4));
            byte tag = bytes[cell];
            int length = Position(bytes, cell + 1);
            var payload = bytes.Slice(cell + 5, length);
            string value = (tag & 127) switch { 0 => "NULL", 2 => "0x" + Convert.ToHexString(payload), _ => Encoding.UTF8.GetString(payload) };
            return new((tag & 128) != 0 ? value + " …" : value, (tag & 127) == 0, (tag & 127) == 2, (tag & 128) != 0);
        }
        catch (ObjectDisposedException) { return new(""); }
        finally { if (added) DangerousRelease(); }
    }

    protected override bool ReleaseHandle()
    {
        NativeMethods.navilite_buffer_free((byte*)handle, _length);
        return true;
    }
}
