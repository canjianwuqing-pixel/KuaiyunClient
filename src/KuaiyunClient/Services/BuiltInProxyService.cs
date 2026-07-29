using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KuaiyunClient.Services;

public sealed class BuiltInProxyService : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _corePath;
    private readonly string _recoveryRoot;
    private bool _disposed;

    public BuiltInProxyService()
    {
        _corePath = Path.Combine(AppContext.BaseDirectory, "core", "mihomo.exe");
        _recoveryRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KuaiyunClient",
            "recovery");
        Directory.CreateDirectory(_recoveryRoot);
    }

    public event EventHandler<BuiltInProxyStatusEventArgs>? StatusChanged;

    public async Task<T> ExecuteAsync<T>(
        IEnumerable<string>? proxyValues,
        Func<HttpClient, CancellationToken, Task<T>> operation,
        Func<Exception, bool>? isFatal = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(operation);

        IReadOnlyList<BuiltInProxyEndpoint> endpoints = BuiltInProxyParser.ParseMany(proxyValues);
        if (endpoints.Count == 0)
        {
            throw new BuiltInProxyUnavailableException("没有配置可用的 BuiltInProxy 应急代理。");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            List<string> errors = [];

            for (int index = 0; index < endpoints.Count; index++)
            {
                BuiltInProxyEndpoint endpoint = endpoints[index];
                RaiseStatus(
                    endpoint,
                    $"正在尝试应急代理 {index + 1}/{endpoints.Count}：{endpoint.DisplayName}");

                try
                {
                    await using IBuiltInProxySession session = await CreateSessionAsync(
                        endpoint,
                        cancellationToken);

                    T result = await operation(session.Client, cancellationToken);
                    RaiseStatus(endpoint, $"应急代理已生效：{endpoint.DisplayName}");
                    return result;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    if (isFatal?.Invoke(ex) == true)
                    {
                        throw;
                    }

                    errors.Add($"{endpoint.DisplayName}: {ex.Message}");
                    RaiseStatus(endpoint, $"应急代理失败：{endpoint.DisplayName}");
                }
            }

            throw new BuiltInProxyUnavailableException(
                "所有 BuiltInProxy 应急代理均不可用。"
                + Environment.NewLine
                + string.Join(Environment.NewLine, errors));
        }
        finally
        {
            _gate.Release();
        }
    }

    private Task<IBuiltInProxySession> CreateSessionAsync(
        BuiltInProxyEndpoint endpoint,
        CancellationToken cancellationToken)
    {
        return endpoint.Kind switch
        {
            BuiltInProxyKind.Direct => Task.FromResult<IBuiltInProxySession>(
                CreateDirectSession(endpoint)),
            BuiltInProxyKind.Shadowsocks => CreateShadowsocksSessionAsync(
                endpoint,
                cancellationToken),
            _ => throw new BuiltInProxyUnavailableException("不支持的应急代理类型。")
        };
    }

    private static IBuiltInProxySession CreateDirectSession(BuiltInProxyEndpoint endpoint)
    {
        Uri proxyUri = new(endpoint.NormalizedValue);
        WebProxy proxy = new(proxyUri);

        if (!string.IsNullOrWhiteSpace(endpoint.Username))
        {
            proxy.Credentials = new NetworkCredential(
                endpoint.Username,
                endpoint.Password ?? string.Empty);
        }

        SocketsHttpHandler handler = new()
        {
            UseProxy = true,
            Proxy = proxy,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            AutomaticDecompression = DecompressionMethods.All
        };

        HttpClient client = new(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(25)
        };

        return new DirectProxySession(client);
    }

    private async Task<IBuiltInProxySession> CreateShadowsocksSessionAsync(
        BuiltInProxyEndpoint endpoint,
        CancellationToken cancellationToken)
    {
        ShadowsocksProxySettings settings = endpoint.Shadowsocks
            ?? throw new BuiltInProxyUnavailableException("Shadowsocks 应急代理缺少配置。") ;

        if (!File.Exists(_corePath))
        {
            throw new BuiltInProxyUnavailableException(
                $"缺少 Mihomo 内核，无法启动 Shadowsocks 应急代理：{_corePath}");
        }

        int mixedPort = ReserveLoopbackPort();
        int controllerPort = ReserveLoopbackPort();
        string secret = Convert
            .ToHexString(RandomNumberGenerator.GetBytes(24))
            .ToLowerInvariant();
        string sessionDirectory = Path.Combine(
            _recoveryRoot,
            DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionDirectory);

        string configPath = Path.Combine(sessionDirectory, "config.yaml");
        string config = BuildShadowsocksConfig(
            settings,
            mixedPort,
            controllerPort,
            secret);
        await File.WriteAllTextAsync(
            configPath,
            config,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);

        ProcessStartInfo startInfo = new()
        {
            FileName = _corePath,
            WorkingDirectory = sessionDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.ArgumentList.Add("-d");
        startInfo.ArgumentList.Add(sessionDirectory);
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add(configPath);

        Process process = new()
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        StringBuilder log = new();
        object logGate = new();
        DataReceivedEventHandler outputHandler = (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data))
            {
                lock (logGate)
                {
                    log.AppendLine(eventArgs.Data);
                }
            }
        };

        process.OutputDataReceived += outputHandler;
        process.ErrorDataReceived += outputHandler;

        MihomoApiClient? apiClient = null;
        SocketsHttpHandler? httpHandler = null;
        HttpClient? httpClient = null;

        try
        {
            if (!process.Start())
            {
                throw new BuiltInProxyUnavailableException(
                    "Windows 未能启动 Mihomo 应急代理进程。");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            apiClient = new MihomoApiClient(controllerPort, secret);
            await apiClient.WaitUntilReadyAsync(TimeSpan.FromSeconds(12), cancellationToken);

            if (process.HasExited)
            {
                throw new BuiltInProxyUnavailableException(
                    $"Mihomo 应急代理启动后立即退出，退出代码：{process.ExitCode}。");
            }

            WebProxy localProxy = new($"http://127.0.0.1:{mixedPort}");
            httpHandler = new SocketsHttpHandler
            {
                UseProxy = true,
                Proxy = localProxy,
                ConnectTimeout = TimeSpan.FromSeconds(10),
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                AutomaticDecompression = DecompressionMethods.All
            };
            httpClient = new HttpClient(httpHandler, disposeHandler: true)
            {
                Timeout = TimeSpan.FromSeconds(25)
            };

            return new ShadowsocksProxySession(
                httpClient,
                process,
                apiClient,
                sessionDirectory,
                outputHandler);
        }
        catch (Exception ex)
        {
            httpClient?.Dispose();
            if (httpClient is null)
            {
                httpHandler?.Dispose();
            }
            apiClient?.Dispose();

            TryStopProcess(process);
            process.OutputDataReceived -= outputHandler;
            process.ErrorDataReceived -= outputHandler;
            process.Dispose();
            TryDeleteDirectory(sessionDirectory);

            string recentLog;
            lock (logGate)
            {
                recentLog = string.Join(
                    Environment.NewLine,
                    log.ToString()
                        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                        .TakeLast(10));
            }

            throw new BuiltInProxyUnavailableException(
                string.IsNullOrWhiteSpace(recentLog)
                    ? "Shadowsocks 应急代理启动失败：" + ex.Message
                    : "Shadowsocks 应急代理启动失败：" + ex.Message
                      + Environment.NewLine
                      + "最近日志："
                      + Environment.NewLine
                      + recentLog,
                ex);
        }
    }

    private static string BuildShadowsocksConfig(
        ShadowsocksProxySettings settings,
        int mixedPort,
        int controllerPort,
        string secret)
    {
        return $$"""
        # 快云客户端 BuiltInProxy 临时恢复配置。
        mixed-port: {{mixedPort}}
        allow-lan: false
        bind-address: 127.0.0.1
        mode: rule
        log-level: warning
        external-controller: 127.0.0.1:{{controllerPort}}
        secret: {{YamlQuote(secret)}}
        tun:
          enable: false
        proxies:
          - name: RECOVERY
            type: ss
            server: {{YamlQuote(settings.Server)}}
            port: {{settings.Port}}
            cipher: {{YamlQuote(settings.Method)}}
            password: {{YamlQuote(settings.Password)}}
        rules:
          - MATCH,RECOVERY
        """;
    }

    private static string YamlQuote(string value)
    {
        return JsonSerializer.Serialize(value);
    }

    private static int ReserveLoopbackPort()
    {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static void TryStopProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(3000);
            }
        }
        catch
        {
            // 应急通道释放阶段尽最大努力停止即可。
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // 临时目录清理失败不影响主流程。
        }
    }

    private void RaiseStatus(BuiltInProxyEndpoint endpoint, string message)
    {
        StatusChanged?.Invoke(this, new BuiltInProxyStatusEventArgs(endpoint, message));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
    }

    private interface IBuiltInProxySession : IAsyncDisposable
    {
        HttpClient Client { get; }
    }

    private sealed class DirectProxySession(HttpClient client) : IBuiltInProxySession
    {
        public HttpClient Client { get; } = client;

        public ValueTask DisposeAsync()
        {
            Client.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ShadowsocksProxySession(
        HttpClient client,
        Process process,
        MihomoApiClient apiClient,
        string runtimeDirectory,
        DataReceivedEventHandler outputHandler) : IBuiltInProxySession
    {
        private bool _disposed;

        public HttpClient Client { get; } = client;

        public ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _disposed = true;
            Client.Dispose();
            apiClient.Dispose();
            TryStopProcess(process);
            process.OutputDataReceived -= outputHandler;
            process.ErrorDataReceived -= outputHandler;
            process.Dispose();
            TryDeleteDirectory(runtimeDirectory);
            return ValueTask.CompletedTask;
        }
    }
}

public sealed class BuiltInProxyStatusEventArgs(
    BuiltInProxyEndpoint endpoint,
    string message) : EventArgs
{
    public BuiltInProxyEndpoint Endpoint { get; } = endpoint;

    public string Message { get; } = message;
}

public sealed class BuiltInProxyUnavailableException : Exception
{
    public BuiltInProxyUnavailableException(string message)
        : base(message)
    {
    }

    public BuiltInProxyUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
