using System.Text.Json;

namespace KuaiyunClient.Services;

public sealed class ClientSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _settingsPath;

    public ClientSettingsService()
    {
        string settingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KuaiyunClient",
            "settings");

        Directory.CreateDirectory(settingsDirectory);
        _settingsPath = Path.Combine(settingsDirectory, "client-settings.json");
    }

    public string SettingsPath => _settingsPath;

    public async Task<ClientSettings> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsPath))
        {
            return ClientSettings.Default;
        }

        try
        {
            string json = await File.ReadAllTextAsync(_settingsPath, cancellationToken);
            return JsonSerializer.Deserialize<ClientSettings>(json, JsonOptions)
                ?? ClientSettings.Default;
        }
        catch (JsonException)
        {
            return ClientSettings.Default;
        }
        catch (IOException)
        {
            return ClientSettings.Default;
        }
    }

    public async Task SaveAsync(
        ClientSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        string temporaryPath = _settingsPath + ".tmp";
        string json = JsonSerializer.Serialize(settings, JsonOptions);
        await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
        File.Move(temporaryPath, _settingsPath, overwrite: true);
    }
}

public sealed record ClientSettings(
    bool StartWithWindows,
    bool AutoConnect,
    bool UseSystemProxy)
{
    public static ClientSettings Default { get; } = new(
        StartWithWindows: false,
        AutoConnect: false,
        UseSystemProxy: true);
}
