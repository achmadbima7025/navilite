using System.IO;
using System.Text;

namespace NaviLite.Desktop.Services;

public static class SqlScriptFiles
{
    public const int MaximumBytes = 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static async Task<string> ReadAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        if (stream.Length > MaximumBytes) throw TooLarge();
        byte[] bytes = new byte[MaximumBytes + 1];
        int length = 0;
        while (length < bytes.Length)
        {
            int read = await stream.ReadAsync(bytes.AsMemory(length));
            if (read == 0) break;
            length += read;
        }
        if (length > MaximumBytes) throw TooLarge();
        string text;
        try { text = Utf8.GetString(bytes, 0, length); }
        catch (DecoderFallbackException) { throw new InvalidOperationException("SQL files must use UTF-8 encoding."); }
        if (text.StartsWith('\uFEFF')) text = text[1..];
        Validate(text);
        return text;
    }

    public static async Task WriteAsync(string path, string script)
    {
        Validate(script);
        byte[] bytes;
        try { bytes = Utf8.GetBytes(script); }
        catch (EncoderFallbackException) { throw new InvalidOperationException("SQL files must contain valid Unicode text."); }
        if (bytes.Length > MaximumBytes) throw TooLarge();
        string target = Path.GetFullPath(path);
        string namespacePath = target.Replace('/', '\\');
        if (namespacePath.StartsWith(@"\\?\", StringComparison.Ordinal) || namespacePath.StartsWith(@"\\.\", StringComparison.Ordinal))
            throw new InvalidOperationException("Choose a standard drive or UNC path for SQL files.");
        string localRoot = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string store = Path.GetFullPath(Path.Combine(localRoot, "NaviLite", "connections.json"));
        if (new[] { store, Path.ChangeExtension(store, "lock"), Path.ChangeExtension(store, "json.tmp") }
            .Any(reserved => string.Equals(reserved, target, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Choose a SQL file outside NaviLite's active connection store.");
        string directory = Path.GetDirectoryName(target) ?? throw new InvalidOperationException("Choose a valid SQL folder.");
        string temporary = Path.Combine(directory, $".navilite-sql-{Guid.NewGuid():N}.tmp");
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

    private static void Validate(string text)
    {
        if (text.Contains('\0')) throw new InvalidOperationException("SQL scripts cannot contain NUL characters.");
    }

    private static InvalidOperationException TooLarge() => new("SQL scripts are limited to 1 MiB of UTF-8.");
}
