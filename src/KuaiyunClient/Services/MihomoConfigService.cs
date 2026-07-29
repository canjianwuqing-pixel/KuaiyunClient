using System.Security.Cryptography;
using System.Text;

namespace KuaiyunClient.Services;

public sealed class MihomoConfigService
{
    public const int MixedPort = 7890;
    public const int ControllerPort = 9090;

    private static readonly HashSet<string> ManagedTopLevelKeys = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "port",
        "socks-port",
        "mixed-port",
        "redir-port",
        "tproxy-port",
        "allow-lan",
        "bind-address",
        "mode",
        "log-level",
        "external-controller",
        "secret"
    };

    private readonly string _runtimeDirectory;
    private readonly string _configPath;
    private readonly string _logPath;

    public MihomoConfigService()
    {
        _runtimeDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KuaiyunClient",
            "runtime");

        Directory.CreateDirectory(_runtimeDirectory);
        _configPath = Path.Combine(_runtimeDirectory, "config.yaml");
        _logPath = Path.Combine(_runtimeDirectory, "mihomo.log");
    }

    public string RuntimeDirectory => _runtimeDirectory;

    public string ConfigPath => _configPath;

    public string LogPath => _logPath;

    public async Task<MihomoRuntimeConfig> WriteAsync(
        string subscriptionYaml,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subscriptionYaml))
        {
            throw new MihomoConfigurationException("订阅配置为空，无法启动 Mihomo。");
        }

        string secret = Convert
            .ToHexString(RandomNumberGenerator.GetBytes(24))
            .ToLowerInvariant();

        string originalConfig = RemoveManagedTopLevelKeys(subscriptionYaml);
        string generatedHeader = $$"""
# 此文件由快云客户端自动生成，请勿手动修改。
mixed-port: {{MixedPort}}
allow-lan: false
bind-address: 127.0.0.1
mode: rule
log-level: info
external-controller: 127.0.0.1:{{ControllerPort}}
secret: "{{secret}}"

""";

        string finalConfig = generatedHeader + originalConfig.TrimStart();
        string temporaryPath = _configPath + ".tmp";

        await File.WriteAllTextAsync(
            temporaryPath,
            finalConfig,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);

        File.Move(temporaryPath, _configPath, overwrite: true);

        return new MihomoRuntimeConfig(
            _runtimeDirectory,
            _configPath,
            _logPath,
            MixedPort,
            ControllerPort,
            secret);
    }

    private static string RemoveManagedTopLevelKeys(string yaml)
    {
        string[] lines = yaml
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .TrimStart('\uFEFF')
            .Split('\n');

        StringBuilder output = new(yaml.Length);

        foreach (string line in lines)
        {
            if (TryGetTopLevelKey(line, out string? key)
                && ManagedTopLevelKeys.Contains(key))
            {
                continue;
            }

            output.AppendLine(line);
        }

        return output.ToString();
    }

    private static bool TryGetTopLevelKey(string line, out string key)
    {
        key = string.Empty;

        if (string.IsNullOrWhiteSpace(line)
            || char.IsWhiteSpace(line[0])
            || line.TrimStart().StartsWith('#'))
        {
            return false;
        }

        int separator = line.IndexOf(':');
        if (separator <= 0)
        {
            return false;
        }

        string candidate = line[..separator].Trim();
        if (candidate.Length == 0
            || candidate.Contains(' ')
            || candidate.Contains('\t'))
        {
            return false;
        }

        key = candidate;
        return true;
    }
}

public sealed record MihomoRuntimeConfig(
    string RuntimeDirectory,
    string ConfigPath,
    string LogPath,
    int MixedPort,
    int ControllerPort,
    string Secret);

public sealed class MihomoConfigurationException(string message) : Exception(message);
