using System.IO;

namespace NaviLite.Desktop.Services;

internal sealed class LimitedMemoryStream(int maximumBytes, string message) : MemoryStream
{
    private void Check(int count)
    {
        if (Length + count > maximumBytes) throw new InvalidOperationException(message);
    }
    public override void Write(byte[] buffer, int offset, int count) { Check(count); base.Write(buffer, offset, count); }
    public override void Write(ReadOnlySpan<byte> buffer) { Check(buffer.Length); base.Write(buffer); }
    public override void WriteByte(byte value) { Check(1); base.WriteByte(value); }
}
