using KuaiyunClient.Models;
using KuaiyunClient.Services;
using KuaiyunClient.Views;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KuaiyunClient;

public partial class ShellWindow : Window
{
    private const string DelayTestUrl = "https://www.gstatic.com/generate_204";
    private const int DelayTestTimeoutMilliseconds = 5000;
    private const int DelayTestConcurrency = 6;

    private readonly LoginView _loginView = new();
    private readonly HomeView _homeView = new();
    private readonly NodesView _nodesView = new();
    private readonly SettingsView _settingsView = new();
    private readonly ConfigService _configService = new();
    private readonly V2BoardApi _v2BoardApi = new();
    private readonly SubscriptionService _subscriptionService = new();
    private readonly MihomoService _mihomoService = new();

    private AppConfig? _appConfig;
    private UserSession? _userSession;
    private string? _subscriptionYaml;
    private ProxyNode? _selectedNode;
    private bool _loginBusy;
    private bool _subscriptionBusy;
    private bool _connectionBusy;
    private bool _delayTestBusy;

    private static readonly Brush ActiveBrush = new SolidColorBrush(Color.FromRgb(32, 58, 87));
    private static readonly Brush InactiveBrush = Brushes.Transparent;
    private static readonly Brush ActiveTextBrush = Brushes.White;
    private static readonly Brush InactiveTextBrush = new SolidColorBrush(Color.FromRgb(175, 192, 211));

    public ShellWindow()
    {
        InitializeComponent();

        _loginView.LoginRequested += LoginView_LoginRequested;
        _homeView.ConnectionToggleRequested += HomeView_ConnectionToggleRequested;
        _nodesView.RefreshRequested += NodesView_RefreshRequested;
        _nodesView.DelayTestRequested += NodesView_DelayTestRequested;
        _nodesView.NodeSelectionRequested += NodesView_NodeSelectionRequested;
        _mihomoService.Exited += MihomoService_Exited;

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
            _loginView.SetBusy(true, "登录成功，正在下载订阅节点...");

            bool subscriptionLoaded = await RefreshSubscriptionAsync();
            _loginView.SetBusy(
                false,
                subscriptionLoaded
                    ? "登录成功，订阅节点已加载。"
                    : "登录成功，但订阅节点读取失败，可到节点页重试。");

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

        if (_delayTestBusy)
        {
            _homeView.ShowConnectionError("节点测速正在进行，请等待测速完成。");
            return;
        }

        _connectionBusy = true;

        try
        {
            if (_mihomoService.IsRunning)
            {
                _homeView.SetConnectionBusy(true, "正在断开...");
                await _mihomoService.StopAsync();
                _homeView.ShowConnectionState(connected: false, _selectedNode?.DisplayName);
                _nodesView.ShowStatus("Mihomo 已停止。Windows 系统代理未被修改。");
                return;
            }

            if (_userSession is null)
            {
                _homeView.ShowConnectionError("请先登录账号。");
                Navigate("Login");
                return;
            }

            if (string.IsNullOrWhiteSpace(_subscriptionYaml))
            {
                _homeView.ShowConnectionError("尚未加载订阅，请先到节点页刷新订阅。");
                return;
            }

            if (_selectedNode is null)
            {
                _homeView.ShowConnectionError("订阅中没有可连接的节点。");
                return;
            }

            _homeView.SetConnectionBusy(true, "正在启动 Mihomo...");
            await _mihomoService.StartAsync(_subscriptionYaml);

            _homeView.SetConnectionBusy(true, "正在切换节点...");
            await _mihomoService.SelectNodeAsync(_selectedNode);

            _homeView.ShowConnectionState(connected: true, _selectedNode.DisplayName);
            _nodesView.ShowStatus(
                $"已连接：{_selectedNode.DisplayName}。当前仅启动本地代理 127.0.0.1:7890。");
        }
        catch (Exception ex)
        {
            try
            {
                if (_mihomoService.IsRunning)
                {
                    await _mihomoService.StopAsync();
                }
            }
            catch
            {
                // 保留原始连接错误。
            }

            _homeView.ShowConnectionError(
                "连接失败：" + ex.Message,
                _selectedNode?.DisplayName);
        }
        finally
        {
            _connectionBusy = false;
        }
    }

    private async void NodesView_RefreshRequested(object? sender, EventArgs e)
    {
        if (_delayTestBusy)
        {
            _nodesView.ShowStatus("节点测速正在进行，请等待测速完成。");
            return;
        }

        if (_mihomoService.IsRunning)
        {
            _nodesView.ShowStatus("请先断开连接，再刷新订阅。");
            return;
        }

        await RefreshSubscriptionAsync();
    }

    private async void NodesView_DelayTestRequested(object? sender, EventArgs e)
    {
        if (_delayTestBusy || _connectionBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_subscriptionYaml))
        {
            _nodesView.ShowStatus("尚未加载订阅，无法测速。");
            return;
        }

        IReadOnlyList<ProxyNode> nodes = _nodesView.GetNodesSnapshot();
        if (nodes.Count == 0)
        {
            _nodesView.ShowStatus("当前没有可测速的节点。");
            return;
        }

