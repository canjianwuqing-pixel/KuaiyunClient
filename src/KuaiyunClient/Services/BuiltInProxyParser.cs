using System.Text;

namespace KuaiyunClient.Services;

public static class BuiltInProxyParser
{
    private static readonly HashSet<string> DirectSchemes = new(
        ["http", "https", "socks4", "socks4a", "socks5"],
        StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<BuiltInProxyEndpoint> ParseMany(IEnumerable<string>? values)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        List<BuiltInProxyEndpoint> endpoints = [];

        foreach (string value in values ?? [])
        {
            string text = value?.Trim() ?? string.Empty;
            if (text.Length == 0)
            {
                continue;
            }

            BuiltInProxyEndpoint endpoint = Parse(text);
            if (seen.Add(endpoint.NormalizedValue))
            {
                endpoints.Add(endpoint);
            }
        }

        return endpoints;
    }

    public static BuiltInProxyEndpoint Parse(string value)
    {
        string text = value?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            throw new BuiltInProxyFormatException("BuiltInProxy 中存在空代理地址。");
        }

        if (text.StartsWith("ss://", StringComparison.OrdinalIgnoreCase))
        {
            return ParseShadowsocks(text);
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri)
            || !DirectSchemes.Contains(uri.Scheme)
            || string.IsNullOrWhiteSpace(uri.Host)
            || uri.Port is <= 0 or > 65535)
        {
            throw new BuiltInProxyFormatException(
                "不支持的 BuiltInProxy 地址。允许 http://、https://、socks4://、socks4a://、socks5:// 和 ss://。");
        }

        string? username = null;
        string? password = null;
        if (!string.IsNullOrWhiteSpace(uri.UserInfo))
        {
            string[] credentials = uri.UserInfo.Split(':', 2);
            username = Uri.UnescapeDataString(credentials[0]);
            password = credentials.Length > 1
                ? Uri.UnescapeDataString(credentials[1])
                : string.Empty;
        }

        UriBuilder normalizedBuilder = new(uri)
        {
            Fragment = string.Empty
        };

        return new BuiltInProxyEndpoint(
            BuiltInProxyKind.Direct,
            normalizedBuilder.Uri.AbsoluteUri,
            uri.Scheme.ToLowerInvariant(),
            uri.Host,
            uri.Port,
            username,
            password,
            Shadowsocks: null);
    }

    private static BuiltInProxyEndpoint ParseShadowsocks(string value)
    {
        string payload = value["ss://".Length..];
        int fragmentIndex = payload.IndexOf('#');
        if (fragmentIndex >= 0)
        {
            payload = payload[..fragmentIndex];
        }

        string query = string.Empty;
        int queryIndex = payload.IndexOf('?');
        if (queryIndex >= 0)
        {
            query = payload[(queryIndex + 1)..];
            payload = payload[..queryIndex];
        }

        if (QueryContainsPlugin(query))
        {
            throw new BuiltInProxyFormatException(
                "当前 BuiltInProxy 暂不支持带 plugin 的 Shadowsocks 分享链接。");
        }

        payload = Uri.UnescapeDataString(payload.Trim());
        if (payload.Length == 0)
        {
            throw new BuiltInProxyFormatException("Shadowsocks 分享链接内容为空。");
        }

        string credentialsText;
        string serverText;
        int separator = payload.LastIndexOf('@');

        if (separator > 0)
        {
            credentialsText = DecodeBase64OrPlain(payload[..separator]);
            serverText = payload[(separator + 1)..];
        }
        else
        {
            string decoded = DecodeBase64OrPlain(payload);
            separator = decoded.LastIndexOf('@');
            if (separator <= 0)
            {
                throw new BuiltInProxyFormatException(
                    "Shadowsocks 分享链接缺少服务器地址。");
            }

            credentialsText = decoded[..separator];
            serverText = decoded[(separator + 1)..];
        }

        int credentialSeparator = credentialsText.IndexOf(':');
        if (credentialSeparator <= 0)
        {
            throw new BuiltInProxyFormatException(
                "Shadowsocks 分享链接缺少加密方式或密码。");
        }

        string method = credentialsText[..credentialSeparator].Trim();
        string password = credentialsText[(credentialSeparator + 1)..];
        if (method.Length == 0 || password.Length == 0)
        {
            throw new BuiltInProxyFormatException(
                "Shadowsocks 分享链接的加密方式或密码为空。");
        }

        if (!Uri.TryCreate("tcp://" + serverText, UriKind.Absolute, out Uri? serverUri)
            || string.IsNullOrWhiteSpace(serverUri.Host)
            || serverUri.Port is <= 0 or > 65535)
        {
            throw new BuiltInProxyFormatException(
                "Shadowsocks 分享链接的服务器或端口无效。");
        }

        ShadowsocksProxySettings shadowsocks = new(
            method,
            password,
            serverUri.Host,
            serverUri.Port);

        string normalizedCredentials = ToUrlSafeBase64($"{method}:{password}");
        string normalizedHost = serverUri.Host.Contains(':')
            ? $"[{serverUri.Host}]"
            : serverUri.Host;
        string normalized = $"ss://{normalizedCredentials}@{normalizedHost}:{serverUri.Port}";

        return new BuiltInProxyEndpoint(
            BuiltInProxyKind.Shadowsocks,
            normalized,
            "ss",
            serverUri.Host,
            serverUri.Port,
            Username: null,
            Password: null,
            Shadowsocks: shadowsocks);
    }

    private static bool QueryContainsPlugin(string query)
    {
        return query.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Split('=', 2)[0])
            .Any(key => string.Equals(
                Uri.UnescapeDataString(key),
                "plugin",
                StringComparison.OrdinalIgnoreCase));
    }

    private static string DecodeBase64OrPlain(string value)
    {
        string text = value.Trim();
        if (text.Contains(':'))
        {
            return text;
        }

        try
        {
            string normalized = text.Replace('-', '+').Replace('_', '/');
            int padding = normalized.Length % 4;
            if (padding > 0)
            {
                normalized = normalized.PadRight(normalized.Length + 4 - padding, '=');
            }

            return Encoding.UTF8.GetString(Convert.FromBase64String(normalized));
        }
        catch (FormatException ex)
        {
            throw new BuiltInProxyFormatException(
                "Shadowsocks 分享链接的 Base64 内容无效。",
                ex);
        }
    }

    private static string ToUrlSafeBase64(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}

public enum BuiltInProxyKind
{
    Direct,
    Shadowsocks
}

public sealed record BuiltInProxyEndpoint(
    BuiltInProxyKind Kind,
    string NormalizedValue,
    string Scheme,
    string Host,
    int Port,
    string? Username,
    string? Password,
    ShadowsocksProxySettings? Shadowsocks)
{
    public string DisplayName => $"{Scheme.ToUpperInvariant()} {Host}:{Port}";
}

public sealed record ShadowsocksProxySettings(
    string Method,
    string Password,
    string Server,
    int Port);

public sealed class BuiltInProxyFormatException : FormatException
{
    public BuiltInProxyFormatException(string message)
        : base(message)
    {
    }

    public BuiltInProxyFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
