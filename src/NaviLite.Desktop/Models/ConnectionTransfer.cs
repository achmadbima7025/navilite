namespace NaviLite.Desktop.Models;

public sealed record PortableConnection(string Name, string Host, int Port, string Username, string Environment, bool UseTls, string? CaCertificate)
{
    public string Endpoint => $"{Username}@{Host}:{Port}";
    public string Security => !UseTls ? "TCP" : CaCertificate is null ? "TLS · system trust" : "TLS · CA file path";
}

public sealed record ConnectionImportCandidate(PortableConnection Connection, bool Duplicate, string PlannedName);
public sealed record ConnectionImportPreview(IReadOnlyList<ConnectionImportCandidate> Candidates);
public sealed record ConnectionImportResult(int Imported, int Skipped, string? FirstImportedId);
