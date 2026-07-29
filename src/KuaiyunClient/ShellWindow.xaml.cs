using KuaiyunClient.Models;
using KuaiyunClient.Services;
using KuaiyunClient.Views;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KuaiyunClient;

public partial class ShellWindow : Window
{
    private readonly LoginView _loginView = new();
    private readonly HomeView _homeView = new();
    private readonly NodesView _nodesView = new();
    private readonly SettingsView _settingsView = new();
    private readonly ConfigService _configService = new();

    private AppConfig? _appConfig;

    private static readonly Brush ActiveBrush = new SolidColorBrush(Color.FromRgb(32, 58, 87));
    private static readonly Brush InactiveBrush = Brushes.Transparent;
    private static readonly Brush ActiveTextBrush = Brushes.White;
    private static readonly Brush InactiveTextBrush = new SolidColorBrush(Color.FromRgb(175, 192, 211));

    public ShellWindow()
    {
        InitializeComponent();
        Navigate("Login");
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ConfigStatusText.Text = "正在读取...";

        try
        {
            ConfigLoadResult result = await _configService.LoadAsync();
            _appConfig = result.Config;

            Title = _appConfig.AppName;
            BrandNameText.Text = _appConfig.AppName;
            ConfigStatusText.Text = result.FromCache ? "本地缓存" : "云端已连接";
        }
        catch (Exception ex)
        {
            ConfigStatusText.Text = "读取失败";
            MessageBox.Show(
                ex.Message,
                "配置加载失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void NavigationButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string page })
        {
            Navigate(page);
        }
    }

    private void Navigate(string page)
    {
        PageHost.Content = page switch
        {
            "Home" => _homeView,
            "Nodes" => _nodesView,
            "Settings" => _settingsView,
            _ => _loginView
        };

        foreach (Button button in new[] { LoginNavButton, HomeNavButton, NodesNavButton, SettingsNavButton })
        {
            bool active = string.Equals(button.Tag?.ToString(), page, StringComparison.Ordinal);
            button.Background = active ? ActiveBrush : InactiveBrush;
            button.Foreground = active ? ActiveTextBrush : InactiveTextBrush;
        }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _configService.Dispose();
    }
}
