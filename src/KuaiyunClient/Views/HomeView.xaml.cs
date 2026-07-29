using KuaiyunClient.Models;
using System.Windows;
using System.Windows.Controls;

namespace KuaiyunClient.Views;

public partial class HomeView : UserControl
{
    public event EventHandler? ConnectionToggleRequested;

    public HomeView()
    {
        InitializeComponent();
    }

    public void ShowSession(UserSession session)
    {
        double used = session.UploadBytes + session.DownloadBytes;
        double remaining = Math.Max(0, session.TransferEnableBytes - used);

        EmailText.Text = session.Email;
        TrafficText.Text = session.TransferEnableBytes > 0
            ? $"{ToGigabytes(remaining):F1} GB"
            : "--";
        ExpiryText.Text = session.ExpiredAt > 0
            ? DateTimeOffset.FromUnixTimeSeconds(session.ExpiredAt).LocalDateTime.ToString("yyyy-MM-dd")
            : "--";
    }

    public void SetConnectionBusy(bool busy, string message)
    {
        ConnectionButton.IsEnabled = !busy;
        ConnectionStateText.Text = message;
        ConnectionButton.Content = busy ? "处理中" : "连接";
    }

    public void ShowConnectionState(bool connected, string? nodeName = null)
    {
        ConnectionButton.IsEnabled = true;
        ConnectionStateText.Text = connected ? "已连接" : "未连接";
        CurrentNodeText.Text = $"当前节点：{(string.IsNullOrWhiteSpace(nodeName) ? "--" : nodeName)}";
        ConnectionButton.Content = connected ? "断开" : "连接";
        ConnectionInfoText.Text = connected
            ? "Mihomo 已运行。当前尚未开启 Windows 系统代理。"
            : "当前阶段只启动 Mihomo，不会修改 Windows 系统代理。";
    }

    public void ShowConnectionError(string message, string? nodeName = null)
    {
        ShowConnectionState(connected: false, nodeName);
        ConnectionInfoText.Text = message;
    }

    private void ConnectionButton_Click(object sender, RoutedEventArgs e)
    {
        ConnectionToggleRequested?.Invoke(this, EventArgs.Empty);
    }

    private static double ToGigabytes(double bytes) => bytes / 1024d / 1024d / 1024d;
}
