using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace KuaiyunClient.Services;

public sealed class LoginCredentialService
{
    private const int CryptProtectUiForbidden = 0x1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _credentialPath;

    public LoginCredentialService()
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KuaiyunClient",
            "settings");
        Directory.CreateDirectory(directory);
        _credentialPath = Path.Combine(directory, "login-credential.json");
    }

    public async Task SaveAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
        {
            throw new ArgumentException("登录凭据不能为空。");
        }

        byte[] protectedPassword = Protect(Encoding.UTF8.GetBytes(password));
        StoredCredential credential = new(email.Trim(), Convert.ToBase64String(protectedPassword));
        string json = JsonSerializer.Serialize(credential, JsonOptions);
        string temporaryPath = _credentialPath + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
        File.Move(temporaryPath, _credentialPath, overwrite: true);
    }

    public async Task<SavedLoginCredential?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_credentialPath))
        {
            return null;
        }

        try
        {
            string json = await File.ReadAllTextAsync(_credentialPath, cancellationToken);
            StoredCredential? stored = JsonSerializer.Deserialize<StoredCredential>(json, JsonOptions);
            if (stored is null
                || string.IsNullOrWhiteSpace(stored.Email)
                || string.IsNullOrWhiteSpace(stored.ProtectedPassword))
            {
                return null;
            }

            byte[] cipher = Convert.FromBase64String(stored.ProtectedPassword);
            string password = Encoding.UTF8.GetString(Unprotect(cipher));
            return string.IsNullOrEmpty(password)
                ? null
                : new SavedLoginCredential(stored.Email, password);
        }
        catch (Exception ex) when (ex is IOException
                                   or JsonException
                                   or FormatException
                                   or InvalidOperationException)
        {
            return null;
        }
    }

    public Task DeleteAsync()
    {
        try
        {
            if (File.Exists(_credentialPath))
            {
                File.Delete(_credentialPath);
            }
        }
        catch (IOException)
        {
            // 清理失败不应阻止用户继续退出或登录。
        }

        return Task.CompletedTask;
    }

    private static byte[] Protect(byte[] plainBytes)
    {
        return Transform(plainBytes, protect: true);
    }

    private static byte[] Unprotect(byte[] protectedBytes)
    {
        return Transform(protectedBytes, protect: false);
    }

    private static byte[] Transform(byte[] inputBytes, bool protect)
    {
        DataBlob input = default;
        DataBlob output = default;

        try
        {
            input.Size = inputBytes.Length;
            input.Data = Marshal.AllocHGlobal(inputBytes.Length);
            Marshal.Copy(inputBytes, 0, input.Data, inputBytes.Length);

            bool success = protect
                ? CryptProtectData(
                    ref input,
                    null,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out output)
                : CryptUnprotectData(
                    ref input,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out output);

            if (!success)
            {
                throw new InvalidOperationException(
                    "Windows 无法保护登录凭据。",
                    new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
            }

            byte[] result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, output.Size);
            return result;
        }
        finally
        {
            if (input.Data != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(input.Data);
            }

            if (output.Data != IntPtr.Zero)
            {
                LocalFree(output.Data);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string? description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        IntPtr description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    private sealed record StoredCredential(string Email, string ProtectedPassword);
}

public sealed record SavedLoginCredential(string Email, string Password);
