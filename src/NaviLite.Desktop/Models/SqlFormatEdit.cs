namespace NaviLite.Desktop.Models;

public sealed record SqlFormatEdit(int StartUtf16, int LengthUtf16, string Replacement, int CaretUtf16, int FormattedStatements, bool Changed);
