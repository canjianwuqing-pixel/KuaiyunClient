using KuaiyunClient.Models;

namespace KuaiyunClient.Services;

public interface IV2BoardApi
{
    Task<UserSession> LoginAsync(
        AppConfig config,
        string email,
        string password,
        CancellationToken cancellationToken = default);

    Task<string> DownloadSubscriptionAsync(
        AppConfig config,
        UserSession session,
        CancellationToken cancellationToken = default);
}
