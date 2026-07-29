namespace KuaiyunClient.Models;

public sealed class ProxyNode
{
    /// <summary>
    /// Mihomo 配置中的原始节点名称。节点切换时必须使用这个名称。
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 在界面中显示的名称，包含自动识别的国家或地区旗帜。
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    public string GroupName { get; init; } = string.Empty;

    public string Type { get; init; } = string.Empty;

    public string? Server { get; init; }

    public string CountryCode { get; init; } = string.Empty;

    public string CountryName { get; init; } = "其他地区";

    public string CountryFlag { get; init; } = "🌐";

    public int? DelayMilliseconds { get; set; }

    public string DelayText => DelayMilliseconds is int delay
        ? $"{delay} ms"
        : "未测速";

    public bool IsSelected { get; set; }
}
