using KuaiyunClient.Models;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace KuaiyunClient.Views;

public partial class NodesView : UserControl
{
    private readonly ObservableCollection<ProxyNode> _nodes = [];

    public event EventHandler? RefreshRequested;

    public event EventHandler<ProxyNode>? NodeSelectionRequested;

    public NodesView()
    {
        InitializeComponent();
        NodesList.ItemsSource = _nodes;
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
        SummaryText.Text = $"{_nodes.Count} 个节点";
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    private void NodesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NodesList.SelectedItem is ProxyNode node)
        {
            NodeSelectionRequested?.Invoke(this, node);
        }
    }
}