        _delayTestBusy = true;
        bool startedForTest = !_mihomoService.IsRunning;
        int successCount = 0;
        int completedCount = 0;

        foreach (ProxyNode node in nodes)
        {
            node.BeginDelayTest();
        }

        _nodesView.SetBusy(true, $"准备测速，共 {nodes.Count} 个节点...");

        try
        {
            if (startedForTest)
            {
                await _mihomoService.StartAsync(_subscriptionYaml);
            }

            using SemaphoreSlim concurrency = new(DelayTestConcurrency, DelayTestConcurrency);

            Task[] tests = nodes.Select(async node =>
            {
                await concurrency.WaitAsync();
                try
                {
                    int? delay = await _mihomoService.TestDelayAsync(
                        node,
                        DelayTestUrl,
                        DelayTestTimeoutMilliseconds);

                    node.CompleteDelayTest(delay);
                    if (delay is > 0)
                    {
                        Interlocked.Increment(ref successCount);
                    }
                }
                catch
                {
                    node.CompleteDelayTest(null);
                }
                finally
                {
                    concurrency.Release();
                    int completed = Interlocked.Increment(ref completedCount);
                    int available = Volatile.Read(ref successCount);
                    await Dispatcher.InvokeAsync(() =>
                        _nodesView.ShowDelayProgress(completed, nodes.Count, available));
                }
            }).ToArray();

            await Task.WhenAll(tests);

            _nodesView.SetBusy(
                false,
                $"测速完成：{successCount}/{nodes.Count} 个节点可用，超时时间 {DelayTestTimeoutMilliseconds / 1000} 秒。");
        }
        catch (Exception ex)
        {
            foreach (ProxyNode node in nodes.Where(node => node.DelayState == DelayTestState.Testing))
            {
                node.CompleteDelayTest(null);
            }

            _nodesView.SetBusy(false, "测速失败：" + ex.Message);
        }
        finally
        {
            if (startedForTest && _mihomoService.IsRunning)
            {
                try
                {
                    await _mihomoService.StopAsync();
                }
                catch (Exception ex)
                {
                    _nodesView.ShowStatus("测速已结束，但临时 Mihomo 停止失败：" + ex.Message);
                }
            }

            _delayTestBusy = false;
        }
    }

    private async void NodesView_NodeSelectionRequested(object? sender, ProxyNode node)
    {
        if (_delayTestBusy)
        {
            _nodesView.ShowStatus("节点测速正在进行，请等待测速完成后再切换。");
            return;
        }

        if (!_mihomoService.IsRunning)
        {
            _selectedNode = node;
            _homeView.ShowConnectionState(connected: false, node.DisplayName);
            _nodesView.ShowStatus($"已选择：{node.DisplayName}。点击首页连接后生效。");
            return;
        }

        if (_connectionBusy)
        {
            return;
        }

        ProxyNode? previousNode = _selectedNode;
        _connectionBusy = true;
        _nodesView.SetBusy(true, $"正在切换到：{node.DisplayName}...");

        try
        {
            await _mihomoService.SelectNodeAsync(node);
            _selectedNode = node;
            _homeView.ShowConnectionState(connected: true, node.DisplayName);
            _nodesView.SetBusy(false, $"已切换到：{node.DisplayName}。");
        }
        catch (Exception ex)
        {
            _selectedNode = previousNode;
            _homeView.ShowConnectionState(connected: true, previousNode?.DisplayName);
            _nodesView.SetBusy(false, "切换失败：" + ex.Message);
        }
        finally
        {
            _connectionBusy = false;
        }
    }

    private async Task<bool> RefreshSubscriptionAsync()
    {
        if (_subscriptionBusy)
        {
            return false;
        }

        if (_appConfig is null || _userSession is null)
        {
            _nodesView.ShowStatus("请先登录账号，再下载订阅节点。");
            return false;
        }

        _subscriptionBusy = true;
        _nodesView.SetBusy(true, "正在下载并解析订阅节点...");

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
                _selectedNode = result.Nodes.FirstOrDefault();
            }

            _homeView.ShowConnectionState(
                connected: false,
                _selectedNode?.DisplayName);

            return true;
        }
        catch (Exception ex)
        {
            _nodesView.SetBusy(false, "订阅读取失败：" + ex.Message);
            return false;
        }
        finally
        {
            _subscriptionBusy = false;
        }
    }

    private void MihomoService_Exited(object? sender, MihomoExitedEventArgs e)
    {
        if (e.Expected)
        {
            return;
        }

        Dispatcher.Invoke(() =>
        {
            _connectionBusy = false;
            _homeView.ShowConnectionError(
                $"Mihomo 意外退出，退出代码：{e.ExitCode?.ToString() ?? "未知"}。",
                _selectedNode?.DisplayName);
            _nodesView.ShowStatus($"Mihomo 意外退出。日志：{_mihomoService.LogPath}");
        });
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
        _mihomoService.Exited -= MihomoService_Exited;
        _mihomoService.Dispose();
        _v2BoardApi.Dispose();
        _configService.Dispose();
    }
}
