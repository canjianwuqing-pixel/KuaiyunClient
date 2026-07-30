using KuaiyunClient.Models;
using KuaiyunClient.Services;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KuaiyunClient.Views;

public partial class PurchaseView : UserControl
{
    private readonly V2BoardCommerceApi _commerceApi = new();
    private UserSession? _session;
    private AppConfig? _apiConfig;
    private bool _busy;
    private int _loadVersion;

    public event EventHandler<PurchaseRequestedEventArgs>? PurchaseRequested;

    public PurchaseView()
    {
        InitializeComponent();
    }

    public void ShowSession(UserSession session)
    {
        _session = session;
        _apiConfig = BuildApiConfig(session);
        RenderSession(session);
        int version = ++_loadVersion;
        _ = RefreshPlansAsync(session, version);
    }

    public void SetPlans(IEnumerable<V2BoardPlan> plans)
    {
        V2BoardPlan[] available = plans
            .Where(plan => plan.Cycles.Count > 0)
            .ToArray();

        PlansItems.ItemsSource = available;
        PlansItems.Visibility = available.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Visibility = available.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
        StatusText.Text = available.Length > 0
            ? string.Empty
            : "后台暂未提供可购买套餐。";
    }

    public void SetBusy(bool busy, string? message = null)
    {
        _busy = busy;
        PlansItems.IsEnabled = !busy;
        if (busy || !string.IsNullOrWhiteSpace(message))
        {
            StatusText.Text = message ?? "正在处理订单...";
            StatusText.Visibility = Visibility.Visible;
        }
        else if (PlansItems.Items.Count > 0)
        {
            StatusText.Visibility = Visibility.Collapsed;
            StatusText.Text = string.Empty;
        }
    }

    public void ShowError(string message)
    {
        SetBusy(false, message);
    }

    private async Task RefreshPlansAsync(UserSession session, int version)
    {
        if (_apiConfig is null)
        {
            ShowError("后台地址无效，暂时无法读取套餐。");
            return;
        }

        try
        {
            await Dispatcher.InvokeAsync(() => SetBusy(true, "正在读取套餐..."));
            await _commerceApi.RefreshSessionAsync(_apiConfig, session);
            IReadOnlyList<V2BoardPlan> plans = await _commerceApi.GetPlansAsync(
                _apiConfig,
                session);
            V2BoardPlan? currentPlan = session.PlanId is int planId
                ? plans.FirstOrDefault(plan => plan.Id == planId)
                : null;
            session.PlanName = currentPlan?.Name ?? "未订阅套餐";

            if (version != _loadVersion || !ReferenceEquals(session, _session))
            {
                return;
            }

            await Dispatcher.InvokeAsync(() =>
            {
                RenderSession(session);
                SetPlans(plans);
                SetBusy(false);
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() => ShowError("套餐读取失败：" + ex.Message));
        }
    }

    private async void PurchaseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy
            || _session is null
            || _apiConfig is null
            || sender is not Button
            {
                Tag: V2BoardPlan plan,
                CommandParameter: PlanCycleOption cycle
            })
        {
            return;
        }

        MessageBoxResult confirmation = MessageBox.Show(
            $"确认购买：{plan.Name}{Environment.NewLine}"
            + $"周期：{cycle.Name}{Environment.NewLine}"
            + $"金额：{cycle.PriceText}",
            "确认订单",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        SetBusy(true, "正在创建订单...");
        try
        {
            string tradeNo = await _commerceApi.CreateOrderAsync(
                _apiConfig,
                _session,
                plan.Id,
                cycle.Key);

            SetBusy(true, "正在读取支付方式...");
            IReadOnlyList<V2BoardPaymentMethod> methods =
                await _commerceApi.GetPaymentMethodsAsync(_apiConfig, _session);
            if (methods.Count == 0)
            {
                MessageBox.Show(
                    $"订单已创建：{tradeNo}{Environment.NewLine}后台暂未返回可用支付方式。",
                    "订单已创建",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            V2BoardPaymentMethod? method = SelectPaymentMethod(methods);
            if (method is null)
            {
                return;
            }

            SetBusy(true, "正在发起支付...");
            V2BoardCheckoutResult checkout = await _commerceApi.CheckoutOrderAsync(
                _apiConfig,
                _session,
                tradeNo,
                method.Id);

            if (OpenExternal(checkout.Data))
            {
                MessageBox.Show(
                    "支付页面已打开。支付完成后重新进入购买页即可刷新套餐。",
                    "等待支付",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                Clipboard.SetText(checkout.Data);
                MessageBox.Show(
                    "支付内容已复制到剪贴板，请粘贴到浏览器或支付应用。",
                    "支付信息",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "购买失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            ShowError("购买失败：" + ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void RenderSession(UserSession session)
    {
        CurrentPlanText.Text = string.IsNullOrWhiteSpace(session.PlanName)
            ? "未订阅套餐"
            : session.PlanName;
        PlanExpiryText.Text = session.ExpiredAt > 0
            ? "到期时间 " + DateTimeOffset
                .FromUnixTimeSeconds(session.ExpiredAt)
                .LocalDateTime
                .ToString("yyyy-MM-dd")
            : "到期时间 --";
    }

    private V2BoardPaymentMethod? SelectPaymentMethod(
        IReadOnlyList<V2BoardPaymentMethod> methods)
    {
        if (methods.Count == 1)
        {
            return methods[0];
        }

        Window dialog = new()
        {
            Title = "选择支付方式",
            Owner = Window.GetWindow(this),
            Width = 340,
            Height = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = (Brush)FindResource("WindowBrush")
        };

        Grid root = new() { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        TextBlock title = new()
        {
            Text = "请选择支付方式",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 12)
        };
        root.Children.Add(title);

        ListBox list = new()
        {
            ItemsSource = methods,
            DisplayMemberPath = nameof(V2BoardPaymentMethod.DisplayName),
            SelectedIndex = 0,
            BorderBrush = (Brush)FindResource("LineBrush"),
            Background = Brushes.White,
            Padding = new Thickness(8)
        };
        Grid.SetRow(list, 1);
        root.Children.Add(list);

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        Button cancel = new()
        {
            Content = "取消",
            Width = 90,
            Height = 40,
            Margin = new Thickness(0, 0, 8, 0),
            IsCancel = true
        };
        Button confirm = new()
        {
            Content = "继续支付",
            Width = 110,
            Height = 40,
            IsDefault = true
        };
        confirm.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        dialog.Content = root;
        return dialog.ShowDialog() == true
            ? list.SelectedItem as V2BoardPaymentMethod
            : null;
    }

    private static AppConfig? BuildApiConfig(UserSession session)
    {
        if (!Uri.TryCreate(session.SubscriptionUrl, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        string host = uri.GetLeftPart(UriPartial.Authority);
        session.ApiHost = host;
        return new AppConfig
        {
            UserAgent = "kuaiyun",
            RemoteHosts = [host]
        };
    }

    private static bool OpenExternal(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        return true;
    }
}

public sealed class PurchaseRequestedEventArgs(
    V2BoardPlan plan,
    PlanCycleOption cycle) : EventArgs
{
    public V2BoardPlan Plan { get; } = plan;

    public PlanCycleOption Cycle { get; } = cycle;
}
