namespace KuaiyunClient.Services;

public static class SubscriptionNodeFilter
{
    private static readonly string[] HiddenKeywords =
    [
        "剩余流量",
        "流量剩余",
        "总流量",
        "距离下次重置",
        "重置剩余",
        "套餐到期",
        "到期时间"
    ];

    public static bool ShouldHide(string? nodeName)
    {
        if (string.IsNullOrWhiteSpace(nodeName))
        {
            return true;
        }

        return HiddenKeywords.Any(keyword =>
            nodeName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }
}
