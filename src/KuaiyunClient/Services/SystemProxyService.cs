using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace KuaiyunClient.Services;

public sealed class SystemProxyService : ISystemProxyService, IDisposable
{
    private const string InternetSettingsPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    private const int InternetOptionRefresh = 37;
    private const int InternetOptionSettingsChanged = 39;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _backupPath;
    private bool _disposed;

    public SystemProxyService()
    {
        string stateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KuaiyunClient",
            "state");

        Directory.CreateDirectory(stateDirectory);
        _backupPath = Path.Combine(stateDirectory, "proxy-backup.json");
    }

    public string BackupPath => _backupPath;

    public bool HasPendingBackup => File.Exists(_backupPath);

    public bool IsEnabled
    {
        get
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(InternetSettingsPath, writable: false);
            return ReadInt32(key, "ProxyEnable") is > 0;
        }
    }

    public async Task EnableAsync(
        string proxyAddress,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateProxyAddress(proxyAddress);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_backupPath))
            {
                ProxyRegistryBackup backup = CaptureCurrentSettings();
                await SaveBackupAsync(backup, cancellationToken);
            }

            using RegistryKey key = Registry.CurrentUser.CreateSubKey(
                InternetSettingsPath,
                writable: true)
                ?? throw new SystemProxyException("无法打开 Windows Internet Settings 注册表项。");

            key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
            key.SetValue("ProxyServer", proxyAddress.Trim(), RegistryValueKind.String);
            key.SetValue(
                "ProxyOverride",
                "<local>;localhost;127.*;10.*;172.16.*;172.17.*;172.18.*;172.19.*;172.2*;172.30.*;172.31.*;192.168.*",
                RegistryValueKind.String);

            // 手动代理启用期间移除 PAC 地址，恢复时会写回原值。
            key.DeleteValue("AutoConfigURL", throwOnMissingValue: false);
            key.Flush();

            NotifyInternetSettingsChanged();
        }
        catch
        {
            // 如果启用到一半失败，尽最大努力恢复原设置。
            try
            {
                await RestoreNoLockAsync(CancellationToken.None);
            }
            catch
            {
                // 保留原始异常。
            }

            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await RestoreNoLockAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private ProxyRegistryBackup CaptureCurrentSettings()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(InternetSettingsPath, writable: false);

        return new ProxyRegistryBackup
        {
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ProxyEnable = CaptureInt32(key, "ProxyEnable"),
            ProxyServer = CaptureString(key, "ProxyServer"),
            ProxyOverride = CaptureString(key, "ProxyOverride"),
            AutoConfigUrl = CaptureString(key, "AutoConfigURL")
        };
    }

    private async Task SaveBackupAsync(
        ProxyRegistryBackup backup,
        CancellationToken cancellationToken)
    {
        string temporaryPath = _backupPath + ".tmp";
        string json = JsonSerializer.Serialize(backup, JsonOptions);

        await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
        File.Move(temporaryPath, _backupPath, overwrite: true);
    }

    private async Task RestoreNoLockAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_backupPath))
        {
            return;
        }

        ProxyRegistryBackup backup;
        try
        {
            string json = await File.ReadAllTextAsync(_backupPath, cancellationToken);
            backup = JsonSerializer.Deserialize<ProxyRegistryBackup>(json, JsonOptions)
                ?? throw new SystemProxyException("系统代理备份文件内容为空。");
        }
        catch (JsonException ex)
        {
            throw new SystemProxyException(
                $"系统代理备份文件损坏：{_backupPath}",
                ex);
        }

        using RegistryKey key = Registry.CurrentUser.CreateSubKey(
            InternetSettingsPath,
            writable: true)
            ?? throw new SystemProxyException("无法打开 Windows Internet Settings 注册表项。");

        RestoreInt32(key, "ProxyEnable", backup.ProxyEnable);
        RestoreString(key, "ProxyServer", backup.ProxyServer);
        RestoreString(key, "ProxyOverride", backup.ProxyOverride);
        RestoreString(key, "AutoConfigURL", backup.AutoConfigUrl);
        key.Flush();

        NotifyInternetSettingsChanged();
        File.Delete(_backupPath);
    }

    private static RegistryValueSnapshot<int> CaptureInt32(RegistryKey? key, string name)
    {
        bool exists = key?.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase) == true;
        return new RegistryValueSnapshot<int>
        {
            Exists = exists,
            Value = exists ? ReadInt32(key, name) : null
        };
    }

    private static RegistryValueSnapshot<string> CaptureString(RegistryKey? key, string name)
    {
        bool exists = key?.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase) == true;
        return new RegistryValueSnapshot<string>
        {
            Exists = exists,
            Value = exists ? key?.GetValue(name)?.ToString() : null
        };
    }

    private static int? ReadInt32(RegistryKey? key, string name)
    {
        object? value = key?.GetValue(name);
        if (value is null)
        {
            return null;
        }

        try
        {
            return Convert.ToInt32(value);
        }
        catch (FormatException)
        {
            return null;
        }
        catch (InvalidCastException)
        {
            return null;
        }
    }

    private static void RestoreInt32(
        RegistryKey key,
        string name,
        RegistryValueSnapshot<int> snapshot)
    {
        if (!snapshot.Exists)
        {
            key.DeleteValue(name, throwOnMissingValue: false);
            return;
        }

        key.SetValue(name, snapshot.Value ?? 0, RegistryValueKind.DWord);
    }

    private static void RestoreString(
        RegistryKey key,
        string name,
        RegistryValueSnapshot<string> snapshot)
    {
        if (!snapshot.Exists)
        {
            key.DeleteValue(name, throwOnMissingValue: false);
            return;
        }

        key.SetValue(name, snapshot.Value ?? string.Empty, RegistryValueKind.String);
    }

    private static void ValidateProxyAddress(string proxyAddress)
    {
        string value = proxyAddress.Trim();
        if (!Uri.TryCreate("http://" + value, UriKind.Absolute, out Uri? uri)
            || uri.Port is <= 0 or > 65535
            || !uri.IsLoopback)
        {
            throw new ArgumentException(
                "系统代理地址必须是本机地址，例如 127.0.0.1:7890。",
                nameof(proxyAddress));
        }
    }

    private static void NotifyInternetSettingsChanged()
    {
        InternetSetOption(IntPtr.Zero, InternetOptionSettingsChanged, IntPtr.Zero, 0);
        InternetSetOption(IntPtr.Zero, InternetOptionRefresh, IntPtr.Zero, 0);
    }

    [DllImport("wininet.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InternetSetOption(
        IntPtr internetHandle,
        int option,
        IntPtr buffer,
        int bufferLength);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
    }
}

public sealed class ProxyRegistryBackup
{
    public DateTimeOffset CreatedAtUtc { get; init; }

    public RegistryValueSnapshot<int> ProxyEnable { get; init; } = new();

    public RegistryValueSnapshot<string> ProxyServer { get; init; } = new();

    public RegistryValueSnapshot<string> ProxyOverride { get; init; } = new();

    public RegistryValueSnapshot<string> AutoConfigUrl { get; init; } = new();
}

public sealed class RegistryValueSnapshot<T>
{
    public bool Exists { get; init; }

    public T? Value { get; init; }
}

public sealed class SystemProxyException : Exception
{
    public SystemProxyException(string message)
        : base(message)
    {
    }

    public SystemProxyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
