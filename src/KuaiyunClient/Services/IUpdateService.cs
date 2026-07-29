using KuaiyunClient.Models;

namespace KuaiyunClient.Services;

public interface IUpdateService
{
    Task<UpdateInfo?> CheckAsync(
        AppConfig config,
        Version currentVersion,
        CancellationToken cancellationToken = default);
}

public sealed record UpdateInfo(
    Version Version,
    string DownloadUrl,
    string? ReleaseNotes,
    bool IsRequired);
