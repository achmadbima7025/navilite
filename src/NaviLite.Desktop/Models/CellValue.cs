using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Text.Json;
using NaviLite.Desktop.Interop;

namespace NaviLite.Desktop.Models;

public sealed class CellValue(byte[] bytes, bool isNull, bool isBinary, bool isTruncated, ulong? originalBytes)
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    public byte[] Bytes { get; } = bytes;
    public bool IsNull { get; } = isNull;
    public bool IsBinary { get; } = isBinary;
    public bool IsTruncated { get; } = isTruncated;
    public ulong? OriginalBytes { get; } = originalBytes;
    public string Text { get; } = isNull ? "NULL" : isBinary ? Convert.ToHexString(bytes) : StrictUtf8.GetString(bytes);

    internal static unsafe CellValue FromNative(FfiBuffer result)
    {
        try
        {
            if (result.data == null || result.length > 1024 * 1024 + 24)
                throw new InvalidDataException("Invalid native cell allocation.");
            var frame = new ReadOnlySpan<byte>(result.data, checked((int)result.length));
            if (result.code != 0)
            {
                using var error = JsonDocument.Parse(frame.ToArray());
                throw new InvalidOperationException(error.RootElement.GetProperty("message").GetString());
            }
            if (frame.Length < 24 || !frame[..4].SequenceEqual("NVLC"u8)
                || BinaryPrimitives.ReadUInt32LittleEndian(frame[4..]) != 1
                || BinaryPrimitives.ReadUInt32LittleEndian(frame[12..]) != 0)
                throw new InvalidDataException("Unsupported native cell frame.");
            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(frame[8..]);
            uint kind = flags & 127;
            ulong original = BinaryPrimitives.ReadUInt64LittleEndian(frame[16..]);
            var payload = frame[24..];
            if ((flags & ~131u) != 0 || kind > 2 || original < (ulong)payload.Length
                || (kind == 0 && (original != 0 || payload.Length != 0))
                || ((flags & 128) != 0) != (original > (ulong)payload.Length))
                throw new InvalidDataException("Invalid native cell payload.");
            return new(payload.ToArray(), kind == 0, kind == 2, (flags & 128) != 0, original);
        }
        finally { NativeMethods.navilite_buffer_free(result.data, result.length); }
    }
}

public sealed record CellInspection(string Title, string SqlType, CellValue Value, string Source);
