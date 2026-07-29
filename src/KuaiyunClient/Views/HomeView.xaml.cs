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

    public void ShowConnectionState(bool connected, string? nodeName = null)
    {
        ConnectionStateText.Text = connected ? "已连接" : "未连接";
        CurrentNodeText.Text = $"当前节点：{(string.IsNullOrWhiteSpace(nodeName) ? "--" : nodeName)}";
        ConnectionButton.Content = connected ? "断开" : "连接";
    }

    private void ConnectionButton_Click(object sender, RoutedEventArgs e)
    {
        ConnectionToggleRequested?.Invoke(this, EventArgs.Empty);
    }

    private static double ToGigabytes(double bytes) => bytes / 1024d / 1024d / 1024d;
}
