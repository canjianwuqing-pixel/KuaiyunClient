using KuaiyunClient.Models;
using System.Windows;
using System.Windows.Controls;

namespace KuaiyunClient.Views;

public partial class PurchaseView : UserControl
{
    public event EventHandler? PurchaseRequested;

    public PurchaseView()
    {
        InitializeComponent();
    }

    public void ShowSession(UserSession session)
    {
        CurrentPlanText.Text = "当前套餐";
        PlanExpiryText.Text = session.ExpiredAt > 0
            ? "到期时间 " + DateTimeOffset
                .FromUnixTimeSeconds(session.ExpiredAt)
                .LocalDateTime
                .ToString("yyyy-MM-dd")
            : "到期时间 --";
    }

    private void PurchaseButton_Click(object sender, RoutedEventArgs e)
    {
        PurchaseRequested?.Invoke(this, EventArgs.Empty);
    }
}
