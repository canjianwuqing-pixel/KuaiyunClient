using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace KuaiyunClient.Services;

public sealed class MihomoConfigService
{
    public const int MixedPort = 7890;

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
        "unified-delay",
        "external-controller",
        "secret"
    };

    private static readonly HashSet<string> ManagedTopLevelBlocks = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "tun"
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

        EnsureMixedPortAvailable();
        int controllerPort = ReserveLoopbackPort();

        string secret = Convert
            .ToHexString(RandomNumberGenerator.GetBytes(24))
            .ToLowerInvariant();

        string originalConfig = RemoveManagedTopLevelEntries(subscriptionYaml);
        string generatedHeader = $$"""
# 此文件由快云客户端自动生成，请勿手动修改。
mixed-port: {{MixedPort}}
allow-lan: false
bind-address: 127.0.0.1
mode: rule
log-level: info
unified-delay: true
external-controller: "127.0.0.1:{{controllerPort}}"
secret: "{{secret}}"
tun:
  enable: false

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
            controllerPort,
            secret);
    }

    private static void EnsureMixedPortAvailable()
    {
        try
        {
            using Socket socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
            {
                ExclusiveAddressUse = true
            };
            socket.Bind(new IPEndPoint(IPAddress.Loopback, MixedPort));
        }
        catch (SocketException ex)
        {
            throw new MihomoConfigurationException(
                $"本地代理端口 127.0.0.1:{MixedPort} 已被其他程序占用。"
                + Environment.NewLine
                + "请退出其他代理软件或旧版快云客户端后重试。",
                ex);
        }
    }

    private static int ReserveLoopbackPort()
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            TcpListener listener = new(IPAddress.Loopback, 0);
            listener.Server.ExclusiveAddressUse = true;
            listener.Start();

            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                if (port != MixedPort)
                {
                    return port;
                }
            }
            finally
            {
                listener.Stop();
            }
        }

        throw new MihomoConfigurationException("无法为 Mihomo Controller 分配本地端口。");
    }

    private static string RemoveManagedTopLevelEntries(string yaml)
    {
        string[] lines = yaml
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .TrimStart('\uFEFF')
            .Split('\n');

        StringBuilder output = new(yaml.Length);
        bool skippingManagedBlock = false;

        foreach (string line in lines)
        {
            if (skippingManagedBlock)
            {
                if (!IsTopLevelContentLine(line))
                {
                    continue;
                }

                skippingManagedBlock = false;
            }

            if (TryGetTopLevelKey(line, out string? key))
            {
                if (ManagedTopLevelBlocks.Contains(key))
                {
                    skippingManagedBlock = true;
                    continue;
                }

                if (ManagedTopLevelKeys.Contains(key))
                {
                    continue;
                }
            }

            output.AppendLine(line);
        }

        return output.ToString();
    }

    private static bool IsTopLevelContentLine(string line)
    {
        return !string.IsNullOrWhiteSpace(line)
            && !char.IsWhiteSpace(line[0])
            && !line.TrimStart().StartsWith('#');
    }

    private static bool TryGetTopLevelKey(string line, out string key)
    {
        key = string.Empty;

        if (!IsTopLevelContentLine(line))
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

public sealed class MihomoConfigurationException : Exception
{
    public MihomoConfigurationException(string message)
        : base(message)
    {
    }

    public MihomoConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}