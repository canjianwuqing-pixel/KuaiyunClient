using KuaiyunClient.Models;

namespace KuaiyunClient.Services;

public interface IConfigService
{
    Task<ConfigLoadResult> LoadAsync(CancellationToken cancellationToken = default);
}

public sealed record ConfigLoadResult(AppConfig Config, string Source, bool FromCache);
