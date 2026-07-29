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
    private readonly V2BoardApi _v2BoardApi = new();

    private AppConfig? _appConfig;
    private UserSession? _userSession;
    private bool _loginBusy;

    private static readonly Brush ActiveBrush = new SolidColorBrush(Color.FromRgb(32, 58, 87));
    private static readonly Brush InactiveBrush = Brushes.Transparent;
    private static readonly Brush ActiveTextBrush = Brushes.White;
    private static readonly Brush InactiveTextBrush = new SolidColorBrush(Color.FromRgb(175, 192, 211));

    public ShellWindow()
    {
        InitializeComponent();

        _loginView.LoginRequested += LoginView_LoginRequested;
        HomeNavButton.IsEnabled = false;
        NodesNavButton.IsEnabled = false;

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
            _loginView.ShowStatus("配置已就绪，请登录账号。");
        }
        catch (Exception ex)
        {
            ConfigStatusText.Text = "读取失败";
            _loginView.ShowStatus("配置读取失败，暂时无法登录。");
            MessageBox.Show(
                ex.Message,
                "配置加载失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void LoginView_LoginRequested(object? sender, LoginRequestedEventArgs e)
    {
        if (_loginBusy)
        {
            return;
        }

        if (_appConfig is null)
        {
            _loginView.ShowStatus("云端配置尚未加载完成，请稍后重试。");
            return;
        }

        _loginBusy = true;
        _loginView.SetBusy(true, "正在登录并读取账号信息...");

        try
        {
            UserSession session = await _v2BoardApi.LoginAsync(
                _appConfig,
                e.Email,
                e.Password);

            _userSession = session;
            _homeView.ShowSession(session);
            _homeView.ShowConnectionState(connected: false);

            HomeNavButton.IsEnabled = true;
            NodesNavButton.IsEnabled = true;
            LoginNavButton.Content = "账号";

            _loginView.ClearPassword();
            _loginView.SetBusy(false, "登录成功。");
            Navigate("Home");
        }
        catch (V2BoardAuthenticationException ex)
        {
            _loginView.SetBusy(false, ex.Message);
        }
        catch (Exception ex)
        {
            _loginView.SetBusy(false, "登录失败：" + ex.Message);
        }
        finally
        {
            _loginBusy = false;
        }
    }

    private void NavigationButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string page })
        {
            if ((page == "Home" || page == "Nodes") && _userSession is null)
            {
                _loginView.ShowStatus("请先登录账号。");
                Navigate("Login");
                return;
            }

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
        _v2BoardApi.Dispose();
        _configService.Dispose();
    }
}
