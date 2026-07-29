namespace KuaiyunClient.Services;

public interface ISystemProxyService
{
    bool IsEnabled { get; }

    Task EnableAsync(string proxyAddress, CancellationToken cancellationToken = default);

    Task RestoreAsync(CancellationToken cancellationToken = default);
}
