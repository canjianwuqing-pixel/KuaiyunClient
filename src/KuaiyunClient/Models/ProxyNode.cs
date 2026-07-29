namespace KuaiyunClient.Models;

public sealed class ProxyNode
{
    public string Name { get; init; } = string.Empty;

    public string GroupName { get; init; } = string.Empty;

    public string Type { get; init; } = string.Empty;

    public int? DelayMilliseconds { get; set; }

    public bool IsSelected { get; set; }
}
