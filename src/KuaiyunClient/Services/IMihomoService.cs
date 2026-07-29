using KuaiyunClient.Models;

namespace KuaiyunClient.Services;

public interface IMihomoService
{
    bool IsRunning { get; }

    string RuntimeConfigPath { get; }

    string LogPath { get; }

    event EventHandler<MihomoLogEventArgs>? LogReceived;

    event EventHandler<MihomoExitedEventArgs>? Exited;

    Task StartAsync(string subscriptionYaml, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProxyNode>> GetNodesAsync(CancellationToken cancellationToken = default);

    Task SelectNodeAsync(ProxyNode node, CancellationToken cancellationToken = default);
}

public sealed class MihomoLogEventArgs(string message, bool isError) : EventArgs
{
    public string Message { get; } = message;

    public bool IsError { get; } = isError;
}

public sealed class MihomoExitedEventArgs(int? exitCode, bool expected) : EventArgs
{
    public int? ExitCode { get; } = exitCode;

    public bool Expected { get; } = expected;
}
