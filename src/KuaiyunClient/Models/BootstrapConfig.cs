namespace KuaiyunClient.Models;

public sealed class BootstrapConfig
{
    public List<string> CloudConfig { get; set; } = [];

    public int CloudUpdateHours { get; set; } = 3;
}
