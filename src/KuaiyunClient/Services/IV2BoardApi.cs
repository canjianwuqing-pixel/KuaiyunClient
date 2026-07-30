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

    Task<IReadOnlyList<V2BoardPlan>> GetPlansAsync(
        AppConfig config,
        UserSession session,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<V2BoardNotice>> GetNoticesAsync(
        AppConfig config,
        UserSession session,
        CancellationToken cancellationToken = default);

    Task<string> CreateOrderAsync(
        AppConfig config,
        UserSession session,
        int planId,
        string cycle,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<V2BoardPaymentMethod>> GetPaymentMethodsAsync(
        AppConfig config,
        UserSession session,
        CancellationToken cancellationToken = default);

    Task<V2BoardCheckoutResult> CheckoutOrderAsync(
        AppConfig config,
        UserSession session,
        string tradeNo,
        int paymentMethodId,
        CancellationToken cancellationToken = default);
}
