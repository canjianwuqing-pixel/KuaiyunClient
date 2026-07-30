namespace KuaiyunClient.Models;

public sealed class UserSession
{
    public string Email { get; set; } = string.Empty;

    public string AuthData { get; set; } = string.Empty;

    public string ApiHost { get; set; } = string.Empty;

    public string SubscriptionUrl { get; set; } = string.Empty;

    public long UploadBytes { get; set; }

    public long DownloadBytes { get; set; }

    public long TransferEnableBytes { get; set; }

    public long ExpiredAt { get; set; }

    public int? PlanId { get; set; }

    public string PlanName { get; set; } = "未订阅套餐";

    public int? ResetDay { get; set; }
}
