# 快云客户端

全新的 Windows 官方客户端项目，从干净框架开始开发。

## 当前阶段

当前仓库已经包含一套可继续开发的 v0.1 框架：

- `ShellWindow` 主窗口与页面导航
- 登录页、首页、节点页、设置页
- `BootstrapConfig` 与 OSS `AppConfig` 模型
- 用户会话和节点模型
- 多 OSS 配置读取与本地缓存回退
- V2Board、Mihomo、Windows 系统代理、更新服务接口
- GitHub Actions Windows 编译检查

当前尚未接入真实 V2Board 请求、订阅解析、Mihomo 内核、系统代理实现、自动更新和内置代理恢复逻辑。

## 配置读取流程

```text
启动客户端
→ 读取程序目录中的 bootstrap.json
→ 缓存仍在有效期内时直接使用缓存
→ 缓存过期后按顺序访问多个 OSS config.json
→ 首个有效配置写入本地缓存
→ 所有 OSS 失败时使用旧缓存
→ OSS 和缓存都不可用时显示配置错误
```

本地缓存路径：

```text
%LocalAppData%\KuaiyunClient\config\config-cache.json
```

## 配置原则

- 安装包内的 `bootstrap.json`：只保存多个 OSS 配置地址和刷新间隔。
- OSS 的 `config.json`：只保存品牌、API 地址、客服、更新地址和 `BuiltInProxy`。
- 后台固定使用 V2Board，订阅格式固定使用 `meta`，不再放入 OSS 配置。

## bootstrap.json 示例

```json
{
  "CloudConfig": [
    "https://software.lvoeky.com/config.json",
    "https://备用OSS/config.json"
  ],
  "CloudUpdateHours": 3
}
```

## OSS config.json 示例

```json
{
  "AppName": "快云加速",
  "AppLogo": "https://software.lvoeky.com/logo.png",
  "HomePage": "https://lvoeky.com",
  "TelegramGroup": "https://t.me/kuaiyunjs",
  "SupportApi": "crisp://替换为你的TOKEN",
  "UpdateUrl": "https://software.lvoeky.com/update.json",
  "UserAgent": "kuaiyun",
  "RemoteHosts": [
    "https://love.kuaiyun51.org"
  ],
  "BuiltInProxy": []
}
```

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

1. 实现 V2Board 登录和用户信息。
2. 下载订阅并解析真实节点。
3. 接入 Mihomo、节点切换和 Windows 系统代理。
4. 实现自动更新与内置代理恢复逻辑。
