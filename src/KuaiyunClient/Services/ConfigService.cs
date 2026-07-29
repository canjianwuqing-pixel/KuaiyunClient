using KuaiyunClient.Models;
using System.Net.Http;
using System.Text.Json;

namespace KuaiyunClient.Services;

public sealed class ConfigService : IConfigService, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true
    };

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly string _bootstrapPath;
    private readonly string _cachePath;

    public ConfigService(HttpClient? httpClient = null)
    {
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        _bootstrapPath = Path.Combine(AppContext.BaseDirectory, "bootstrap.json");

        string configDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KuaiyunClient",
            "config");

        Directory.CreateDirectory(configDirectory);
        _cachePath = Path.Combine(configDirectory, "config-cache.json");
    }

    public async Task<ConfigLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        BootstrapConfig bootstrap = await LoadBootstrapAsync(cancellationToken);
        List<string> errors = [];

        if (IsCacheFresh(bootstrap.CloudUpdateHours))
        {
            try
            {
                AppConfig freshCache = await LoadCacheAsync(cancellationToken);
                return new ConfigLoadResult(freshCache, _cachePath, FromCache: true);
            }
            catch (Exception ex) when (IsRecoverableConfigError(ex, cancellationToken))
            {
                errors.Add($"本地缓存: {ex.Message}");
            }
        }

        foreach (string cloudUrl in bootstrap.CloudConfig)
        {
            try
            {
                AppConfig config = await DownloadConfigAsync(cloudUrl, cancellationToken);
                await SaveCacheAsync(config, cancellationToken);
                return new ConfigLoadResult(config, cloudUrl, FromCache: false);
            }
            catch (Exception ex) when (IsRecoverableConfigError(ex, cancellationToken))
            {
                errors.Add($"{cloudUrl}: {ex.Message}");
            }
        }

        if (File.Exists(_cachePath))
        {
            try
            {
                AppConfig staleCache = await LoadCacheAsync(cancellationToken);
                return new ConfigLoadResult(staleCache, _cachePath, FromCache: true);
            }
            catch (Exception ex) when (IsRecoverableConfigError(ex, cancellationToken))
            {
                errors.Add($"本地缓存: {ex.Message}");
            }
        }

        string details = errors.Count == 0
            ? "没有可用的 OSS 配置地址。"
            : string.Join(Environment.NewLine, errors);

        throw new InvalidOperationException(
            "远程配置读取失败，并且本地没有可用缓存。" + Environment.NewLine + details);
    }

    private async Task<BootstrapConfig> LoadBootstrapAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_bootstrapPath))
        {
            throw new FileNotFoundException(
                "程序目录中缺少 bootstrap.json。",
                _bootstrapPath);
        }

        await using FileStream stream = File.OpenRead(_bootstrapPath);
        BootstrapConfig? bootstrap = await JsonSerializer.DeserializeAsync<BootstrapConfig>(
            stream,
            JsonOptions,
            cancellationToken);

        if (bootstrap is null)
        {
            throw new InvalidOperationException("bootstrap.json 内容为空或格式错误。");
        }

        bootstrap.CloudConfig = NormalizeHttpUrls(bootstrap.CloudConfig, "CloudConfig");
        bootstrap.CloudUpdateHours = Math.Max(1, bootstrap.CloudUpdateHours);

        if (bootstrap.CloudConfig.Count == 0)
        {
            throw new InvalidOperationException("bootstrap.json 至少需要一个 CloudConfig 地址。");
        }

        return bootstrap;
    }

    private async Task<AppConfig> DownloadConfigAsync(
        string cloudUrl,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, cloudUrl);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");

        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        AppConfig? config = await JsonSerializer.DeserializeAsync<AppConfig>(
            stream,
            JsonOptions,
            cancellationToken);

        return ValidateConfig(config);
    }

    private async Task<AppConfig> LoadCacheAsync(CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(_cachePath);
        AppConfig? config = await JsonSerializer.DeserializeAsync<AppConfig>(
            stream,
            JsonOptions,
            cancellationToken);

        return ValidateConfig(config);
    }

    private async Task SaveCacheAsync(
        AppConfig config,
        CancellationToken cancellationToken)
    {
        string temporaryPath = _cachePath + ".tmp";

        await using (FileStream stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                config,
                JsonOptions,
                cancellationToken);
        }

        File.Move(temporaryPath, _cachePath, overwrite: true);
    }

    private bool IsCacheFresh(int updateHours)
    {
        if (!File.Exists(_cachePath))
        {
            return false;
        }

        DateTime lastWriteTimeUtc = File.GetLastWriteTimeUtc(_cachePath);
        return DateTime.UtcNow - lastWriteTimeUtc < TimeSpan.FromHours(updateHours);
    }

    private static bool IsRecoverableConfigError(
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
            or InvalidOperationException
            or IOException
            or UnauthorizedAccessException;
    }

    private static AppConfig ValidateConfig(AppConfig? config)
    {
        if (config is null)
        {
            throw new InvalidOperationException("远程 config.json 内容为空或格式错误。");
        }

        if (string.IsNullOrWhiteSpace(config.AppName))
        {
            throw new InvalidOperationException("远程 config.json 缺少 AppName。");
        }

        if (string.IsNullOrWhiteSpace(config.UserAgent))
        {
            throw new InvalidOperationException("远程 config.json 缺少 UserAgent。");
        }

        config.AppName = config.AppName.Trim();
        config.UserAgent = config.UserAgent.Trim();
        config.RemoteHosts = NormalizeHttpUrls(config.RemoteHosts, "RemoteHosts");
        config.BuiltInProxy ??= [];

        if (config.RemoteHosts.Count == 0)
        {
            throw new InvalidOperationException("远程 config.json 至少需要一个 RemoteHosts 地址。");
        }

        ValidateOptionalHttpUrl(config.AppLogo, nameof(config.AppLogo));
        ValidateOptionalHttpUrl(config.HomePage, nameof(config.HomePage));
        ValidateOptionalHttpUrl(config.TelegramGroup, nameof(config.TelegramGroup));
        ValidateOptionalHttpUrl(config.UpdateUrl, nameof(config.UpdateUrl));

        return config;
    }

    private static List<string> NormalizeHttpUrls(
        IEnumerable<string>? values,
        string fieldName)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        List<string> output = [];

        foreach (string value in values ?? [])
        {
            string text = value.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri)
                || (uri.Scheme != Uri.UriSchemeHttp
                    && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException($"{fieldName} 包含无效地址：{value}");
            }

            string normalized = uri.AbsoluteUri.TrimEnd('/');
            if (seen.Add(normalized))
            {
                output.Add(normalized);
            }
        }

        return output;
    }

    private static void ValidateOptionalHttpUrl(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp
                && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException($"{fieldName} 地址无效：{value}");
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
