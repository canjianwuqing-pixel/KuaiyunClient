using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace KuaiyunClient.Models;

public sealed class ProxyNode : INotifyPropertyChanged
{
    private int? _delayMilliseconds;
    private DelayTestState _delayState;
    private bool _isSelected;

    /// <summary>
    /// Mihomo 配置中的原始节点名称。节点切换和测速时必须使用这个名称。
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 面向普通用户的线路名称，不包含协议、服务器地址或重复国家代码。
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    public string GroupName { get; init; } = string.Empty;

    public string Type { get; init; } = string.Empty;

    public string? Server { get; init; }

    public string CountryCode { get; init; } = string.Empty;

    public string CountryName { get; init; } = "其他地区";

    public string CountryFlag { get; init; } = "🌐";

    public string FlagImagePath
    {
        get
        {
            string code = string.IsNullOrWhiteSpace(CountryCode)
                ? "unknown"
                : CountryCode.Trim().ToLowerInvariant();

            string candidate = Path.Combine(
                AppContext.BaseDirectory,
                "Assets",
                "Flags",
                code + ".png");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            return Path.Combine(
                AppContext.BaseDirectory,
                "Assets",
                "Flags",
                "unknown.png");
        }
    }

    public int? DelayMilliseconds
    {
        get => _delayMilliseconds;
        set
        {
            if (_delayMilliseconds == value)
            {
                return;
            }

            _delayMilliseconds = value;
            _delayState = value is > 0
                ? DelayTestState.Success
                : DelayTestState.NotTested;

            OnPropertyChanged();
            OnPropertyChanged(nameof(DelayState));
            OnPropertyChanged(nameof(DelayText));
        }
    }

    public DelayTestState DelayState => _delayState;

    public string DelayText => _delayState switch
    {
        DelayTestState.Testing => "检测中",
        DelayTestState.Success when _delayMilliseconds is int delay => $"{delay} ms",
        DelayTestState.Failed => "--",
        _ => "--"
    };

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void BeginDelayTest()
    {
        _delayMilliseconds = null;
        _delayState = DelayTestState.Testing;
        OnPropertyChanged(nameof(DelayMilliseconds));
        OnPropertyChanged(nameof(DelayState));
        OnPropertyChanged(nameof(DelayText));
    }

    public void CompleteDelayTest(int? delayMilliseconds)
    {
        _delayMilliseconds = delayMilliseconds is > 0 ? delayMilliseconds : null;
        _delayState = _delayMilliseconds.HasValue
            ? DelayTestState.Success
            : DelayTestState.Failed;

        OnPropertyChanged(nameof(DelayMilliseconds));
        OnPropertyChanged(nameof(DelayState));
        OnPropertyChanged(nameof(DelayText));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public enum DelayTestState
{
    NotTested,
    Testing,
    Success,
    Failed
}
