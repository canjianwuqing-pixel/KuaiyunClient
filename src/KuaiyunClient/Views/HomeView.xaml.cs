using KuaiyunClient.Models;
using KuaiyunClient.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KuaiyunClient.Views;

public partial class HomeView : UserControl
{
    private readonly V2BoardCommerceApi _commerceApi = new();
    private ProxyNode? _currentNode;
    private UserSession? _session;
    private IReadOnlyList<V2BoardNotice> _notices = [];
    private int _accountLoadVersion;

    public event EventHandler? ConnectionToggleRequested;

    public event EventHandler? ServerSelectionRequested;

    public event EventHandler? AnnouncementRequested;

    public HomeView()
    {
        InitializeComponent();
    }

    public void ShowSession(UserSession session)
    {
        _session = session;
        RenderSession(session);
        int version = ++_accountLoadVersion;
        _ = RefreshAccountAsync(session, version);
    }

    public void SetAnnouncement(string? announcement)
    {
        AnnouncementText.Text = string.IsNullOrWhiteSpace(announcement)
            ? "暂无公告"
            : announcement.Trim();
    }

    public void SetCurrentNode(ProxyNode? node)
    {
        _currentNode = node;
        string name = node?.DisplayName ?? "暂无可用线路";
        CurrentNodeText.Text = name;
        ServerNameText.Text = name;

        if (node is null || !File.Exists(node.FlagImagePath))
        {
            CurrentFlagImage.Source = null;
            return;
        }

        CurrentFlagImage.Source = new BitmapImage(new Uri(node.FlagImagePath, UriKind.Absolute));
    }

    public void ShowConnectionState(ConnectionUiState state, ProxyNode? node = null)
    {
        if (node is not null || _currentNode is null)
        {
            SetCurrentNode(node);
        }

        ConnectionStateText.Text = state switch
        {
            ConnectionUiState.Connecting => "连接中",
            ConnectionUiState.Connected => "已连接",
            _ => "未连接"
        };

        ConnectionButton.IsEnabled = state != ConnectionUiState.Connecting && _currentNode is not null;
        ConnectionButton.Background = state == ConnectionUiState.Connected
            ? (Brush)FindResource("AccentBrush")
            : Brushes.White;
    }

    public void ShowConnectionError(string message, ProxyNode? node = null)
    {
        ShowConnectionState(ConnectionUiState.Disconnected, node);
        MessageBox.Show(
            message,
            "连接提示",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private async Task RefreshAccountAsync(UserSession session, int version)
    {
        AppConfig? config = BuildApiConfig(session);
        if (config is null)
        {
            return;
        }

        try
        {
            await _commerceApi.RefreshSessionAsync(config, session);
            IReadOnlyList<V2BoardPlan> plans = await _commerceApi.GetPlansAsync(config, session);
            V2BoardPlan? currentPlan = session.PlanId is int planId
                ? plans.FirstOrDefault(plan => plan.Id == planId)
                : null;
            session.PlanName = currentPlan?.Name ?? "未订阅套餐";

            try
            {
                _notices = await _commerceApi.GetNoticesAsync(config, session);
            }
            catch
            {
                _notices = [];
            }

            if (version != _accountLoadVersion || !ReferenceEquals(session, _session))
            {
                return;
            }

            await Dispatcher.InvokeAsync(() =>
            {
                RenderSession(session);
                SetAnnouncement(_notices.FirstOrDefault()?.Title);
            });
        }
        catch
        {
            // 账户扩展信息读取失败不影响订阅连接。
        }
    }

    private void RenderSession(UserSession session)
    {
        double used = session.UploadBytes + session.DownloadBytes;
        double remaining = Math.Max(0, session.TransferEnableBytes - used);

        TrafficText.Text = session.TransferEnableBytes > 0
            ? $"{ToGigabytes(remaining):F1} GB"
            : "--";
        TotalTrafficText.Text = session.TransferEnableBytes > 0
            ? $"总流量 {ToGigabytes(session.TransferEnableBytes):F0} GB"
            : "总流量 --";
        ExpiryText.Text = session.ExpiredAt > 0
            ? "到期时间 " + DateTimeOffset
                .FromUnixTimeSeconds(session.ExpiredAt)
                .LocalDateTime
                .ToString("yyyy-MM-dd")
            : "到期时间 --";
        PlanText.Text = string.IsNullOrWhiteSpace(session.PlanName)
            ? "未订阅套餐"
            : session.PlanName;
        ResetText.Text = BuildResetText(session.ResetDay);
    }

    private void ConnectionButton_Click(object sender, RoutedEventArgs e)
    {
        ConnectionToggleRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ServerButton_Click(object sender, RoutedEventArgs e)
    {
        ServerSelectionRequested?.Invoke(this, EventArgs.Empty);
    }

    private void AnnouncementButton_Click(object sender, RoutedEventArgs e)
    {
        V2BoardNotice? notice = _notices.FirstOrDefault();
        if (notice is null)
        {
            AnnouncementRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        MessageBox.Show(
            string.IsNullOrWhiteSpace(notice.PlainContent)
                ? "暂无详细内容。"
                : notice.PlainContent,
            notice.Title,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private static AppConfig? BuildApiConfig(UserSession session)
    {
        if (!Uri.TryCreate(session.SubscriptionUrl, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        string host = uri.GetLeftPart(UriPartial.Authority);
        session.ApiHost = host;
        return new AppConfig
        {
            UserAgent = "kuaiyun",
            RemoteHosts = [host]
        };
    }

    private static string BuildResetText(int? resetDay)
    {
        if (resetDay is not > 0)
        {
            return "流量重置 --";
        }

        DateTime today = DateTime.Today;
        DateTime candidate = CreateSafeDate(today.Year, today.Month, resetDay.Value);
        if (candidate < today)
        {
            DateTime nextMonth = today.AddMonths(1);
            candidate = CreateSafeDate(nextMonth.Year, nextMonth.Month, resetDay.Value);
        }

        int days = Math.Max(0, (candidate - today).Days);
        return $"下次重置 {candidate:MM-dd}（{days} 天）";
    }

    private static DateTime CreateSafeDate(int year, int month, int day)
    {
        return new DateTime(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));
    }

    private static double ToGigabytes(double bytes) => bytes / 1024d / 1024d / 1024d;
}

public enum ConnectionUiState
{
    Disconnected,
    Connecting,
    Connected
}
