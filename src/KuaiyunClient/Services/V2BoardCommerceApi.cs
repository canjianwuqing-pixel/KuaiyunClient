using KuaiyunClient.Models;
using System.Text;
using System.Text.Json;

namespace KuaiyunClient.Services;

public sealed class V2BoardCommerceApi : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public V2BoardCommerceApi(HttpClient? httpClient = null)
    {
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    }

    public async Task RefreshSessionAsync(
        AppConfig config,
        UserSession session,
        CancellationToken cancellationToken = default)
    {
        JsonElement root = await SendAcrossHostsAsync(
            config, session, HttpMethod.Get, ["/user/getSubscribe"], null, cancellationToken);
        JsonElement data = RequireObject(root, "data", "账户信息响应缺少 data。");

        session.Email = GetString(data, "email") ?? session.Email;
        session.SubscriptionUrl = GetString(data, "subscribe_url") ?? session.SubscriptionUrl;
        session.UploadBytes = GetInt64(data, "u");
        session.DownloadBytes = GetInt64(data, "d");
        session.TransferEnableBytes = GetInt64(data, "transfer_enable");
        session.ExpiredAt = GetInt64(data, "expired_at");
        session.PlanId = GetNullableInt32(data, "plan_id");
        session.ResetDay = GetNullableInt32(data, "reset_day");
    }

    public async Task<IReadOnlyList<V2BoardPlan>> GetPlansAsync(
        AppConfig config,
        UserSession session,
        CancellationToken cancellationToken = default)
    {
        JsonElement root = await SendAcrossHostsAsync(
            config,
            session,
            HttpMethod.Get,
            ["/user/plan/fetch", "/guest/plan/fetch"],
            null,
            cancellationToken);
        JsonElement data = RequireArray(root, "data", "套餐响应缺少 data。");

        return data.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Where(item => GetInt32(item, "show", 1) != 0)
            .Select(ParsePlan)
            .Where(plan => plan.Id > 0 && plan.Cycles.Count > 0)
            .OrderBy(plan => plan.Sort)
            .ThenBy(plan => plan.Id)
            .ToArray();
    }

    public async Task<IReadOnlyList<V2BoardNotice>> GetNoticesAsync(
        AppConfig config,
        UserSession session,
        CancellationToken cancellationToken = default)
    {
        JsonElement root = await SendAcrossHostsAsync(
            config, session, HttpMethod.Get, ["/user/notice/fetch"], null, cancellationToken);
        JsonElement data = RequireArray(root, "data", "公告响应缺少 data。");

        return data.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(item => new V2BoardNotice
            {
                Id = GetInt32(item, "id"),
                Title = GetString(item, "title") ?? "系统公告",
                Content = GetString(item, "content") ?? string.Empty,
                CreatedAt = GetInt64(item, "created_at")
            })
            .OrderByDescending(item => item.CreatedAt)
            .ToArray();
    }

    public async Task<string> CreateOrderAsync(
        AppConfig config,
        UserSession session,
        int planId,
        string period,
        CancellationToken cancellationToken = default)
    {
        if (planId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(planId));
        }

        if (string.IsNullOrWhiteSpace(period))
        {
            throw new ArgumentException("套餐周期不能为空。", nameof(period));
        }

        JsonElement root = await SendAcrossHostsAsync(
            config,
            session,
            HttpMethod.Post,
            ["/user/order/save"],
            new { plan_id = planId, period },
            cancellationToken);
        return RequireString(root, "data", "创建订单成功，但后台未返回订单号。");
    }

    public async Task<IReadOnlyList<V2BoardPaymentMethod>> GetPaymentMethodsAsync(
        AppConfig config,
        UserSession session,
        CancellationToken cancellationToken = default)
    {
        JsonElement root = await SendAcrossHostsAsync(
            config,
            session,
            HttpMethod.Get,
            ["/user/order/getPaymentMethod"],
            null,
            cancellationToken);
        JsonElement data = RequireArray(root, "data", "支付方式响应缺少 data。");

        return data.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(item => new V2BoardPaymentMethod
            {
                Id = GetInt32(item, "id"),
                Name = GetString(item, "name") ?? "在线支付",
                Payment = GetString(item, "payment") ?? string.Empty
            })
            .Where(item => item.Id > 0)
            .ToArray();
    }

    public async Task<V2BoardCheckoutResult> CheckoutOrderAsync(
        AppConfig config,
        UserSession session,
        string tradeNo,
        int paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tradeNo))
        {
            throw new ArgumentException("订单号不能为空。", nameof(tradeNo));
        }

        if (paymentMethodId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(paymentMethodId));
        }

        JsonElement root = await SendAcrossHostsAsync(
            config,
            session,
            HttpMethod.Post,
            ["/user/order/checkout"],
            new { trade_no = tradeNo, method = paymentMethodId },
            cancellationToken);

        JsonElement payload = root;
        if (root.TryGetProperty("data", out JsonElement wrapped)
            && wrapped.ValueKind == JsonValueKind.Object)
        {
            payload = wrapped;
        }

        int type = GetInt32(payload, "type", GetInt32(root, "type"));
        string? data = GetString(payload, "data");
        if (string.IsNullOrWhiteSpace(data)
            && root.TryGetProperty("data", out JsonElement directData))
        {
            data = directData.ValueKind switch
            {
                JsonValueKind.String => directData.GetString(),
                JsonValueKind.True => "支付成功",
                JsonValueKind.Number => directData.GetRawText(),
                _ => null
            };
        }

        if (string.IsNullOrWhiteSpace(data))
        {
            throw new V2BoardApiException("后台未返回支付地址或二维码内容。");
        }

        return new V2BoardCheckoutResult(type, data);
    }

    private async Task<JsonElement> SendAcrossHostsAsync(
        AppConfig config,
        UserSession session,
        HttpMethod method,
        IReadOnlyList<string> relativePaths,
        object? body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(session);

        List<string> errors = [];
        foreach (string host in OrderedHosts(config, session))
        {
            foreach (string relativePath in relativePaths)
            {
                Uri uri;
                try
                {
                    uri = BuildApiUri(host, relativePath);
                }
                catch (Exception ex) when (ex is UriFormatException or ArgumentException)
                {
                    errors.Add($"{host}{relativePath}: 后台地址无效：{ex.Message}");
                    continue;
                }

                try
                {
                    JsonElement result = await SendJsonAsync(
                        config, session, uri, method, body, cancellationToken);
                    session.ApiHost = host;
                    return result;
                }
                catch (V2BoardAuthenticationException)
                {
                    throw;
                }
                catch (Exception ex) when (IsRecoverable(ex, cancellationToken))
                {
                    errors.Add($"{uri}: {ex.Message}");
                }
            }
        }

        string details = errors.Count == 0
            ? "没有可用的 API 地址。"
            : string.Join(Environment.NewLine, errors);
        throw new V2BoardApiException("后台请求失败。" + Environment.NewLine + details);
    }

    private async Task<JsonElement> SendJsonAsync(
        AppConfig config,
        UserSession session,
        Uri uri,
        HttpMethod method,
        object? body,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(method, uri);
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
        request.Headers.TryAddWithoutValidation(
            "User-Agent",
            string.IsNullOrWhiteSpace(config.UserAgent) ? "kuaiyun" : config.UserAgent.Trim());
        request.Headers.TryAddWithoutValidation("Authorization", session.AuthData);

        if (body is not null)
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(body, JsonOptions),
                Encoding.UTF8,
                "application/json");
        }

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string message = ReadApiMessage(
                responseBody,
                $"请求失败（HTTP {(int)response.StatusCode}）");
            if ((int)response.StatusCode is 401 or 403)
            {
                throw new V2BoardAuthenticationException(
                    "登录状态已失效，请重新登录。" + Environment.NewLine + message);
            }

            throw new V2BoardApiException(message);
        }

        if (string.IsNullOrWhiteSpace(responseBody))
        {
            throw new V2BoardApiException("后台返回了空响应。");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(responseBody);
            return document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new V2BoardApiException("后台返回的内容不是有效 JSON：" + ex.Message);
        }
    }

    private static V2BoardPlan ParsePlan(JsonElement item) => new()
    {
        Id = GetInt32(item, "id"),
        Name = GetString(item, "name") ?? "未命名套餐",
        TransferEnableGigabytes = GetInt64(item, "transfer_enable"),
        Content = GetString(item, "content") ?? string.Empty,
        Renew = GetInt32(item, "renew", 1) != 0,
        Sort = GetInt32(item, "sort"),
        MonthPrice = GetNullableInt64(item, "month_price"),
        QuarterPrice = GetNullableInt64(item, "quarter_price"),
        HalfYearPrice = GetNullableInt64(item, "half_year_price"),
        YearPrice = GetNullableInt64(item, "year_price"),
        TwoYearPrice = GetNullableInt64(item, "two_year_price"),
        ThreeYearPrice = GetNullableInt64(item, "three_year_price"),
        OnetimePrice = GetNullableInt64(item, "onetime_price")
    };

    private static IEnumerable<string> OrderedHosts(AppConfig config, UserSession session) =>
        config.RemoteHosts
            .Where(host => !string.IsNullOrWhiteSpace(host))
            .OrderByDescending(host => string.Equals(
                host.TrimEnd('/'),
                session.ApiHost.TrimEnd('/'),
                StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private static Uri BuildApiUri(string host, string relativePath)
    {
        string root = host.Trim().TrimEnd('/');
        return root.EndsWith("/api/v1", StringComparison.OrdinalIgnoreCase)
            ? new Uri(root + relativePath)
            : new Uri(root + "/api/v1" + relativePath);
    }

    private static JsonElement RequireObject(
        JsonElement root,
        string name,
        string fallback)
    {
        if (!root.TryGetProperty(name, out JsonElement value)
            || value.ValueKind != JsonValueKind.Object)
        {
            throw new V2BoardApiException(ReadApiMessage(root, fallback));
        }

        return value;
    }

    private static JsonElement RequireArray(
        JsonElement root,
        string name,
        string fallback)
    {
        if (!root.TryGetProperty(name, out JsonElement value)
            || value.ValueKind != JsonValueKind.Array)
        {
            throw new V2BoardApiException(ReadApiMessage(root, fallback));
        }

        return value;
    }

    private static string RequireString(
        JsonElement root,
        string name,
        string fallback)
    {
        string? value = GetString(root, name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new V2BoardApiException(ReadApiMessage(root, fallback));
        }

        return value;
    }

    private static string? GetString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static int GetInt32(JsonElement root, string name, int fallback = 0)
    {
        if (!root.TryGetProperty(name, out JsonElement value))
        {
            return fallback;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), out number)
                ? number
                : fallback;
    }

    private static int? GetNullableInt32(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), out number)
                ? number
                : null;
    }

    private static long GetInt64(JsonElement root, string name) =>
        GetNullableInt64(root, name) ?? 0;

    private static long? GetNullableInt64(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String
            && long.TryParse(value.GetString(), out number)
                ? number
                : null;
    }

    private static string ReadApiMessage(string body, string fallback)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return fallback;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            return ReadApiMessage(document.RootElement, fallback);
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    private static string ReadApiMessage(JsonElement root, string fallback)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return fallback;
        }

        if (root.TryGetProperty("errors", out JsonElement errors)
            && errors.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty error in errors.EnumerateObject())
            {
                if (error.Value.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                string? first = error.Value.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString())
                    .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item));
                if (!string.IsNullOrWhiteSpace(first))
                {
                    return first!;
                }
            }
        }

        foreach (string name in new[] { "message", "msg" })
        {
            string? message = GetString(root, name);
            if (!string.IsNullOrWhiteSpace(message))
            {
                return message;
            }
        }

        if (root.TryGetProperty("data", out JsonElement data)
            && data.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(data.GetString()))
        {
            return data.GetString()!;
        }

        return fallback;
    }

    private static bool IsRecoverable(
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        return exception is HttpRequestException
            or TaskCanceledException
            or JsonException
            or FormatException
            or V2BoardApiException;
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}