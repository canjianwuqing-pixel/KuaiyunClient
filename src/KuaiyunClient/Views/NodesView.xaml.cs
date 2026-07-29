using KuaiyunClient.Models;
using KuaiyunClient.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace KuaiyunClient.Views;

public partial class NodesView : UserControl
{
    private readonly ObservableCollection<ProxyNode> _nodes = [];

    public event EventHandler? RefreshRequested;

    public event EventHandler? DelayTestRequested;

    public event EventHandler<ProxyNode>? NodeSelectionRequested;

    public NodesView()
    {
        InitializeComponent();
        NodesList.ItemsSource = _nodes;
    }

    public IReadOnlyList<ProxyNode> GetNodesSnapshot()
    {
        return _nodes.ToArray();
    }

    public void SetBusy(bool busy, string message)
    {
        RefreshButton.IsEnabled = !busy;
        DelayTestButton.IsEnabled = !busy && _nodes.Count > 0;
        NodesList.IsEnabled = !busy;
        StatusText.Text = message;
    }

    public void ShowStatus(string message)
    {
        StatusText.Text = message;
    }

    public void ShowDelayProgress(int completed, int total, int successCount)
    {
        StatusText.Text = $"正在测速：{completed}/{total}，可用 {successCount} 个。";
    }

    public void SetNodes(IEnumerable<ProxyNode> nodes)
    {
        _nodes.Clear();
        foreach (ProxyNode node in nodes)
        {
            _nodes.Add(node);
        }

        bool hasNodes = _nodes.Count > 0;
        EmptyText.Visibility = hasNodes ? Visibility.Collapsed : Visibility.Visible;
        NodesList.Visibility = hasNodes ? Visibility.Visible : Visibility.Collapsed;

        int countryCount = _nodes
            .Select(node => string.IsNullOrWhiteSpace(node.CountryCode)
                ? node.CountryName
                : node.CountryCode)
            .Where(value => !string.IsNullOrWhiteSpace(value)
                && !string.Equals(value, "其他地区", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        SummaryText.Text = countryCount > 0
            ? $"{_nodes.Count} 个节点 · 实际 {countryCount} 个国家/地区 · 图标库 {CountryFlagResolver.SupportedRegionCount}"
            : $"{_nodes.Count} 个节点 · 图标库 {CountryFlagResolver.SupportedRegionCount}";

        StatusText.Text = hasNodes
            ? "订阅已加载。点击“全部测速”获取真实延迟。"
            : "订阅中没有可显示的节点。";

        RefreshButton.IsEnabled = true;
        DelayTestButton.IsEnabled = hasNodes;
        NodesList.IsEnabled = true;
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    private void DelayTestButton_Click(object sender, RoutedEventArgs e)
    {
        DelayTestRequested?.Invoke(this, EventArgs.Empty);
    }

    private void NodesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NodesList.SelectedItem is ProxyNode node)
        {
            NodeSelectionRequested?.Invoke(this, node);
        }
    }
}
