using KuaiyunClient.Services;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace KuaiyunClient;

public partial class App : Application
{
    private const string SelfTestArgument = "--self-test";

    private const string MihomoSmokeTestConfig = """
proxies: []
proxy-groups: []
rules:
  - MATCH,DIRECT
""";

    private static readonly object LogGate = new();

    public static string StartupLogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KuaiyunClient",
        "logs",
        "startup.log");

    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        WriteLog("客户端进程已启动。", null);
    }

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        if (e.Args.Any(argument =>
                string.Equals(argument, SelfTestArgument, StringComparison.OrdinalIgnoreCase)))
        {
            // 自检会创建完整窗口对象，但不能走正常 Closing 事件，否则系统代理安全恢复逻辑会拦截退出。
            Environment.Exit(RunSelfTest());
            return;
        }

        try
        {
            WriteLog(
                $"正在创建主窗口。版本：{typeof(App).Assembly.GetName().Version}；目录：{AppContext.BaseDirectory}",
                null);

            ShellWindow window = new();
            MainWindow = window;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            window.Show();

            WriteLog("主窗口已显示。", null);
        }
        catch (Exception ex)
        {
            ShowFatalError("快云客户端启动失败", ex);
            Shutdown(-1);
        }
    }

    private static int RunSelfTest()
    {
        try
        {
            WriteLog("开始执行 Windows 发布包启动自检。", null);

            string bootstrapPath = Path.Combine(AppContext.BaseDirectory, "bootstrap.json");
            string corePath = Path.Combine(AppContext.BaseDirectory, "core", "mihomo.exe");

            if (!File.Exists(bootstrapPath))
            {
                throw new FileNotFoundException("发布包缺少 bootstrap.json。", bootstrapPath);
            }

            if (!File.Exists(corePath))
            {
                throw new FileNotFoundException("发布包缺少 core\\mihomo.exe。", corePath);
            }

            if (new FileInfo(corePath).Length < 1_000_000)
            {
                throw new InvalidDataException(
                    $"core\\mihomo.exe 文件大小异常：{corePath}");
            }

            using (JsonDocument bootstrap = JsonDocument.Parse(File.ReadAllText(bootstrapPath)))
            {
                if (!bootstrap.RootElement.TryGetProperty("CloudConfig", out JsonElement cloudConfig)
                    || cloudConfig.ValueKind != JsonValueKind.Array
                    || cloudConfig.GetArrayLength() == 0)
                {
                    throw new InvalidDataException(
                        $"bootstrap.json 缺少 CloudConfig 地址：{bootstrapPath}");
                }
            }

            // 创建完整主窗口及所有子页面，验证 WPF XAML、资源和构造流程可以正常加载。
            ShellWindow window = new();
            GC.KeepAlive(window);
            WriteLog("WPF 页面与资源自检通过。", null);

            // 在后台线程真实启动 Mihomo，验证配置校验、Controller 监听和停止流程。
            Task.Run(RunMihomoSmokeTestAsync).GetAwaiter().GetResult();

            WriteLog("Windows 发布包启动自检通过。", null);
            return 0;
        }
        catch (Exception ex)
        {
            WriteLog("Windows 发布包启动自检失败。", ex);
            return 1;
        }
    }

    private static async Task RunMihomoSmokeTestAsync()
    {
        WriteLog("开始执行 Mihomo Controller 启动自检。", null);

        using MihomoService service = new();
        await service.StartAsync(MihomoSmokeTestConfig, CancellationToken.None);

        if (!service.IsRunning)
        {
            throw new InvalidOperationException("Mihomo 自检启动后未保持运行状态。");
        }

        await service.StopAsync(CancellationToken.None);
        WriteLog("Mihomo Controller 启动与停止自检通过。", null);
    }

    private void Application_DispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        ShowFatalError("快云客户端发生未处理错误", e.Exception);
        Shutdown(-1);
    }

    private static void CurrentDomain_UnhandledException(
        object sender,
        UnhandledExceptionEventArgs e)
    {
        Exception exception = e.ExceptionObject as Exception
            ?? new Exception(e.ExceptionObject?.ToString() ?? "未知未处理错误");

        WriteLog(
            e.IsTerminating ? "进程即将因未处理错误退出。" : "捕获到未处理错误。",
            exception);
    }

    private static void TaskScheduler_UnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs e)
    {
        WriteLog("捕获到未观察的异步任务错误。", e.Exception);
        e.SetObserved();
    }

    private void Application_Exit(object sender, ExitEventArgs e)
    {
        WriteLog($"客户端进程退出，代码：{e.ApplicationExitCode}。", null);
    }

    private static void ShowFatalError(string title, Exception exception)
    {
        WriteLog(title, exception);

        string message = title
            + Environment.NewLine
            + Environment.NewLine
            + exception.Message
            + Environment.NewLine
            + Environment.NewLine
            + "错误日志："
            + Environment.NewLine
            + StartupLogPath;

        try
        {
            MessageBox.Show(
                message,
                "快云加速",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // 如果连错误窗口都无法创建，日志仍然会保留。
        }
    }

    private static void WriteLog(string message, Exception? exception)
    {
        try
        {
            lock (LogGate)
            {
                string? directory = Path.GetDirectoryName(StartupLogPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                StringBuilder output = new();
                output.Append('[')
                    .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"))
                    .Append("] ")
                    .AppendLine(message);

                if (exception is not null)
                {
                    output.AppendLine(exception.ToString());
                }

                File.AppendAllText(
                    StartupLogPath,
                    output.ToString(),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
        }
        catch
        {
            // 启动日志不能反过来阻止客户端启动。
        }
    }
}