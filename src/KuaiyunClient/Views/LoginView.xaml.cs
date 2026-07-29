using System.Windows;
using System.Windows.Controls;

namespace KuaiyunClient.Views;

public partial class LoginView : UserControl
{
    public event EventHandler<LoginRequestedEventArgs>? LoginRequested;

    public LoginView()
    {
        InitializeComponent();
    }

    public void SetBusy(bool busy, string? message = null)
    {
        LoginButton.IsEnabled = !busy;
        EmailInput.IsEnabled = !busy;
        PasswordInput.IsEnabled = !busy;
        StatusText.Text = message ?? string.Empty;
    }

    public void ClearPassword()
    {
        PasswordInput.Clear();
    }

    public void ShowStatus(string message)
    {
        StatusText.Text = message;
    }

    private void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        string email = EmailInput.Text.Trim();
        string password = PasswordInput.Password;

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            StatusText.Text = "请填写邮箱账号和密码。";
            return;
        }

        StatusText.Text = string.Empty;
        LoginRequested?.Invoke(this, new LoginRequestedEventArgs(email, password));
    }
}

public sealed class LoginRequestedEventArgs(string email, string password) : EventArgs
{
    public string Email { get; } = email;

    public string Password { get; } = password;
}
