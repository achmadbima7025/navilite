namespace NaviLite.Desktop.Models;

public sealed record SqlStatementSpan(int Index, int StartUtf16, int LengthUtf16, int StartLine, string Preview);
public sealed record SqlScriptAnalysis(List<SqlStatementSpan> Statements, int? CurrentIndex, int DelimiterCount, int DocumentLengthUtf16);
