using KuaiyunClient.Models;
using System.Text;
using System.Text.Json;

namespace KuaiyunClient.Services;

public sealed class V2BoardAccountService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly BuiltInProxyService _builtInProxyService = new();

    public V2BoardAccountService(HttpClient? httpClient = null)
    {
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
    }

    public async Task SendEmailVerificationAsync(
        AppConfig config,
        string email,
        bool forPasswordReset,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        string normalizedEmail = NormalizeEmail(email);

        await ExecuteWithFallbackAsync(
            config,
            async (client, host, token) =>
            {
                Uri uri = BuildApiUri(host, "/passport/comm/sendEmailVerify");
                object body = forPasswordReset
                    ? new { email = normalizedEmail, isforget = true }
                    : new { email = normalizedEmail };
                await PostAsync(client, config, uri, body, "发送验证码", token);
                return true;
            },
            "发送验证码",
            cancellationToken);
    }

    public async Task RegisterAsync(
        AppConfig config,
        string email,
        string emailCode,
        string password,
        string? inviteCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        string normalizedEmail = NormalizeEmail(email);
        ValidatePassword(password);
        if (string.IsNullOrWhiteSpace(emailCode))
        {
            throw new ArgumentException("邮箱验证码不能为空。", nameof(emailCode));
        }

        await ExecuteWithFallbackAsync(
            config,
            async (client, host, token) =>
            {
                Uri uri = BuildApiUri(host, "/passport/auth/register");
                await PostAsync(
                    client,
                    config,
                    uri,
                    new
                    {
                        email = normalizedEmail,
                        email_code = emailCode.Trim(),
                        password,
                        invite_code = string.IsNullOrWhiteSpace(inviteCode)
                            ? null
                            : inviteCode.Trim()
                    },
                    "注册账号",
                    token);
                return true;
            },
            "注册账号",
            cancellationToken);
    }

    public async Task ResetPasswordAsync(
        AppConfig config,
        string email,
        string emailCode,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        string normalizedEmail = NormalizeEmail(email);
        ValidatePassword(password);
        if (string.IsNullOrWhiteSpace(emailCode))
        {
            throw new ArgumentException("邮箱验证码不能为空。", nameof(emailCode));
        }

        await ExecuteWithFallbackAsync(
            config,
            async (client, host, token) =>
            {
                Uri uri = BuildApiUri(host, "/passport/auth/forget");
                await PostAsync(
                    client,
                    config,
                    uri,
                    new
                    {
                        email = normalizedEmail,
                        email_code = emailCode.Trim(),
                        password
                    },
                    "重置密码",
                    token);
                return true;
            },
            "重置密码",
            cancellationToken);
    }

    private async Task<T> ExecuteWithFallbackAsync<T>(
        AppConfig config,
        Func<HttpClient, string, CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken)
    {
        Exception? directError = null;
        try
        {
            return await ExecuteAcrossHostsAsync(
                _httpClient,
                config,
                operation,
                operationName,
                cancellationToken);
        }
        catch (V2BoardAuthenticationException)
        {
            throw;
        }
        catch (Exception ex) when (IsRecoverable(ex, cancellationToken))
        {
            directError = ex;
        }

        if (config.BuiltInProxy.Count == 0)
        {
            throw new V2BoardApiException(
                operationName + "失败。"
                + Environment.NewLine
                + (directError?.Message ?? "没有可用的 API 地址。"));
        }

        try
        {
            return await _builtInProxyService.ExecuteAsync(
                config.BuiltInProxy,
                (client, token) => ExecuteAcrossHostsAsync(
                    client,
                    config,
                    operation,
                    operationName,
                    token),
                isFatal: ex => ex is V2BoardAuthenticationException,
                cancellationToken);
        }
        catch (V2BoardAuthenticationException)
        {
            throw;
        }
        catch (Exception ex) when (IsRecoverable(ex, cancellationToken))
        {
            throw new V2BoardApiException(
                operationName + "的直连和应急代理均失败。"
                + Environment.NewLine
                + "直连："
                + (directError?.Message ?? "未知错误")
                + Environment.NewLine
                + "应急代理："
                + ex.Message);
        }
    }

    private static async Task<T> ExecuteAcrossHostsAsync<T>(
        HttpClient client,
        AppConfig config,
        Func<HttpClient, string, CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken)
    {
        List<string> errors = [];

        foreach (string host in config.RemoteHosts)
        {
            try
            {
                return await operation(client, host, cancellationToken);
            }
            catch (V2BoardAuthenticationException)
            {
                throw;
            }
            catch (Exception ex) when (IsRecoverable(ex, cancellationToken))
            {
                errors.Add($"{host}: {ex.Message}");
            }
        }

        throw new V2BoardApiException(
            operationName + "失败。"
            + Environment.NewLine
            + (errors.Count == 0
                ? "没有可用的 API 地址。"
                : string.Join(Environment.NewLine, errors)));
    }

    private static async Task PostAsync(
        HttpClient client,
        AppConfig config,
        Uri uri,
        object body,
        string operationName,
        CancellationToken cancellationToken)
    {
        string payload = JsonSerializer.Serialize(body, JsonOptions);
        using HttpRequestMessage request = new(HttpMethod.Post, uri);
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
        request.Headers.TryAddWithoutValidation(
            "User-Agent",
            string.IsNullOrWhiteSpace(config.UserAgent) ? "kuaiyun" : config.UserAgent.Trim());
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string message = ReadApiMessage(
            responseBody,
            $"{operationName}失败（HTTP {(int)response.StatusCode}）");
        if ((int)response.StatusCode is 400 or 401 or 403 or 422 or 429)
        {
            throw new V2BoardAuthenticationException(message);
        }

        throw new V2BoardApiException(message);
    }

    private static Uri BuildApiUri(string host, string relativePath)
    {
        string root = host.Trim().TrimEnd('/');
        return root.EndsWith("/api/v1", StringComparison.OrdinalIgnoreCase)
            ? new Uri(root + relativePath)
            : new Uri(root + "/api/v1" + relativePath);
    }

    private static string NormalizeEmail(string email)
    {
        string value = email.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("邮箱不能为空。", nameof(email));
        }

        return value;
    }

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("密码不能为空。", nameof(password));
        }
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
            JsonElement root = document.RootElement;
            foreach (string name in new[] { "message", "msg" })
            {
                if (root.TryGetProperty(name, out JsonElement property)
                    && property.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(property.GetString()))
                {
                    return property.GetString()!;
                }
            }

            if (root.TryGetProperty("data", out JsonElement data)
                && data.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(data.GetString()))
            {
                return data.GetString()!;
            }
        }
        catch (JsonException)
        {
            // 非 JSON 错误响应使用默认提示。
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
            or V2BoardApiException
            or BuiltInProxyUnavailableException;
    }

    public void Dispose()
    {
        _builtInProxyService.Dispose();
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
