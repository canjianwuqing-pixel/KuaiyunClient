namespace KuaiyunClient.Models;

public sealed class UserSession
{
    public string Email { get; init; } = string.Empty;

    public string AuthData { get; init; } = string.Empty;

    public string SubscriptionUrl { get; init; } = string.Empty;

    public long UploadBytes { get; init; }

    public long DownloadBytes { get; init; }

    public long TransferEnableBytes { get; init; }

    public long ExpiredAt { get; init; }
}
