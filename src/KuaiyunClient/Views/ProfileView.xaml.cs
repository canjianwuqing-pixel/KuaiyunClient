using KuaiyunClient.Models;
using KuaiyunClient.Services;
using System.Windows;
using System.Windows.Controls;

namespace KuaiyunClient.Views;

public partial class ProfileView : UserControl
{
    private readonly ClientSettingsService _settingsService = new();
    private readonly LoginCredentialService _credentialService = new();

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

    private async void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag }
            || !Enum.TryParse(tag, ignoreCase: true, out ProfileAction action))
        {
            return;
        }

        if (action == ProfileAction.Logout)
        {
            try
            {
                ClientSettings settings = await _settingsService.LoadAsync();
                await _settingsService.SaveAsync(settings with { AutoLogin = false });
                await _credentialService.DeleteAsync();
            }
            catch
            {
                // 退出账号仍由主窗口处理；清理自动登录失败不应阻止退出。
            }
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
