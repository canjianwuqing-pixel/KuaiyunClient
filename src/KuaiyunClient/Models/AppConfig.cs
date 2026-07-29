namespace KuaiyunClient.Models;

public sealed class AppConfig
{
    public string AppName { get; set; } = "快云加速";

    public string? AppLogo { get; set; }

    public string? HomePage { get; set; }

    public string? TelegramGroup { get; set; }

    public string? SupportApi { get; set; }

    public string? UpdateUrl { get; set; }

    public string UserAgent { get; set; } = "kuaiyun";

    public List<string> RemoteHosts { get; set; } = [];

    public List<string> BuiltInProxy { get; set; } = [];
}
