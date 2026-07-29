using KuaiyunClient.Models;
using System.Diagnostics;
using System.Text;

namespace KuaiyunClient.Services;

public sealed class MihomoService : IMihomoService, IDisposable
{
    private static readonly TimeSpan ConfigValidationTimeout = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan ControllerStartupTimeout = TimeSpan.FromSeconds(45);

    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly object _logGate = new();
    private readonly MihomoConfigService _configService = new();
    private readonly string _corePath;

    private Process? _process;
    private MihomoApiClient? _apiClient;
    private StreamWriter? _logWriter;
    private bool _stopping;
    private bool _disposed;

    public MihomoService()
    {
        _corePath = Path.Combine(AppContext.BaseDirectory, "core", "mihomo.exe");
    }

    public bool IsRunning => _process is { HasExited: false };

    public string RuntimeConfigPath => _configService.ConfigPath;

    public string LogPath => _configService.LogPath;

    public event EventHandler<MihomoLogEventArgs>? LogReceived;

    public event EventHandler<MihomoExitedEventArgs>? Exited;

    public async Task StartAsync(
        string subscriptionYaml,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            if (IsRunning)
            {
                return;
            }

            await StopProcessNoLockAsync(CancellationToken.None);

            if (!File.Exists(_corePath))
            {
                throw new MihomoCoreNotFoundException(
                    $"缺少 Mihomo 内核：{_corePath}{Environment.NewLine}" +
                    "请重新安装快云客户端，或使用完整的 GitHub Actions 构建包。");
            }

            MihomoRuntimeConfig runtime = await _configService.WriteAsync(
                subscriptionYaml,
                cancellationToken);

            OpenLogWriter(runtime.LogPath);
            WriteLog(
                $"准备启动 Mihomo。代理端口：127.0.0.1:{runtime.MixedPort}；"
                + $"Controller：127.0.0.1:{runtime.ControllerPort}。",
                isError: false);

            try
            {
                await ValidateConfigAsync(runtime, cancellationToken);

                ProcessStartInfo startInfo = CreateStartInfo(runtime, testConfiguration: false);
                Process process = new()
                {
                    StartInfo = startInfo,
                    EnableRaisingEvents = true
                };

                process.OutputDataReceived += Process_OutputDataReceived;
                process.ErrorDataReceived += Process_ErrorDataReceived;
                process.Exited += Process_Exited;

                _stopping = false;
                _process = process;

                if (!process.Start())
                {
                    throw new MihomoStartException("Windows 未能启动 Mihomo 进程。");
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                _apiClient = new MihomoApiClient(runtime.ControllerPort, runtime.Secret);
                await _apiClient.WaitUntilReadyAsync(
                    ControllerStartupTimeout,
                    cancellationToken);

                if (process.HasExited)
                {
                    throw new MihomoStartException(
                        $"Mihomo 启动后立即退出，退出代码：{process.ExitCode}。");
                }

                WriteLog(
                    $"Mihomo 已就绪，混合端口 127.0.0.1:{runtime.MixedPort}，"
                    + $"Controller 端口 {runtime.ControllerPort}。",
                    isError: false);
            }
            catch (Exception ex)
            {
                await Task.Delay(250, CancellationToken.None);
                string recentLog = ReadRecentLogLines(30);
                string message = BuildStartupFailureMessage(ex, runtime, recentLog);

                await StopProcessNoLockAsync(CancellationToken.None);
                throw new MihomoStartException(message, ex);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            await StopProcessNoLockAsync(cancellationToken);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public Task<IReadOnlyList<ProxyNode>> GetNodesAsync(
        CancellationToken cancellationToken = default)
    {
        return RequireApiClient().GetNodesAsync(cancellationToken);
    }

    public Task SelectNodeAsync(
        ProxyNode node,
        CancellationToken cancellationToken = default)
    {
        return RequireApiClient().SelectNodeAsync(node, cancellationToken);
    }

    public Task<int?> TestDelayAsync(
        ProxyNode node,
        string testUrl,
        int timeoutMilliseconds,
        CancellationToken cancellationToken = default)
    {
        return RequireApiClient().TestDelayAsync(
            node,
            testUrl,
            timeoutMilliseconds,
            cancellationToken);
    }

    private async Task ValidateConfigAsync(
        MihomoRuntimeConfig runtime,
        CancellationToken cancellationToken)
    {
        WriteLog("正在验证 Mihomo 运行配置...", isError: false);

        using Process process = new()
        {
            StartInfo = CreateStartInfo(runtime, testConfiguration: true)
        };

        if (!process.Start())
        {
            throw new MihomoConfigurationException("Windows 未能启动 Mihomo 配置验证进程。");
        }

        Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> standardErrorTask = process.StandardError.ReadToEndAsync();

        using CancellationTokenSource timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(ConfigValidationTimeout);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKillProcess(process);
            throw new MihomoConfigurationException(
                $"Mihomo 配置验证超过 {ConfigValidationTimeout.TotalSeconds:0} 秒，已终止验证。");
        }

        string standardOutput = await standardOutputTask;
        string standardError = await standardErrorTask;
        string diagnostics = CombineProcessOutput(standardOutput, standardError);

        if (process.ExitCode != 0)
        {
            throw new MihomoConfigurationException(
                "订阅生成的 Mihomo 配置无效。"
                + Environment.NewLine
                + $"退出代码：{process.ExitCode}"
                + (string.IsNullOrWhiteSpace(diagnostics)
                    ? string.Empty
                    : Environment.NewLine + diagnostics));
        }

        if (!string.IsNullOrWhiteSpace(diagnostics))
        {
            foreach (string line in diagnostics.Split(
                         ['\r', '\n'],
                         StringSplitOptions.RemoveEmptyEntries))
            {
                WriteLog("配置验证：" + line, isError: false);
            }
        }

        WriteLog("Mihomo 配置验证通过。", isError: false);
    }

    private ProcessStartInfo CreateStartInfo(
        MihomoRuntimeConfig runtime,
        bool testConfiguration)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = _corePath,
            WorkingDirectory = runtime.RuntimeDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        if (testConfiguration)
        {
            startInfo.ArgumentList.Add("-t");
        }

        startInfo.ArgumentList.Add("-d");
        startInfo.ArgumentList.Add(runtime.RuntimeDirectory);
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add(runtime.ConfigPath);
        return startInfo;
    }

    private MihomoApiClient RequireApiClient()
    {
        if (!IsRunning || _apiClient is null)
        {
            throw new MihomoApiException("Mihomo 尚未运行。");
        }

        return _apiClient;
    }

    private async Task StopProcessNoLockAsync(CancellationToken cancellationToken)
    {
        Process? process = _process;
        _stopping = true;

        _apiClient?.Dispose();
        _apiClient = null;

        if (process is not null)
        {
            try
            {
                if (!process.HasExited)
                {
                    WriteLog("正在停止 Mihomo。", isError: false);
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(cancellationToken);
                }
            }
            catch (InvalidOperationException)
            {
                // 进程已经退出或尚未成功启动。
            }
            finally
            {
                process.OutputDataReceived -= Process_OutputDataReceived;
                process.ErrorDataReceived -= Process_ErrorDataReceived;
                process.Exited -= Process_Exited;
                process.Dispose();
            }
        }

        _process = null;
        CloseLogWriter();
    }

    private void Process_OutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(e.Data))
        {
            WriteLog(e.Data, isError: false);
        }
    }

    private void Process_ErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(e.Data))
        {
            WriteLog(e.Data, isError: true);
        }
    }

    private void Process_Exited(object? sender, EventArgs e)
    {
        Process? process = sender as Process;
        int? exitCode = null;

        try
        {
            exitCode = process?.ExitCode;
        }
        catch (InvalidOperationException)
        {
            // 无法读取退出代码时保持 null。
        }

        bool expected = _stopping;
        WriteLog(
            expected
                ? "Mihomo 已停止。"
                : $"Mihomo 意外退出，退出代码：{exitCode?.ToString() ?? "未知"}。",
            isError: !expected);

        Exited?.Invoke(this, new MihomoExitedEventArgs(exitCode, expected));
    }

    private void OpenLogWriter(string logPath)
    {
        lock (_logGate)
        {
            CloseLogWriterNoLock();

            FileStream stream = new(
                logPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite);

            _logWriter = new StreamWriter(stream, new UTF8Encoding(false))
            {
                AutoFlush = true
            };

            _logWriter.WriteLine();
            _logWriter.WriteLine($"===== {DateTime.Now:yyyy-MM-dd HH:mm:ss} =====");
        }
    }

    private void CloseLogWriter()
    {
        lock (_logGate)
        {
            CloseLogWriterNoLock();
        }
    }

    private void CloseLogWriterNoLock()
    {
        _logWriter?.Dispose();
        _logWriter = null;
    }

    private void WriteLog(string message, bool isError)
    {
        string line = $"[{DateTime.Now:HH:mm:ss}] {message}";

        lock (_logGate)
        {
            try
            {
                _logWriter?.WriteLine(line);
            }
            catch (ObjectDisposedException)
            {
                // 进程结束时输出事件可能晚于日志关闭。
            }
        }

        LogReceived?.Invoke(this, new MihomoLogEventArgs(message, isError));
    }

    private string ReadRecentLogLines(int maximumLines)
    {
        try
        {
            lock (_logGate)
            {
                _logWriter?.Flush();
            }

            if (!File.Exists(LogPath))
            {
                return string.Empty;
            }

            return string.Join(
                Environment.NewLine,
                File.ReadLines(LogPath).TakeLast(maximumLines));
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    private static string BuildStartupFailureMessage(
        Exception exception,
        MihomoRuntimeConfig runtime,
        string recentLog)
    {
        StringBuilder message = new();
        message.Append("Mihomo 启动失败：").AppendLine(exception.Message);

        if (exception.InnerException is not null
            && !string.Equals(
                exception.InnerException.Message,
                exception.Message,
                StringComparison.Ordinal))
        {
            message.Append("内部原因：")
                .AppendLine(exception.InnerException.Message);
        }

        message.Append("运行配置：").AppendLine(runtime.ConfigPath);
        message.Append("运行日志：").Append(runtime.LogPath);

        if (!string.IsNullOrWhiteSpace(recentLog))
        {
            message.AppendLine()
                .AppendLine("最近日志：")
                .Append(recentLog);
        }

        return message.ToString();
    }

    private static string CombineProcessOutput(string standardOutput, string standardError)
    {
        return string.Join(
            Environment.NewLine,
            new[] { standardOutput.Trim(), standardError.Trim() }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static void TryKillProcess(Process process)
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
            // 超时清理阶段尽最大努力终止即可。
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch
        {
            // 释放阶段不能阻止应用退出。
        }

        _disposed = true;
        _lifecycleGate.Dispose();
        CloseLogWriter();
    }
}

public sealed class MihomoCoreNotFoundException(string message) : Exception(message);

public sealed class MihomoStartException : Exception
{
    public MihomoStartException(string message)
        : base(message)
    {
    }

    public MihomoStartException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}