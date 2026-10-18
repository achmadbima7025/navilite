using System.IO;
using System.Text;

namespace NaviLite.Desktop.Services;

public static class ConnectionFiles
{
    private const int MaximumBytes = 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static async Task<string> ReadAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        if (stream.Length > MaximumBytes) throw new InvalidOperationException("Connection files are limited to 1 MiB.");
        byte[] bytes = new byte[MaximumBytes + 1];
        int length = 0;
        while (length < bytes.Length)
        {
            int read = await stream.ReadAsync(bytes.AsMemory(length));
            if (read == 0) break;
            length += read;
        }
        if (length > MaximumBytes) throw new InvalidOperationException("Connection files are limited to 1 MiB.");
        try { return Utf8.GetString(bytes, 0, length); }
        catch (DecoderFallbackException) { throw new InvalidOperationException("Connection files must use UTF-8 encoding."); }
    }

    public static async Task WriteAsync(string path, string document)
    {
        byte[] bytes = Utf8.GetBytes(document);
        if (bytes.Length > MaximumBytes) throw new InvalidOperationException("Connection files are limited to 1 MiB.");
        string target = Path.GetFullPath(path);
        // Device/extended spellings bypass ordinary Win32 path normalization
        // and could alias a reserved profile-store file below.
        string namespacePath = target.Replace('/', '\\');
        if (namespacePath.StartsWith(@"\\?\", StringComparison.Ordinal) || namespacePath.StartsWith(@"\\.\", StringComparison.Ordinal))
            throw new InvalidOperationException("Choose a standard drive or UNC path for connection exports. Device and extended paths are not supported.");
        string localRoot = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string store = Path.GetFullPath(Path.Combine(localRoot, "NaviLite", "connections.json"));
        if (new[] { store, Path.ChangeExtension(store, "lock"), Path.ChangeExtension(store, "json.tmp") }
            .Any(reserved => string.Equals(reserved, target, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Choose an export file outside NaviLite's active connection store.");
        string directory = Path.GetDirectoryName(target) ?? throw new InvalidOperationException("Choose a valid export folder.");
        string temporary = Path.Combine(directory, $".navilite-export-{Guid.NewGuid():N}.tmp");
        bool created = false;
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true))
            {
                created = true;
                await stream.WriteAsync(bytes);
                await stream.FlushAsync();
            }
            File.Move(temporary, target, true);
            created = false;
        }
        finally
        {
            if (created)
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
