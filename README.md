# 快云客户端

全新的 Windows 官方客户端项目，从干净框架开始开发。

## 当前阶段

当前仓库已经完成：

- `ShellWindow` 主窗口与页面导航
- 登录页、首页、节点页、设置页
- 多 OSS 配置读取与本地缓存回退
- V2Board 邮箱密码登录与多个 API 地址重试
- 获取账号邮箱、流量、到期时间和订阅地址
- 登录成功后下载 `flag=meta` 订阅
- 保存原始订阅 YAML 到本地
- 解析真实 `proxies` 节点
- 252 个国家、地区和特殊区域图标目录
- Mihomo Windows x64 内核下载脚本
- Mihomo 运行配置生成
- Mihomo 启动、停止、日志和健康检查
- 通过 Mihomo Controller 切换真实节点
- Mihomo 异常退出检测
- GitHub Actions 自动下载内核、编译和发布 Windows x64 测试包

当前尚未完成：

- 真实延迟测速
- Windows 系统代理开启与恢复
- `BuiltInProxy` 应急代理
- 自动更新
- 最终 UI 精修

## 完整流程

```text
读取 OSS 配置
→ 用户登录 V2Board
→ 获取账号和订阅地址
→ 下载 flag=meta 订阅
→ 解析真实节点与国家图标
→ 用户选择节点
→ 生成 Mihomo 运行配置
→ 启动 mihomo.exe
→ 等待 Controller 就绪
→ 切换到用户选择的节点
→ 首页显示已连接
```

## 当前连接范围

这一阶段只启动本地 Mihomo 代理：

```text
127.0.0.1:7890
```

客户端目前不会修改 Windows 系统代理，也不会自动接管浏览器流量。

运行配置会强制使用：

```yaml
mixed-port: 7890
allow-lan: false
bind-address: 127.0.0.1
external-controller: 127.0.0.1:9090
tun:
  enable: false
```

即使订阅配置中启用了 TUN，客户端生成运行配置时也会将其关闭。

## 本地文件

OSS 配置缓存：

```text
%LocalAppData%\KuaiyunClient\config\config-cache.json
```

原始订阅缓存：

```text
%LocalAppData%\KuaiyunClient\subscription\current.yaml
```

Mihomo 运行配置：

```text
%LocalAppData%\KuaiyunClient\runtime\config.yaml
```

Mihomo 日志：

```text
%LocalAppData%\KuaiyunClient\runtime\mihomo.log
```

## Mihomo 内核

内核不会直接提交到 Git 仓库。开发或本地打包前运行：

```powershell
.\scripts\download-mihomo.ps1
```

脚本会从 Mihomo 官方 GitHub Release 下载 Windows amd64 兼容版，并保存到：

```text
src\KuaiyunClient\core\mihomo.exe
```

也可以指定版本：

```powershell
.\scripts\download-mihomo.ps1 -Version v版本号
```

GitHub Actions 会在构建前自动执行该脚本。

## 配置原则

安装包中的 `bootstrap.json` 只保存多个 OSS 地址：

```json
{
  "CloudConfig": [
    "https://software.lvoeky.com/config.json",
    "https://备用OSS/config.json"
  ],
  "CloudUpdateHours": 3
}
```

OSS 的 `config.json`：

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

后台固定使用 V2Board，订阅格式固定为 `meta`，不再配置 `RemoteType` 和 `SubFlag`。

## 项目结构

```text
KuaiyunClient/
├─ config/
│  ├─ bootstrap.example.json
│  └─ config.example.json
├─ scripts/
│  └─ download-mihomo.ps1
├─ src/KuaiyunClient/
│  ├─ Models/
│  ├─ Services/
│  ├─ Views/
│  ├─ core/                 # 构建时生成，不提交 mihomo.exe
│  ├─ App.xaml
│  ├─ ShellWindow.xaml
│  └─ KuaiyunClient.csproj
└─ .github/workflows/build.yml
```

## 本地构建

```powershell
.\scripts\download-mihomo.ps1
dotnet build .\src\KuaiyunClient\KuaiyunClient.csproj -c Release
```

发布 Windows x64：

```powershell
dotnet publish .\src\KuaiyunClient\KuaiyunClient.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=false `
  -o .\artifacts\KuaiyunClient-win-x64
```

## 下一步

1. 通过 Mihomo Controller 测试真实节点延迟。
2. 实现 Windows 系统代理的开启、保存与恢复。
3. 增加程序异常退出后的代理恢复。
4. 接入 `BuiltInProxy` 应急代理。
5. 实现自动更新并统一精修 UI。
