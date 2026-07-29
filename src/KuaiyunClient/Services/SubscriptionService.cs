using KuaiyunClient.Models;
using System.Text;

namespace KuaiyunClient.Services;

public sealed class SubscriptionService
{
    private readonly SubscriptionNodeParser _parser = new();
    private readonly string _subscriptionPath;

    public SubscriptionService()
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KuaiyunClient",
            "subscription");

        Directory.CreateDirectory(directory);
        _subscriptionPath = Path.Combine(directory, "current.yaml");
    }

    public async Task<SubscriptionLoadResult> DownloadAsync(
        IV2BoardApi api,
        AppConfig config,
        UserSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(session);

        string yaml = await api.DownloadSubscriptionAsync(
            config,
            session,
            cancellationToken);

        IReadOnlyList<ProxyNode> nodes = _parser.Parse(yaml);
        await SaveAsync(yaml, cancellationToken);

        return new SubscriptionLoadResult(nodes, _subscriptionPath, yaml);
    }

    public async Task<string?> ReadCachedYamlAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_subscriptionPath))
        {
            return null;
        }

        string yaml = await File.ReadAllTextAsync(_subscriptionPath, cancellationToken);
        return string.IsNullOrWhiteSpace(yaml) ? null : yaml;
    }

    private async Task SaveAsync(
        string yaml,
        CancellationToken cancellationToken)
    {
        string temporaryPath = _subscriptionPath + ".tmp";
        await File.WriteAllTextAsync(
            temporaryPath,
            yaml,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);

        File.Move(temporaryPath, _subscriptionPath, overwrite: true);
    }
}

public sealed record SubscriptionLoadResult(
    IReadOnlyList<ProxyNode> Nodes,
    string CachePath,
    string Yaml);
