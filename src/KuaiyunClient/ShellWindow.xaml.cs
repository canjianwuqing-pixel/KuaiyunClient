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

    private static readonly Brush ActiveBrush = new SolidColorBrush(Color.FromRgb(32, 58, 87));
    private static readonly Brush InactiveBrush = Brushes.Transparent;
    private static readonly Brush ActiveTextBrush = Brushes.White;
    private static readonly Brush InactiveTextBrush = new SolidColorBrush(Color.FromRgb(175, 192, 211));

    public ShellWindow()
    {
        InitializeComponent();
        Navigate("Login");
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
}
