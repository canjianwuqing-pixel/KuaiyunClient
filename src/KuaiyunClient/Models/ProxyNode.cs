using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace KuaiyunClient.Models;

public sealed class ProxyNode : INotifyPropertyChanged
{
    private int? _delayMilliseconds;
    private DelayTestState _delayState;

    /// <summary>
    /// Mihomo 配置中的原始节点名称。节点切换和测速时必须使用这个名称。
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 在界面中显示的名称，包含自动识别的国家或地区旗帜。
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    public string GroupName { get; init; } = string.Empty;

    public string Type { get; init; } = string.Empty;

    public string? Server { get; init; }

    public string CountryCode { get; init; } = string.Empty;

    public string CountryName { get; init; } = "其他地区";

    public string CountryFlag { get; init; } = "🌐";

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
        DelayTestState.Testing => "测速中...",
        DelayTestState.Success when _delayMilliseconds is int delay => $"{delay} ms",
        DelayTestState.Failed => "超时",
        _ => "未测速"
    };

    public bool IsSelected { get; set; }

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
