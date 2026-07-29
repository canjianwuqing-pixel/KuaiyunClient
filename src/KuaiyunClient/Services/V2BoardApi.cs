using KuaiyunClient.Models;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace KuaiyunClient.Services;

public sealed class V2BoardApi : IV2BoardApi, IDisposable
{
    private const string SubscriptionFlag = "meta";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public V2BoardApi(HttpClient? httpClient = null)
    {
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
    }

    public async Task<UserSession> LoginAsync(
        AppConfig config,
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        string normalizedEmail = email.Trim();
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            throw new ArgumentException("邮箱不能为空。", nameof(email));
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("密码不能为空。", nameof(password));
        }

        List<string> errors = [];

        foreach (string host in config.RemoteHosts)
        {
            try
            {
                return await LoginAgainstHostAsync(
                    config,
                    host,
                    normalizedEmail,
                    password,
                    cancellationToken);
            }
            catch (V2BoardAuthenticationException)
            {
                throw;
            }
            catch (Exception ex) when (IsHostRecoverable(ex, cancellationToken))
            {
                errors.Add($"{host}: {ex.Message}");
            }
        }

        string details = errors.Count == 0
            ? "没有可用的 API 地址。"
            : string.Join(Environment.NewLine, errors);

        throw new V2BoardApiException(
            "所有 V2Board API 地址均无法完成登录。" + Environment.NewLine + details);
    }

    public async Task<string> DownloadSubscriptionAsync(
        AppConfig config,
        UserSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(session);

        if (!Uri.TryCreate(session.SubscriptionUrl, UriKind.Absolute, out Uri? subscriptionUri)
            || (subscriptionUri.Scheme != Uri.UriSchemeHttp
                && subscriptionUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new V2BoardApiException("后台返回的订阅地址无效。");
        }

        Uri finalUri = AppendQueryParameter(subscriptionUri, "flag", SubscriptionFlag);
        using HttpRequestMessage request = CreateRequest(HttpMethod.Get, finalUri, config.UserAgent);
        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode || string.IsNullOrWhiteSpace(body))
        {
            throw new V2BoardApiException(
                ReadApiMessage(body, $"下载订阅失败（HTTP {(int)response.StatusCode}）"));
        }

        return body;
    }

    private async Task<UserSession> LoginAgainstHostAsync(
        AppConfig config,
        string host,
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        Uri loginUri = BuildApiUri(host, "/passport/auth/login");
        string payload = JsonSerializer.Serialize(new { email, password }, JsonOptions);

        using HttpRequestMessage loginRequest = CreateRequest(HttpMethod.Post, loginUri, config.UserAgent);
        loginRequest.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using HttpResponseMessage loginResponse = await _httpClient.SendAsync(loginRequest, cancellationToken);
        string loginBody = await loginResponse.Content.ReadAsStringAsync(cancellationToken);

        if (!loginResponse.IsSuccessStatusCode)
        {
            string message = ReadApiMessage(
                loginBody,
                $"登录失败（HTTP {(int)loginResponse.StatusCode}）");

            if ((int)loginResponse.StatusCode is 400 or 401 or 403 or 422)
            {
                throw new V2BoardAuthenticationException(message);
            }

            throw new V2BoardApiException(message);
        }

        using JsonDocument loginJson = JsonDocument.Parse(loginBody);
        JsonElement loginData = RequireObject(loginJson.RootElement, "data", "登录响应缺少 data。");
        string authData = RequireString(loginData, "auth_data", "登录响应缺少 auth_data。");

        Uri subscribeUri = BuildApiUri(host, "/user/getSubscribe");
        using HttpRequestMessage subscribeRequest = CreateRequest(HttpMethod.Get, subscribeUri, config.UserAgent);
        subscribeRequest.Headers.TryAddWithoutValidation("Authorization", authData);

        using HttpResponseMessage subscribeResponse = await _httpClient.SendAsync(
            subscribeRequest,
            cancellationToken);
        string subscribeBody = await subscribeResponse.Content.ReadAsStringAsync(cancellationToken);

        if (!subscribeResponse.IsSuccessStatusCode)
        {
            throw new V2BoardApiException(
                ReadApiMessage(
                    subscribeBody,
                    $"获取用户信息失败（HTTP {(int)subscribeResponse.StatusCode}）"));
        }

        using JsonDocument subscribeJson = JsonDocument.Parse(subscribeBody);
        JsonElement subscribeData = RequireObject(
            subscribeJson.RootElement,
            "data",
            "用户信息响应缺少 data。");

        return new UserSession
        {
            Email = TryGetString(subscribeData, "email") ?? email,
            AuthData = authData,
            SubscriptionUrl = RequireString(
                subscribeData,
                "subscribe_url",
                "用户信息响应缺少 subscribe_url。"),
            UploadBytes = TryGetInt64(subscribeData, "u"),
            DownloadBytes = TryGetInt64(subscribeData, "d"),
            TransferEnableBytes = TryGetInt64(subscribeData, "transfer_enable"),
            ExpiredAt = TryGetInt64(subscribeData, "expired_at")
        };
    }

    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        Uri uri,
        string userAgent)
    {
        HttpRequestMessage request = new(method, uri);
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
        request.Headers.TryAddWithoutValidation(
            "User-Agent",
            string.IsNullOrWhiteSpace(userAgent) ? "kuaiyun" : userAgent.Trim());
        return request;
    }

    private static Uri BuildApiUri(string host, string relativePath)
    {
        string root = host.Trim().TrimEnd('/');
        if (root.EndsWith("/api/v1", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(root + relativePath);
        }

        return new Uri(root + "/api/v1" + relativePath);
    }

    private static Uri AppendQueryParameter(Uri uri, string key, string value)
    {
        string escapedKey = Uri.EscapeDataString(key);
        string query = uri.Query.TrimStart('?');

        if (query.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Any(item => item.StartsWith(escapedKey + "=", StringComparison.OrdinalIgnoreCase)))
        {
            return uri;
        }

        UriBuilder builder = new(uri)
        {
            Query = string.IsNullOrWhiteSpace(query)
                ? $"{escapedKey}={Uri.EscapeDataString(value)}"
                : $"{query}&{escapedKey}={Uri.EscapeDataString(value)}"
        };

        return builder.Uri;
    }

    private static JsonElement RequireObject(
        JsonElement parent,
        string propertyName,
        string errorMessage)
    {
        if (!parent.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind != JsonValueKind.Object)
        {
            throw new V2BoardApiException(ReadApiMessage(parent, errorMessage));
        }

        return value;
    }

    private static string RequireString(
        JsonElement parent,
        string propertyName,
        string errorMessage)
    {
        string? value = TryGetString(parent, propertyName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new V2BoardApiException(errorMessage);
        }

        return value;
    }

    private static string? TryGetString(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out JsonElement value))
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

    private static long TryGetInt64(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out JsonElement value))
        {
            return 0;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String
            && long.TryParse(value.GetString(), out number))
        {
            return number;
        }

        return 0;
    }

    private static string ReadApiMessage(string body, string fallback)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return fallback;
        }

        try
        {
            using JsonDocument json = JsonDocument.Parse(body);
            return ReadApiMessage(json.RootElement, fallback);
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    private static string ReadApiMessage(JsonElement root, string fallback)
    {
        foreach (string property in new[] { "message", "msg" })
        {
            string? value = TryGetString(root, property);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
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

    private static bool IsHostRecoverable(
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

public class V2BoardApiException(string message) : Exception(message);

public sealed class V2BoardAuthenticationException(string message)
    : V2BoardApiException(message);
