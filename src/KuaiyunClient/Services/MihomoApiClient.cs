using KuaiyunClient.Models;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace KuaiyunClient.Services;

public sealed class MihomoApiClient : IDisposable
{
    private static readonly string[] PreferredGroupNames =
    [
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
            Timeout = TimeSpan.FromSeconds(20)
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
            if (IsGroupType(type)
                || IsBuiltInProxy(property.Name)
                || SubscriptionNodeFilter.ShouldHide(property.Name))
            {
                continue;
            }

            NodeLocation location = CountryFlagResolver.Resolve(property.Name);
            int? delay = ReadLastDelay(property.Value);

            nodes.Add(new ProxyNode
            {
                Name = property.Name,
                DisplayName = NodeDisplayNameFormatter.Format(property.Name, location),
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
        CancellationToken cancellationToken)
    {
        await SelectNodeAsync(node, preferredGroupName: null, cancellationToken);
    }

    public async Task<NodeSelectionResult> SelectNodeAsync(
        ProxyNode node,
        string? preferredGroupName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(node);

        using JsonDocument json = await GetProxiesDocumentAsync(cancellationToken);
        JsonElement proxies = json.RootElement.GetProperty("proxies");

        string? groupName = FindSelectorGroup(proxies, node.Name, preferredGroupName);
        if (string.IsNullOrWhiteSpace(groupName))
        {
            throw new MihomoApiException(
                $"没有找到可以使用“{node.DisplayName}”的线路组。");
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
                $"线路切换失败（HTTP {(int)response.StatusCode}）：{body}");
        }

        string? actualNode = null;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            actualNode = await ReadSelectedNodeAsync(groupName, cancellationToken);
            if (string.Equals(actualNode, node.Name, StringComparison.Ordinal))
            {
                return new NodeSelectionResult(groupName, actualNode);
            }

            await Task.Delay(100, cancellationToken);
        }

        throw new MihomoApiException(
            $"线路未真正切换成功。目标：{node.DisplayName}；"
            + $"当前：{(string.IsNullOrWhiteSpace(actualNode) ? "未知" : actualNode)}。");
    }

    public async Task<int?> TestDelayAsync(
        ProxyNode node,
        string testUrl,
        int timeoutMilliseconds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (!Uri.TryCreate(testUrl, UriKind.Absolute, out Uri? targetUri)
            || (targetUri.Scheme != Uri.UriSchemeHttp
                && targetUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("测速地址必须是有效的 HTTP 或 HTTPS 地址。", nameof(testUrl));
        }

        int timeout = Math.Clamp(timeoutMilliseconds, 1000, 30000);
        string path = "proxies/"
            + Uri.EscapeDataString(node.Name)
            + "/delay?url="
            + Uri.EscapeDataString(targetUri.AbsoluteUri)
            + "&timeout="
            + timeout;

        using HttpResponseMessage response = await _httpClient.GetAsync(path, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new MihomoApiException(
                $"Mihomo Controller 拒绝测速请求（HTTP {(int)response.StatusCode}）。");
        }

        if (!response.IsSuccessStatusCode || string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using JsonDocument json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("delay", out JsonElement delayElement)
                && delayElement.TryGetInt32(out int delay)
                && delay > 0)
            {
                return delay;
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private async Task<string?> ReadSelectedNodeAsync(
        string groupName,
        CancellationToken cancellationToken)
    {
        string path = "proxies/" + Uri.EscapeDataString(groupName);
        using HttpResponseMessage response = await _httpClient.GetAsync(path, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new MihomoApiException(
                $"无法确认实际线路（HTTP {(int)response.StatusCode}）。");
        }

        using JsonDocument json = JsonDocument.Parse(body);
        return ReadString(json.RootElement, "now");
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
                $"读取节点失败（HTTP {(int)response.StatusCode}）：{body}");
        }

        JsonDocument json = JsonDocument.Parse(body);
        if (!json.RootElement.TryGetProperty("proxies", out JsonElement proxies)
            || proxies.ValueKind != JsonValueKind.Object)
        {
            json.Dispose();
            throw new MihomoApiException("Controller 返回的数据缺少 proxies。");
        }

        return json;
    }

    private static string? FindSelectorGroup(
        JsonElement proxies,
        string nodeName,
        string? preferredGroupName)
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
            .OrderBy(name => GetGroupPriority(name, preferredGroupName))
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static int GetGroupPriority(string groupName, string? preferredGroupName)
    {
        if (!string.IsNullOrWhiteSpace(preferredGroupName)
            && string.Equals(groupName, preferredGroupName, StringComparison.OrdinalIgnoreCase))
        {
            return -100;
        }

        if (string.Equals(groupName, "GLOBAL", StringComparison.OrdinalIgnoreCase))
        {
            return 10_000;
        }

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

        return PreferredGroupNames.Length + 10;
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

public sealed record NodeSelectionResult(string GroupName, string NodeName);

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
