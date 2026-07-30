using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace KuaiyunClient.Views;

public partial class LoginView : UserControl
{
    private readonly DispatcherTimer _countdownTimer;
    private Button? _countdownButton;
    private int _countdownSeconds;
    private bool _busy;

    public event EventHandler<LoginRequestedEventArgs>? LoginRequested;
    public event EventHandler<VerificationCodeRequestedEventArgs>? VerificationCodeRequested;
    public event EventHandler<RegisterRequestedEventArgs>? RegisterRequested;
    public event EventHandler<PasswordResetRequestedEventArgs>? PasswordResetRequested;

    public LoginView()
    {
        InitializeComponent();
        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += CountdownTimer_Tick;
    }

    public void SetLoginPreferences(string? email, bool rememberAccount, bool autoLogin)
    {
        EmailInput.Text = email ?? string.Empty;
        RememberAccountCheckBox.IsChecked = rememberAccount || autoLogin;
        AutoLoginCheckBox.IsChecked = autoLogin;
    }

    public void SetBusy(bool busy, string? message = null)
    {
        _busy = busy;
        LoginModeButton.IsEnabled = !busy;
        RegisterModeButton.IsEnabled = !busy;
        LoginPanel.IsEnabled = !busy;
        RegisterPanel.IsEnabled = !busy;
        ForgotPanel.IsEnabled = !busy;

        if (_countdownButton is not null && _countdownSeconds > 0)
        {
            _countdownButton.IsEnabled = false;
        }

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

    public void ShowLoginMode(string? email = null, string? status = null)
    {
        if (!string.IsNullOrWhiteSpace(email))
        {
            EmailInput.Text = email.Trim();
        }

        SetMode(LoginPageMode.Login);
        StatusText.Text = status ?? string.Empty;
        PasswordInput.Focus();
    }

    public void StartVerificationCountdown(VerificationPurpose purpose)
    {
        _countdownButton = purpose == VerificationPurpose.PasswordReset
            ? ResetCodeButton
            : RegisterCodeButton;
        _countdownSeconds = 60;
        UpdateCountdownButton();
        _countdownTimer.Start();
    }

    private void ModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || sender is not Button { Tag: string tag })
        {
            return;
        }

