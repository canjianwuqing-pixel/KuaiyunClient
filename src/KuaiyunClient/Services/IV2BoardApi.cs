using KuaiyunClient.Models;

namespace KuaiyunClient.Services;

public interface IV2BoardApi
{
    Task<UserSession> LoginAsync(
        AppConfig config,
        string email,
        string password,
        CancellationToken cancellationToken = default);

    Task SendEmailVerificationAsync(
        AppConfig config,
        string email,
        bool forPasswordReset,
        CancellationToken cancellationToken = default);

    Task<UserSession> RegisterAsync(
        AppConfig config,
        string email,
        string emailCode,
        string password,
        string? inviteCode,
        CancellationToken cancellationToken = default);

    Task ResetPasswordAsync(
        AppConfig config,
        string email,
        string emailCode,
        string password,
        CancellationToken cancellationToken = default);

    Task<string> DownloadSubscriptionAsync(
        AppConfig config,
        UserSession session,
        CancellationToken cancellationToken = default);
}
