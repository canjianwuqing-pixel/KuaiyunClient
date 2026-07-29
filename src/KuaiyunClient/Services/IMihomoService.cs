using KuaiyunClient.Models;

namespace KuaiyunClient.Services;

public interface IMihomoService
{
    bool IsRunning { get; }

    Task StartAsync(string subscriptionYaml, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProxyNode>> GetNodesAsync(CancellationToken cancellationToken = default);

    Task SelectNodeAsync(ProxyNode node, CancellationToken cancellationToken = default);
}