        SetMode(string.Equals(tag, "Register", StringComparison.OrdinalIgnoreCase)
            ? LoginPageMode.Register
            : LoginPageMode.Login);
    }

    private void SetMode(LoginPageMode mode)
    {
        LoginPanel.Visibility = mode == LoginPageMode.Login ? Visibility.Visible : Visibility.Collapsed;
        RegisterPanel.Visibility = mode == LoginPageMode.Register ? Visibility.Visible : Visibility.Collapsed;
        ForgotPanel.Visibility = mode == LoginPageMode.ForgotPassword ? Visibility.Visible : Visibility.Collapsed;

        bool normalTabs = mode != LoginPageMode.ForgotPassword;
        LoginModeButton.Visibility = normalTabs ? Visibility.Visible : Visibility.Collapsed;
        RegisterModeButton.Visibility = normalTabs ? Visibility.Visible : Visibility.Collapsed;

        LoginModeButton.Background = mode == LoginPageMode.Login
            ? (Brush)FindResource("AccentBrush")
            : new SolidColorBrush(Color.FromRgb(238, 242, 248));
        LoginModeButton.Foreground = mode == LoginPageMode.Login
            ? Brushes.White
            : new SolidColorBrush(Color.FromRgb(83, 97, 118));
        RegisterModeButton.Background = mode == LoginPageMode.Register
            ? (Brush)FindResource("AccentBrush")
            : new SolidColorBrush(Color.FromRgb(238, 242, 248));
        RegisterModeButton.Foreground = mode == LoginPageMode.Register
            ? Brushes.White
            : new SolidColorBrush(Color.FromRgb(83, 97, 118));

        PageHintText.Text = mode switch
        {
            LoginPageMode.Register => "创建账号后即可使用",
            LoginPageMode.ForgotPassword => "通过邮箱验证码重置密码",
            _ => "登录后开始使用"
        };
        StatusText.Text = string.Empty;
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

        bool autoLogin = AutoLoginCheckBox.IsChecked == true;
        bool rememberAccount = RememberAccountCheckBox.IsChecked == true || autoLogin;
        StatusText.Text = string.Empty;
        LoginRequested?.Invoke(
            this,
            new LoginRequestedEventArgs(email, password, rememberAccount, autoLogin));
    }

    private void RegisterButton_Click(object sender, RoutedEventArgs e)
    {
        string email = RegisterEmailInput.Text.Trim();
        string emailCode = RegisterCodeInput.Text.Trim();
        string password = RegisterPasswordInput.Password;
        string confirmPassword = RegisterConfirmPasswordInput.Password;
        string inviteCode = InviteCodeInput.Text.Trim();

        if (string.IsNullOrWhiteSpace(email)
            || string.IsNullOrWhiteSpace(emailCode)
            || string.IsNullOrWhiteSpace(password)
            || string.IsNullOrWhiteSpace(confirmPassword))
        {
            StatusText.Text = "请填写邮箱、验证码和密码。";
            return;
        }

        if (!string.Equals(password, confirmPassword, StringComparison.Ordinal))
        {
            StatusText.Text = "两次输入的密码不一致。";
            return;
        }

        RegisterRequested?.Invoke(
            this,
            new RegisterRequestedEventArgs(
                email,
                emailCode,
                password,
                confirmPassword,
                string.IsNullOrWhiteSpace(inviteCode) ? null : inviteCode));
    }

    private void VerificationButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || sender is not Button { Tag: string tag })
        {
            return;
        }

        VerificationPurpose purpose = string.Equals(tag, "Reset", StringComparison.OrdinalIgnoreCase)
            ? VerificationPurpose.PasswordReset
            : VerificationPurpose.Register;
        string email = purpose == VerificationPurpose.PasswordReset
            ? ResetEmailInput.Text.Trim()
            : RegisterEmailInput.Text.Trim();

        if (string.IsNullOrWhiteSpace(email))
        {
            StatusText.Text = "请先填写邮箱账号。";
            return;
        }

        VerificationCodeRequested?.Invoke(
            this,
            new VerificationCodeRequestedEventArgs(email, purpose));
    }

    private void ForgotPasswordButton_Click(object sender, RoutedEventArgs e)
    {
        ResetEmailInput.Text = EmailInput.Text.Trim();
        SetMode(LoginPageMode.ForgotPassword);
    }

    private void BackToLoginButton_Click(object sender, RoutedEventArgs e)
    {
        ShowLoginMode(ResetEmailInput.Text.Trim());
    }

    private void ResetPasswordButton_Click(object sender, RoutedEventArgs e)
    {
        string email = ResetEmailInput.Text.Trim();
        string emailCode = ResetCodeInput.Text.Trim();
        string password = ResetPasswordInput.Password;
        string confirmPassword = ResetConfirmPasswordInput.Password;

        if (string.IsNullOrWhiteSpace(email)
            || string.IsNullOrWhiteSpace(emailCode)
            || string.IsNullOrWhiteSpace(password)
            || string.IsNullOrWhiteSpace(confirmPassword))
        {
            StatusText.Text = "请填写邮箱、验证码和新密码。";
            return;
        }

        if (!string.Equals(password, confirmPassword, StringComparison.Ordinal))
        {
            StatusText.Text = "两次输入的新密码不一致。";
            return;
        }

        PasswordResetRequested?.Invoke(
            this,
            new PasswordResetRequestedEventArgs(email, emailCode, password, confirmPassword));
    }

    private void AutoLoginCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        RememberAccountCheckBox.IsChecked = true;
    }

    private void CountdownTimer_Tick(object? sender, EventArgs e)
    {
        _countdownSeconds--;
        if (_countdownSeconds <= 0)
        {
            _countdownTimer.Stop();
            if (_countdownButton is not null)
            {
                _countdownButton.Content = "获取验证码";
                _countdownButton.IsEnabled = !_busy;
            }

            _countdownButton = null;
            return;
        }

        UpdateCountdownButton();
    }

    private void UpdateCountdownButton()
    {
        if (_countdownButton is null)
        {
            return;
        }

        _countdownButton.Content = $"{_countdownSeconds} 秒";
        _countdownButton.IsEnabled = false;
    }

    private enum LoginPageMode
    {
        Login,
        Register,
        ForgotPassword
    }
}

public enum VerificationPurpose
{
    Register,
    PasswordReset
}

public sealed class LoginRequestedEventArgs(
    string email,
    string password,
    bool rememberAccount,
    bool autoLogin) : EventArgs
{
    public string Email { get; } = email;
    public string Password { get; } = password;
    public bool RememberAccount { get; } = rememberAccount;
    public bool AutoLogin { get; } = autoLogin;
}

public sealed class VerificationCodeRequestedEventArgs(
    string email,
    VerificationPurpose purpose) : EventArgs
{
    public string Email { get; } = email;
    public VerificationPurpose Purpose { get; } = purpose;
}

public sealed class RegisterRequestedEventArgs(
    string email,
    string emailCode,
    string password,
    string confirmPassword,
    string? inviteCode) : EventArgs
{
    public string Email { get; } = email;
    public string EmailCode { get; } = emailCode;
    public string Password { get; } = password;
    public string ConfirmPassword { get; } = confirmPassword;
    public string? InviteCode { get; } = inviteCode;
}

public sealed class PasswordResetRequestedEventArgs(
    string email,
    string emailCode,
    string password,
    string confirmPassword) : EventArgs
{
    public string Email { get; } = email;
    public string EmailCode { get; } = emailCode;
    public string Password { get; } = password;
    public string ConfirmPassword { get; } = confirmPassword;
}
