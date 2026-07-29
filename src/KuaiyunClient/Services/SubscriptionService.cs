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

        IReadOnlyList<ProxyNode> nodes = NormalizeNodes(_parser.Parse(yaml));
        if (nodes.Count == 0)
        {
            throw new SubscriptionParseException("订阅中没有可用线路。");
        }

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

    private static IReadOnlyList<ProxyNode> NormalizeNodes(IEnumerable<ProxyNode> source)
    {
        return source
            .Where(node => !SubscriptionNodeFilter.ShouldHide(node.Name))
            .Select(node =>
            {
                NodeLocation location = CountryFlagResolver.Resolve(node.Name);
                return new ProxyNode
                {
                    Name = node.Name,
                    DisplayName = NodeDisplayNameFormatter.Format(node.Name, location),
                    GroupName = location.CountryName,
                    Type = node.Type,
                    Server = node.Server,
                    CountryCode = location.CountryCode,
                    CountryName = location.CountryName,
                    CountryFlag = location.Flag,
                    DelayMilliseconds = node.DelayMilliseconds
                };
            })
            .ToArray();
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
