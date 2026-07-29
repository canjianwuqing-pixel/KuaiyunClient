using KuaiyunClient.Models;
using System.Windows;
using System.Windows.Controls;

namespace KuaiyunClient.Views;

public partial class ProfileView : UserControl
{
    public event EventHandler<ProfileAction>? ActionRequested;

    public ProfileView()
    {
        InitializeComponent();
    }

    public void ShowSession(UserSession session)
    {
        EmailText.Text = session.Email;
        ExpiryText.Text = session.ExpiredAt > 0
            ? "到期时间 " + DateTimeOffset
                .FromUnixTimeSeconds(session.ExpiredAt)
                .LocalDateTime
                .ToString("yyyy-MM-dd")
            : "到期时间 --";
    }

    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag }
            || !Enum.TryParse(tag, ignoreCase: true, out ProfileAction action))
        {
            return;
        }

        ActionRequested?.Invoke(this, action);
    }
}

public enum ProfileAction
{
    Orders,
    Invite,
    Website,
    Announcement,
    Support,
    Telegram,
    Password,
    Logs,
    Version,
    Logout
}
