namespace KuaiyunClient.Services;

public interface ISystemProxyService
{
    bool IsEnabled { get; }

    bool HasPendingBackup { get; }

    string BackupPath { get; }

    Task EnableAsync(string proxyAddress, CancellationToken cancellationToken = default);

    Task RestoreAsync(CancellationToken cancellationToken = default);
}
