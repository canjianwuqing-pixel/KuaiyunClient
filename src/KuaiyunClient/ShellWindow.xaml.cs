using KuaiyunClient.Models;
using KuaiyunClient.Services;
using KuaiyunClient.Views;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KuaiyunClient;

public partial class ShellWindow : Window
{
    private const string SystemProxyAddress = "127.0.0.1:7890";

    private readonly LoginView _loginView = new();
    private readonly HomeView _homeView = new();
    private readonly NodesView _nodesView = new();
    private readonly PurchaseView _purchaseView = new();
    private readonly ProfileView _profileView = new();

    private readonly ConfigService _configService = new();
    private readonly V2BoardApi _v2BoardApi = new();
    private readonly SubscriptionService _subscriptionService = new();
    private readonly MihomoService _mihomoService = new();
    private readonly SystemProxyService _systemProxyService = new();
    private readonly ClientSettingsService _clientSettingsService = new();

    private AppConfig? _appConfig;
    private UserSession? _userSession;
    private string? _subscriptionYaml;
    private ProxyNode? _selectedNode;
    private ClientSettings _clientSettings = ClientSettings.Default;

    private bool _loginBusy;
    private bool _subscriptionBusy;
    private bool _connectionBusy;
    private bool _closing;
    private bool _closeCompleted;
    private bool _resourcesDisposed;

    private static readonly Brush ActiveNavBrush = new SolidColorBrush(Color.FromRgb(232, 237, 245));
    private static readonly Brush ActiveNavTextBrush = new SolidColorBrush(Color.FromRgb(24, 31, 45));
    private static readonly Brush InactiveNavTextBrush = new SolidColorBrush(Color.FromRgb(104, 116, 135));

