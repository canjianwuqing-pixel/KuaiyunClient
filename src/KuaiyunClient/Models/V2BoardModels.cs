using System.Text.RegularExpressions;

namespace KuaiyunClient.Models;

public sealed class V2BoardPlan
{
    public int Id { get; init; }

    public string Name { get; init; } = "未命名套餐";

    public long TransferEnableGigabytes { get; init; }

    public string Content { get; init; } = string.Empty;

    public bool Renew { get; init; }

    public int Sort { get; init; }

    public long? MonthPrice { get; init; }

    public long? QuarterPrice { get; init; }

    public long? HalfYearPrice { get; init; }

    public long? YearPrice { get; init; }

    public long? TwoYearPrice { get; init; }

    public long? ThreeYearPrice { get; init; }

    public long? OnetimePrice { get; init; }

    public string TrafficText => TransferEnableGigabytes > 0
        ? $"每月 {TransferEnableGigabytes} GB 流量"
        : "流量以后台套餐说明为准";

    public string DescriptionText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Content))
            {
                return "高速稳定线路，具体权益以后台配置为准。";
            }

            string withoutTags = Regex.Replace(Content, "<[^>]+>", " ");
            string decoded = WebUtility.HtmlDecode(withoutTags);
            return Regex.Replace(decoded, @"\s+", " ").Trim();
        }
    }

    public IReadOnlyList<PlanCycleOption> Cycles
    {
        get
        {
            List<PlanCycleOption> cycles = [];
            AddCycle(cycles, "month_price", "月付", MonthPrice);
            AddCycle(cycles, "quarter_price", "季付", QuarterPrice);
            AddCycle(cycles, "half_year_price", "半年付", HalfYearPrice);
            AddCycle(cycles, "year_price", "年付", YearPrice);
            AddCycle(cycles, "two_year_price", "两年付", TwoYearPrice);
            AddCycle(cycles, "three_year_price", "三年付", ThreeYearPrice);
            AddCycle(cycles, "onetime_price", "一次性", OnetimePrice);
            return cycles;
        }
    }

    private static void AddCycle(
        ICollection<PlanCycleOption> target,
        string key,
        string name,
        long? priceCents)
    {
        if (priceCents is > 0)
        {
            target.Add(new PlanCycleOption(key, name, priceCents.Value));
        }
    }
}

public sealed record PlanCycleOption(string Key, string Name, long PriceCents)
{
    public string PriceText => $"¥{PriceCents / 100m:0.##}";

    public string DisplayText => $"{Name}  {PriceText}";
}

public sealed class V2BoardNotice
{
    public int Id { get; init; }

    public string Title { get; init; } = "系统公告";

    public string Content { get; init; } = string.Empty;

    public long CreatedAt { get; init; }

    public string PlainContent
    {
        get
        {
            string withoutTags = Regex.Replace(Content ?? string.Empty, "<[^>]+>", " ");
            string decoded = WebUtility.HtmlDecode(withoutTags);
            return Regex.Replace(decoded, @"\s+", " ").Trim();
        }
    }
}

public sealed class V2BoardPaymentMethod
{
    public int Id { get; init; }

    public string Name { get; init; } = "在线支付";

    public string Payment { get; init; } = string.Empty;

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Payment : Name;
}

public sealed record V2BoardCheckoutResult(int Type, string Data);
