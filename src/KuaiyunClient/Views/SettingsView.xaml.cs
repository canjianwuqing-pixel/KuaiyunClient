using System.Windows;
using System.Windows.Controls;

namespace KuaiyunClient.Views;

public partial class SettingsView : UserControl
{
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

    private void OptionChanged(object sender, RoutedEventArgs e)
    {
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
