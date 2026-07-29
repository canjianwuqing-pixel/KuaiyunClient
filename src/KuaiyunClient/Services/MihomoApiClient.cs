using KuaiyunClient.Models;
using System.Net.Http.Json;
using System.Text.Json;

namespace KuaiyunClient.Services;

public sealed class MihomoApiClient : IDisposable
{
    private static readonly string[] PreferredGroupNames =
    [
        "GLOBAL",
        "节点选择",
        "代理选择",
        "选择代理",
        "手动选择",
        "Proxy",
        "PROXY"
    ];

    private readonly HttpClient _httpClient;

    public MihomoApiClient(int controllerPort, string secret)
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{controllerPort}/"),
            Timeout = TimeSpan.FromSeconds(2)
        };

        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(
            "Authorization",
            $"Bearer {secret}");
    }

    public async Task WaitUntilReadyAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        Exception? lastError = null;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using HttpResponseMessage response = await _httpClient.GetAsync(
                    "version",
                    cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    return;
                }

                lastError = new MihomoApiException(
                    $"Controller 返回 HTTP {(int)response.StatusCode}。");
            }
            catch (Exception ex) when (ex is HttpRequestException
                                       or TaskCanceledException)
            {
                lastError = ex;
            }

            await Task.Delay(250, cancellationToken);
        }

        throw new MihomoApiException(
            "Mihomo 已启动，但 Controller 在规定时间内没有就绪。",
            lastError);
    }

    public async Task<IReadOnlyList<ProxyNode>> GetNodesAsync(
        CancellationToken cancellationToken = default)
    {
        using JsonDocument json = await GetProxiesDocumentAsync(cancellationToken);
        JsonElement proxies = json.RootElement.GetProperty("proxies");
        List<ProxyNode> nodes = [];

        foreach (JsonProperty property in proxies.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string type = ReadString(property.Value, "type") ?? string.Empty;
            if (IsGroupType(type) || IsBuiltInProxy(property.Name))
            {
                continue;
            }

            NodeLocation location = CountryFlagResolver.Resolve(property.Name);
            int? delay = ReadLastDelay(property.Value);

            nodes.Add(new ProxyNode
            {
                Name = property.Name,
                DisplayName = property.Name.StartsWith(location.Flag, StringComparison.Ordinal)
                    ? property.Name
                    : $"{location.Flag} {property.Name}",
                GroupName = location.CountryName,
                Type = type.ToUpperInvariant(),
                CountryCode = location.CountryCode,
                CountryName = location.CountryName,
                CountryFlag = location.Flag,
                DelayMilliseconds = delay
            });
        }

        return nodes;
    }

    public async Task SelectNodeAsync(
        ProxyNode node,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(node);

        using JsonDocument json = await GetProxiesDocumentAsync(cancellationToken);
        JsonElement proxies = json.RootElement.GetProperty("proxies");

        string? groupName = FindSelectorGroup(proxies, node.Name);
        if (string.IsNullOrWhiteSpace(groupName))
        {
            throw new MihomoApiException(
                $"订阅中没有找到可切换到“{node.Name}”的 selector 节点组。");
        }

        string path = "proxies/" + Uri.EscapeDataString(groupName);
        using HttpResponseMessage response = await _httpClient.PutAsJsonAsync(
            path,
            new { name = node.Name },
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new MihomoApiException(
                $"切换节点失败（HTTP {(int)response.StatusCode}）：{body}");
        }
    }

    private async Task<JsonDocument> GetProxiesDocumentAsync(
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync(
            "proxies",
            cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new MihomoApiException(
                $"读取 Mihomo 节点失败（HTTP {(int)response.StatusCode}）：{body}");
        }

        JsonDocument json = JsonDocument.Parse(body);
        if (!json.RootElement.TryGetProperty("proxies", out JsonElement proxies)
            || proxies.ValueKind != JsonValueKind.Object)
        {
            json.Dispose();
            throw new MihomoApiException("Mihomo Controller 返回的数据缺少 proxies。");
        }

        return json;
    }

    private static string? FindSelectorGroup(JsonElement proxies, string nodeName)
    {
        List<string> candidates = [];

        foreach (JsonProperty property in proxies.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object
                || !string.Equals(
                    ReadString(property.Value, "type"),
                    "Selector",
                    StringComparison.OrdinalIgnoreCase)
                || !ContainsNode(property.Value, nodeName))
            {
                continue;
            }

            candidates.Add(property.Name);
        }

        return candidates
            .OrderBy(GetGroupPriority)
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static int GetGroupPriority(string groupName)
    {
        for (int index = 0; index < PreferredGroupNames.Length; index++)
        {
            if (string.Equals(
                groupName,
                PreferredGroupNames[index],
                StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return PreferredGroupNames.Length;
    }

    private static bool ContainsNode(JsonElement group, string nodeName)
    {
        if (!group.TryGetProperty("all", out JsonElement all)
            || all.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        return all.EnumerateArray().Any(item =>
            item.ValueKind == JsonValueKind.String
            && string.Equals(item.GetString(), nodeName, StringComparison.Ordinal));
    }

    private static int? ReadLastDelay(JsonElement proxy)
    {
        if (!proxy.TryGetProperty("history", out JsonElement history)
            || history.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        int? result = null;
        foreach (JsonElement item in history.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty("delay", out JsonElement delay)
                && delay.TryGetInt32(out int value)
                && value > 0)
            {
                result = value;
            }
        }

        return result;
    }

    private static string? ReadString(JsonElement parent, string propertyName)
    {
        return parent.TryGetProperty(propertyName, out JsonElement value)
               && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool IsGroupType(string type)
    {
        return type.Equals("Selector", StringComparison.OrdinalIgnoreCase)
            || type.Equals("URLTest", StringComparison.OrdinalIgnoreCase)
            || type.Equals("Fallback", StringComparison.OrdinalIgnoreCase)
            || type.Equals("LoadBalance", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBuiltInProxy(string name)
    {
        return name.Equals("DIRECT", StringComparison.OrdinalIgnoreCase)
            || name.Equals("REJECT", StringComparison.OrdinalIgnoreCase)
            || name.Equals("PASS", StringComparison.OrdinalIgnoreCase)
            || name.Equals("COMPATIBLE", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}

public sealed class MihomoApiException : Exception
{
    public MihomoApiException(string message)
        : base(message)
    {
    }

    public MihomoApiException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