    public ShellWindow()
    {
        InitializeComponent();

        _loginView.LoginRequested += LoginView_LoginRequested;
        _homeView.ConnectionToggleRequested += HomeView_ConnectionToggleRequested;
        _homeView.ServerSelectionRequested += HomeView_ServerSelectionRequested;
        _homeView.AnnouncementRequested += HomeView_AnnouncementRequested;
        _nodesView.BackRequested += NodesView_BackRequested;
        _nodesView.NodeSelectionRequested += NodesView_NodeSelectionRequested;
        _purchaseView.PurchaseRequested += PurchaseView_PurchaseRequested;
        _profileView.ActionRequested += ProfileView_ActionRequested;
        _mihomoService.Exited += MihomoService_Exited;

        Navigate("Login");
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _systemProxyService.RestoreAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "网络设置恢复失败，请检查 Windows 代理设置。"
                + Environment.NewLine
                + ex.Message,
                "网络提示",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        _clientSettings = await _clientSettingsService.LoadAsync();

        try
        {
            ConfigLoadResult result = await _configService.LoadAsync();
            _appConfig = result.Config;
            Title = _appConfig.AppName;
            _loginView.ShowStatus("请输入账号密码登录。");
        }
        catch (Exception ex)
        {
            _loginView.ShowStatus("服务配置读取失败，请稍后重试。");
            MessageBox.Show(
                ex.Message,
                "服务暂不可用",
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
            _loginView.ShowStatus("服务配置尚未就绪，请稍后重试。");
            return;
        }

        _loginBusy = true;
        _loginView.SetBusy(true, "正在登录...");

        try
        {
            UserSession session = await _v2BoardApi.LoginAsync(
                _appConfig,
                e.Email,
                e.Password);

            _userSession = session;
            _homeView.ShowSession(session);
            _purchaseView.ShowSession(session);
            _profileView.ShowSession(session);
            _loginView.ClearPassword();
            _loginView.SetBusy(true, "正在读取线路...");

            bool loaded = await RefreshSubscriptionAsync();
            _loginView.SetBusy(
                false,
                loaded ? "登录成功。" : "登录成功，线路读取失败，请稍后重试。");

            BottomNavigation.Visibility = Visibility.Visible;
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

    private async void HomeView_ConnectionToggleRequested(object? sender, EventArgs e)
    {
        if (_connectionBusy)
        {
            return;
        }

        _connectionBusy = true;
        try
        {
            if (_mihomoService.IsRunning)
            {
                await DisconnectAsync();
                return;
            }

            if (_userSession is null)
            {
                Navigate("Login");
                return;
            }

            if (string.IsNullOrWhiteSpace(_subscriptionYaml) || _selectedNode is null)
            {
                _homeView.ShowConnectionError("暂无可用线路，请稍后重试。", _selectedNode);
                return;
            }

            _homeView.ShowConnectionState(ConnectionUiState.Connecting, _selectedNode);
            await _mihomoService.StartAsync(_subscriptionYaml);

            // SelectNodeAsync 会在返回前读取真实代理组的 now 字段确认切换结果。
            await _mihomoService.SelectNodeAsync(_selectedNode);

            if (_clientSettings.UseSystemProxy)
            {
                await _systemProxyService.EnableAsync(SystemProxyAddress);
            }

            _nodesView.SetSelectedNode(_selectedNode);
            _homeView.ShowConnectionState(ConnectionUiState.Connected, _selectedNode);
        }
        catch (Exception ex)
        {
            string? rollbackError = await RollbackConnectionAsync();
            string message = ex.Message;
            if (!string.IsNullOrWhiteSpace(rollbackError))
            {
                message += Environment.NewLine + rollbackError;
            }

            _homeView.ShowConnectionError(message, _selectedNode);
        }
        finally
        {
            _connectionBusy = false;
        }
    }

    private async Task DisconnectAsync()
    {
        _homeView.ShowConnectionState(ConnectionUiState.Connecting, _selectedNode);

        try
        {
            await _systemProxyService.RestoreAsync();
            await _mihomoService.StopAsync();
            _homeView.ShowConnectionState(ConnectionUiState.Disconnected, _selectedNode);
        }
        catch (Exception ex)
        {
            _homeView.ShowConnectionState(
                _mihomoService.IsRunning ? ConnectionUiState.Connected : ConnectionUiState.Disconnected,
                _selectedNode);

            MessageBox.Show(
                "断开连接时恢复网络失败。"
                + Environment.NewLine
                + ex.Message,
                "网络提示",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async Task<string?> RollbackConnectionAsync()
    {
        try
        {
            await _systemProxyService.RestoreAsync();
        }
        catch (Exception ex)
        {
            return "恢复网络设置失败：" + ex.Message;
        }

        try
        {
            if (_mihomoService.IsRunning)
            {
                await _mihomoService.StopAsync();
            }
        }
        catch (Exception ex)
        {
            return "停止连接失败：" + ex.Message;
        }

        return null;
    }

    private void HomeView_ServerSelectionRequested(object? sender, EventArgs e)
    {
        if (_userSession is null)
        {
            Navigate("Login");
            return;
        }

        _nodesView.SetSelectedNode(_selectedNode);
        Navigate("Nodes");
    }

    private void HomeView_AnnouncementRequested(object? sender, EventArgs e)
    {
        MessageBox.Show(
            "暂无公告。",
            "系统公告",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void NodesView_BackRequested(object? sender, EventArgs e)
    {
        Navigate("Home");
    }

    private async void NodesView_NodeSelectionRequested(object? sender, ProxyNode node)
    {
        if (_connectionBusy)
        {
            return;
        }

        if (!_mihomoService.IsRunning)
        {
            _selectedNode = node;
            _homeView.SetCurrentNode(node);
            _homeView.ShowConnectionState(ConnectionUiState.Disconnected, node);
            _nodesView.SetSelectedNode(node);
            Navigate("Home");
            return;
        }

        ProxyNode? previousNode = _selectedNode;
        _connectionBusy = true;
        _nodesView.SetBusy(true, "正在切换线路...");
        _homeView.ShowConnectionState(ConnectionUiState.Connecting, node);

        try
        {
            // 只有实际代理组回读等于 node.Name 时，此调用才会成功返回。
            await _mihomoService.SelectNodeAsync(node);
            _selectedNode = node;
            _nodesView.SetSelectedNode(node);
            _nodesView.SetBusy(false);
            _homeView.ShowConnectionState(ConnectionUiState.Connected, node);
            Navigate("Home");
        }
        catch (Exception ex)
        {
            _selectedNode = previousNode;
            _nodesView.SetSelectedNode(previousNode);
            _nodesView.SetBusy(false);
            _homeView.ShowConnectionState(ConnectionUiState.Connected, previousNode);
            MessageBox.Show(
                ex.Message,
                "线路切换失败",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            _connectionBusy = false;
        }
    }

    private async Task<bool> RefreshSubscriptionAsync()
    {
        if (_subscriptionBusy || _appConfig is null || _userSession is null)
        {
            return false;
        }

        _subscriptionBusy = true;
        try
        {
            SubscriptionLoadResult result = await _subscriptionService.DownloadAsync(
                _v2BoardApi,
                _appConfig,
                _userSession);

            _subscriptionYaml = result.Yaml;
            _nodesView.SetNodes(result.Nodes);

            if (_selectedNode is null
                || !result.Nodes.Any(node =>
                    string.Equals(node.Name, _selectedNode.Name, StringComparison.Ordinal)))
            {
                // 订阅读取成功后始终以过滤后的第一条真实线路作为默认线路。
                _selectedNode = result.Nodes.FirstOrDefault();
            }

            _nodesView.SetSelectedNode(_selectedNode);
            _homeView.SetCurrentNode(_selectedNode);
            _homeView.ShowConnectionState(ConnectionUiState.Disconnected, _selectedNode);
            return _selectedNode is not null;
        }
        catch (Exception ex)
        {
            _nodesView.ShowStatus("线路读取失败：" + ex.Message);
            _homeView.SetCurrentNode(null);
            _homeView.ShowConnectionState(ConnectionUiState.Disconnected);
            return false;
        }
        finally
        {
            _subscriptionBusy = false;
        }
    }

    private void PurchaseView_PurchaseRequested(object? sender, EventArgs e)
    {
        if (!OpenExternal(_appConfig?.HomePage))
        {
            OpenPanelPath(string.Empty);
        }
    }

    private async void ProfileView_ActionRequested(object? sender, ProfileAction action)
    {
        switch (action)
        {
            case ProfileAction.Orders:
                OpenPanelPath("/dashboard/orders");
                break;
            case ProfileAction.Invite:
                OpenPanelPath("/dashboard/invite");
                break;
            case ProfileAction.Website:
                OpenExternal(_appConfig?.HomePage);
                break;
            case ProfileAction.Announcement:
                Navigate("Home");
                HomeView_AnnouncementRequested(this, EventArgs.Empty);
                break;
            case ProfileAction.Support:
                if (!OpenExternal(_appConfig?.SupportApi))
                {
                    OpenExternal(_appConfig?.TelegramGroup);
                }
                break;
            case ProfileAction.Telegram:
                OpenExternal(_appConfig?.TelegramGroup);
                break;
            case ProfileAction.Password:
                OpenPanelPath("/dashboard/profile");
                break;
            case ProfileAction.Logs:
                OpenLogsDirectory();
                break;
            case ProfileAction.Version:
                ShowVersion();
                break;
            case ProfileAction.Logout:
                await LogoutAsync();
                break;
        }
    }

    private async Task LogoutAsync()
    {
        MessageBoxResult result = MessageBox.Show(
            "确定退出当前账号吗？",
            "退出",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        if (_mihomoService.IsRunning)
        {
            await DisconnectAsync();
        }

        _userSession = null;
        _subscriptionYaml = null;
        _selectedNode = null;
        _nodesView.SetNodes([]);
        _homeView.SetCurrentNode(null);
        BottomNavigation.Visibility = Visibility.Collapsed;
        Navigate("Login");
        _loginView.ShowStatus("已退出账号。");
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
        if (_userSession is null && page != "Login")
        {
            page = "Login";
        }

        PageHost.Content = page switch
        {
            "Home" => _homeView,
            "Nodes" => _nodesView,
            "Purchase" => _purchaseView,
            "Profile" => _profileView,
            _ => _loginView
        };

        BottomNavigation.Visibility = page is "Login" or "Nodes"
            ? Visibility.Collapsed
            : Visibility.Visible;

        foreach (Button button in new[] { HomeNavButton, PurchaseNavButton, ProfileNavButton })
        {
            bool active = string.Equals(button.Tag?.ToString(), page, StringComparison.Ordinal);
            button.Background = active ? ActiveNavBrush : Brushes.Transparent;
            button.Foreground = active ? ActiveNavTextBrush : InactiveNavTextBrush;
        }
    }

    private void OpenPanelPath(string path)
    {
        string? host = _appConfig?.RemoteHosts.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(host))
        {
            MessageBox.Show("暂时无法打开该页面。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string root = host.Trim().TrimEnd('/');
        if (root.EndsWith("/api/v1", StringComparison.OrdinalIgnoreCase))
        {
            root = root[..^7];
        }

        OpenExternal(root + path);
    }

    private static bool OpenExternal(string? address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        return true;
    }

    private static void OpenLogsDirectory()
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KuaiyunClient");
        Directory.CreateDirectory(directory);
        Process.Start(new ProcessStartInfo("explorer.exe", directory) { UseShellExecute = true });
    }

    private static void ShowVersion()
    {
        Version? version = typeof(ShellWindow).Assembly.GetName().Version;
        MessageBox.Show(
            $"当前版本：v{version?.ToString(3) ?? "未知"}",
            "检查版本",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async void MihomoService_Exited(object? sender, MihomoExitedEventArgs e)
    {
        if (e.Expected)
        {
            return;
        }

        try
        {
            await _systemProxyService.RestoreAsync();
        }
        catch
        {
            // 下次启动仍会根据备份再次尝试恢复。
        }

        await Dispatcher.InvokeAsync(() =>
        {
            _connectionBusy = false;
            _homeView.ShowConnectionState(ConnectionUiState.Disconnected, _selectedNode);
            MessageBox.Show(
                "连接已中断，请重新连接。",
                "连接提示",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        });
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_closeCompleted)
        {
            return;
        }

        e.Cancel = true;
        if (_closing)
        {
            return;
        }

        _closing = true;
        IsEnabled = false;

        try
        {
            await _systemProxyService.RestoreAsync();
            if (_mihomoService.IsRunning)
            {
                await _mihomoService.StopAsync();
            }

            DisposeResources();
            _closeCompleted = true;
            Close();
        }
        catch (Exception ex)
        {
            _closing = false;
            IsEnabled = true;
            MessageBox.Show(
                "退出前恢复网络失败，客户端暂未退出。"
                + Environment.NewLine
                + ex.Message,
                "网络提示",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void DisposeResources()
    {
        if (_resourcesDisposed)
        {
            return;
        }

        _resourcesDisposed = true;
        _mihomoService.Exited -= MihomoService_Exited;
        _mihomoService.Dispose();
        _systemProxyService.Dispose();
        _v2BoardApi.Dispose();
        _configService.Dispose();
    }
}
