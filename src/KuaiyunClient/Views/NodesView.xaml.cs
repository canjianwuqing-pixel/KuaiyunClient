using KuaiyunClient.Models;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace KuaiyunClient.Views;

public partial class NodesView : UserControl
{
    private readonly ObservableCollection<ProxyNode> _nodes = [];

    public event EventHandler? BackRequested;

    public event EventHandler<ProxyNode>? NodeSelectionRequested;

    public NodesView()
    {
        InitializeComponent();
        NodesList.ItemsSource = _nodes;
    }

    public IReadOnlyList<ProxyNode> GetNodesSnapshot() => _nodes.ToArray();

    public void SetBusy(bool busy, string? message = null)
    {
        NodesList.IsEnabled = !busy;
        if (string.IsNullOrWhiteSpace(message))
        {
            StatusText.Visibility = Visibility.Collapsed;
            StatusText.Text = string.Empty;
        }
        else
        {
            StatusText.Text = message;
            StatusText.Visibility = Visibility.Visible;
        }
    }

    public void ShowStatus(string? message)
    {
        SetBusy(false, message);
    }

    public void ShowDelayProgress(int completed, int total, int successCount)
    {
        SetBusy(true, $"正在检测线路 {completed}/{total}");
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
        SetBusy(false);
    }

    public void SetSelectedNode(ProxyNode? node)
    {
        if (node is null)
        {
            NodesList.SelectedItem = null;
            return;
        }

        ProxyNode? match = _nodes.FirstOrDefault(item =>
            string.Equals(item.Name, node.Name, StringComparison.Ordinal));
        NodesList.SelectedItem = match;
        if (match is not null)
        {
            NodesList.ScrollIntoView(match);
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void NodesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NodesList.SelectedItem is ProxyNode node)
        {
            NodeSelectionRequested?.Invoke(this, node);
        }
    }
}
