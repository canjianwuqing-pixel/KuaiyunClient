using KuaiyunClient.Models;
using System.Windows;
using System.Windows.Controls;

namespace KuaiyunClient.Views;

public partial class PurchaseView : UserControl
{
    public event EventHandler<PurchaseRequestedEventArgs>? PurchaseRequested;

    public PurchaseView()
    {
        InitializeComponent();
    }

    public void ShowSession(UserSession session)
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

    private void PurchaseButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button
            {
                Tag: V2BoardPlan plan,
                CommandParameter: PlanCycleOption cycle
            })
        {
            StatusText.Text = "请选择套餐周期。";
            StatusText.Visibility = Visibility.Visible;
            return;
        }

        PurchaseRequested?.Invoke(this, new PurchaseRequestedEventArgs(plan, cycle));
    }
}

public sealed class PurchaseRequestedEventArgs(
    V2BoardPlan plan,
    PlanCycleOption cycle) : EventArgs
{
    public V2BoardPlan Plan { get; } = plan;

    public PlanCycleOption Cycle { get; } = cycle;
}
