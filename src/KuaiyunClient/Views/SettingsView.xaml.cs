using System.Windows;
using System.Windows.Controls;

namespace KuaiyunClient.Views;

public partial class SettingsView : UserControl
{
    private bool _suppressOptionChanged;

    public event EventHandler<ClientOptions>? OptionsChanged;

    public event EventHandler? ReloadConfigRequested;

    public event EventHandler? CheckUpdateRequested;

    public SettingsView()
    {
        InitializeComponent();
    }

    public void SetVersion(string version)
    {
        VersionText.Text = $"当前版本：{version}";
    }

    public void SetOptions(ClientOptions options)
    {
        _suppressOptionChanged = true;
        try
        {
            StartWithWindowsCheckBox.IsChecked = options.StartWithWindows;
            AutoConnectCheckBox.IsChecked = options.AutoConnect;
            SystemProxyCheckBox.IsChecked = options.UseSystemProxy;
        }
        finally
        {
            _suppressOptionChanged = false;
        }
    }

    public void ShowSystemProxyStatus(bool enabled, string? message = null)
    {
        SystemProxyStatusText.Text = message
            ?? (enabled ? "系统代理：已启用" : "系统代理：未启用");
    }

    private void OptionChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressOptionChanged)
        {
            return;
        }

        OptionsChanged?.Invoke(this, new ClientOptions(
            StartWithWindowsCheckBox.IsChecked == true,
            AutoConnectCheckBox.IsChecked == true,
            SystemProxyCheckBox.IsChecked == true));
    }

    private void ReloadConfigButton_Click(object sender, RoutedEventArgs e)
    {
        ReloadConfigRequested?.Invoke(this, EventArgs.Empty);
    }

    private void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateRequested?.Invoke(this, EventArgs.Empty);
    }
}

public sealed record ClientOptions(
    bool StartWithWindows,
    bool AutoConnect,
    bool UseSystemProxy);
