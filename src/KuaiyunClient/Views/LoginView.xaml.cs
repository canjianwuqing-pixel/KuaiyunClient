using KuaiyunClient.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace KuaiyunClient.Views;

public partial class LoginView : UserControl
{
    private readonly DispatcherTimer _countdownTimer;
    private readonly ConfigService _configService = new();
    private readonly V2BoardAccountService _accountService = new();
    private readonly ClientSettingsService _settingsService = new();
    private readonly LoginCredentialService _credentialService = new();

    private Button? _countdownButton;
    private LoginRequestedEventArgs? _pendingLogin;
    private int _countdownSeconds;
    private bool _busy;
    private bool _autoLoginAttempted;

    public event EventHandler<LoginRequestedEventArgs>? LoginRequested;

    public LoginView()
    {
        InitializeComponent();
        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += CountdownTimer_Tick;
        Loaded += LoginView_Loaded;
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

        if (!busy && _pendingLogin is not null)
        {
            LoginRequestedEventArgs completedRequest = _pendingLogin;
            _pendingLogin = null;

            if (!string.IsNullOrWhiteSpace(message)
                && message.StartsWith("登录成功", StringComparison.Ordinal))
            {
                _ = PersistSuccessfulLoginAsync(completedRequest);
            }
        }
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

    private async void LoginView_Loaded(object sender, RoutedEventArgs e)
    {
        if (_autoLoginAttempted)
        {
            return;
        }

        _autoLoginAttempted = true;
        ClientSettings settings = await _settingsService.LoadAsync();
        SetLoginPreferences(
            settings.SavedEmail,
            settings.RememberAccount,
            settings.AutoLogin);

        if (!settings.AutoLogin)
        {
            return;
        }

        SavedLoginCredential? credential = await _credentialService.LoadAsync();
        if (credential is null)
        {
            await DisableAutoLoginAsync(settings);
            StatusText.Text = "自动登录凭据已失效，请重新输入密码。";
            return;
        }

        try
        {
            // 先等待云端配置可用，再把登录请求交给主窗口，避免启动阶段抢跑。
            await _configService.LoadAsync();
            await Task.Delay(400);

            for (int attempt = 0; attempt < 3 && IsVisible; attempt++)
            {
                SubmitLogin(new LoginRequestedEventArgs(
                    credential.Email,
                    credential.Password,
                    rememberAccount: true,
                    autoLogin: true));

                await Task.Delay(1500);
                if (!IsVisible
                    || !StatusText.Text.Contains("服务配置尚未就绪", StringComparison.Ordinal))
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "自动登录暂不可用：" + ex.Message;
        }
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
        SubmitLogin(new LoginRequestedEventArgs(
            email,
            password,
            rememberAccount,
            autoLogin));
    }

    private async void RegisterButton_Click(object sender, RoutedEventArgs e)
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

        SetBusy(true, "正在注册账号...");
        try
        {
            AppConfig config = (await _configService.LoadAsync()).Config;
            await _accountService.RegisterAsync(
                config,
                email,
                emailCode,
                password,
                string.IsNullOrWhiteSpace(inviteCode) ? null : inviteCode);

            SetBusy(false, "注册成功，正在登录...");
            ShowLoginMode(email, "注册成功，正在登录...");
            SubmitLogin(new LoginRequestedEventArgs(
                email,
                password,
                rememberAccount: true,
                autoLogin: false));
        }
        catch (Exception ex)
        {
            SetBusy(false, "注册失败：" + ex.Message);
        }
    }

    private async void VerificationButton_Click(object sender, RoutedEventArgs e)
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

        SetBusy(true, "正在发送验证码...");
        try
        {
            AppConfig config = (await _configService.LoadAsync()).Config;
            await _accountService.SendEmailVerificationAsync(
                config,
                email,
                purpose == VerificationPurpose.PasswordReset);
            SetBusy(false, "验证码已发送，请检查邮箱。");
            StartVerificationCountdown(purpose);
        }
        catch (Exception ex)
        {
            SetBusy(false, ex.Message);
        }
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

    private async void ResetPasswordButton_Click(object sender, RoutedEventArgs e)
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

        SetBusy(true, "正在重置密码...");
        try
        {
            AppConfig config = (await _configService.LoadAsync()).Config;
            await _accountService.ResetPasswordAsync(
                config,
                email,
                emailCode,
                password);

            ClientSettings settings = await _settingsService.LoadAsync();
            await DisableAutoLoginAsync(settings);
            SetBusy(false);
            ShowLoginMode(email, "密码已重置，请使用新密码登录。");
        }
        catch (Exception ex)
        {
            SetBusy(false, "重置失败：" + ex.Message);
        }
    }

    private void AutoLoginCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        RememberAccountCheckBox.IsChecked = true;
    }

    private void SubmitLogin(LoginRequestedEventArgs request)
    {
        _pendingLogin = request;
        StatusText.Text = string.Empty;
        LoginRequested?.Invoke(this, request);
    }

    private async Task PersistSuccessfulLoginAsync(LoginRequestedEventArgs request)
    {
        try
        {
            bool remember = request.RememberAccount || request.AutoLogin;
            ClientSettings settings = await _settingsService.LoadAsync();
            settings = settings with
            {
                RememberAccount = remember,
                AutoLogin = request.AutoLogin,
                SavedEmail = remember ? request.Email.Trim() : null
            };
            await _settingsService.SaveAsync(settings);

            if (request.AutoLogin)
            {
                await _credentialService.SaveAsync(request.Email, request.Password);
            }
            else
            {
                await _credentialService.DeleteAsync();
            }
        }
        catch
        {
            // 登录已经成功，凭据保存失败不应阻止进入主界面。
        }
    }

    private async Task DisableAutoLoginAsync(ClientSettings settings)
    {
        await _settingsService.SaveAsync(settings with { AutoLogin = false });
        await _credentialService.DeleteAsync();
        AutoLoginCheckBox.IsChecked = false;
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
    bool rememberAccount = false,
    bool autoLogin = false) : EventArgs
{
    public string Email { get; } = email;
    public string Password { get; } = password;
    public bool RememberAccount { get; } = rememberAccount;
    public bool AutoLogin { get; } = autoLogin;
}
