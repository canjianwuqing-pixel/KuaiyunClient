# 快云客户端

全新的 Windows 官方客户端项目，从干净框架开始开发。

## 当前阶段

当前仓库已经包含一套可继续开发的 v0.1 框架：

- `ShellWindow` 主窗口与页面导航
- 登录页、首页、节点页、设置页
- `BootstrapConfig` 与 OSS `AppConfig` 模型
- 用户会话和节点模型
- OSS 配置、V2Board、Mihomo、Windows 系统代理、更新服务接口
- GitHub Actions Windows 编译检查

当前只搭建框架，尚未接入真实 V2Board 请求、订阅解析、Mihomo 内核、系统代理实现、自动更新和内置代理恢复逻辑。

## 配置原则

- 安装包内的 `bootstrap.json`：只保存多个 OSS 配置地址和刷新间隔。
- OSS 的 `config.json`：只保存品牌、API 地址、客服、更新地址和 `BuiltInProxy`。
- 后台固定使用 V2Board，订阅格式固定使用 `meta`，不再放入 OSS 配置。

## 项目结构

```text
KuaiyunClient/
├─ config/
│  ├─ bootstrap.example.json
│  └─ config.example.json
├─ src/KuaiyunClient/
│  ├─ Models/
│  ├─ Services/
│  ├─ Views/
│  ├─ App.xaml
│  ├─ ShellWindow.xaml
│  └─ KuaiyunClient.csproj
└─ .github/workflows/build.yml
```

## 技术栈

- .NET 8
- WPF
- Windows x64

## 构建

```powershell
dotnet build .\src\KuaiyunClient\KuaiyunClient.csproj -c Release
```

## 下一步

1. 实现 OSS 多地址读取与本地缓存。
2. 实现 V2Board 登录和用户信息。
3. 下载订阅并解析真实节点。
4. 接入 Mihomo、节点切换和 Windows 系统代理。
