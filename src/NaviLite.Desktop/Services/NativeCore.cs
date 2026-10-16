using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NaviLite.Desktop.Interop;
using NaviLite.Desktop.Models;

namespace NaviLite.Desktop.Services;

public sealed unsafe class NativeCore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<CoreInfo> GetInfoAsync() => Task.Run(() => Read<CoreInfo>(NativeMethods.navilite_core_info()));
    public Task<List<ConnectionProfile>> ListProfilesAsync() => Task.Run(() => Read<List<ConnectionProfile>>(NativeMethods.navilite_profiles_list()));
    public Task<ConnectionProfile> SaveProfileAsync(ConnectionProfile profile, string? password) =>
        Task.Run(() => WithProfile<ConnectionProfile>(profile, password, NativeMethods.navilite_profile_save));
    public Task<ConnectionInfo> TestConnectionAsync(ConnectionProfile profile, string? password) =>
        Task.Run(() => WithProfile<ConnectionInfo>(profile, password, NativeMethods.navilite_conn_test));
    public Task<ConnectionInfo> OpenConnectionAsync(ConnectionProfile profile, string? password) =>
        Task.Run(() => WithProfile<ConnectionInfo>(profile, password, NativeMethods.navilite_conn_open));
    public Task CloseConnectionAsync(ulong handle) =>
        Task.Run(() => Read<JsonElement>(NativeMethods.navilite_conn_close(handle)));
    public Task<List<string>> ListDatabasesAsync(ulong handle) =>
        Task.Run(() => Read<List<string>>(NativeMethods.navilite_databases_list(handle)));
    public Task DeleteProfileAsync(string id) => Task.Run(() => DeleteProfile(id));

    private unsafe delegate FfiResult ProfileCall(byte* config, byte* password);

    private static unsafe T WithProfile<T>(ConnectionProfile profile, string? password, ProfileCall call)
    {
        byte[] config = Utf8(JsonSerializer.Serialize(profile, JsonOptions));
        byte[]? secret = password is null ? null : Utf8(password);
        try
        {
            fixed (byte* configPtr = config)
            fixed (byte* secretPtr = secret)
            {
                return Read<T>(call(configPtr, secretPtr));
            }
        }
        finally
        {
            if (secret is not null) CryptographicOperations.ZeroMemory(secret);
        }
    }

    private static unsafe void DeleteProfile(string id)
    {
        byte[] bytes = Utf8(id);
        fixed (byte* pointer = bytes)
            Read<JsonElement>(NativeMethods.navilite_profile_delete(pointer));
    }

    private static byte[] Utf8(string value)
    {
        if (value.Contains('\0')) throw new ArgumentException("Values cannot contain a NUL character.");
        return Encoding.UTF8.GetBytes(value + '\0');
    }

    private static unsafe T Read<T>(FfiResult result)
    {
        try
        {
            string json = Marshal.PtrToStringUTF8((nint)result.data)
                ?? throw new InvalidOperationException("The native core returned no response.");
            if (result.code != 0)
            {
                using var document = JsonDocument.Parse(json);
                throw new InvalidOperationException(document.RootElement.GetProperty("message").GetString());
            }
            return JsonSerializer.Deserialize<T>(json, JsonOptions)!;
        }
        finally
        {
            NativeMethods.navilite_result_free(result.data);
        }
    }
}
