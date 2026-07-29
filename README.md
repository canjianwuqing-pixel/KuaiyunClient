# 快云客户端

全新的 Windows 官方客户端项目，从干净框架开始开发。

## 当前阶段

当前仓库已经完成：

- `ShellWindow` 主窗口与登录、首页、节点、设置页面
- 多 OSS 配置读取、本地缓存与失败回退
- V2Board 邮箱密码登录和多个 API 地址重试
- 获取账号、流量、到期时间与订阅地址
- 下载并缓存 `flag=meta` 订阅
- 解析真实 Mihomo `proxies` 节点
- 252 个国家、地区与特殊区域图标目录
- Mihomo Windows x64 内核下载、启动、停止、日志和健康检查
- 通过 Mihomo Controller 切换真实节点
- 最多 6 个节点并发真实延迟测速
- Windows 系统代理备份、启用、恢复与异常退出保护
- 设置项本地保存
- GitHub Actions 自动下载内核、编译和发布 Windows x64 测试包

当前尚未完成：

- `BuiltInProxy` 应急代理
- 自动更新
- 开机启动和启动后自动连接的实际执行逻辑
- 最终 UI 精修、托盘和连接动画

## 完整连接流程

```text
读取 OSS 配置
→ 用户登录 V2Board
→ 获取账号和订阅地址
→ 下载 flag=meta 订阅
→ 解析真实节点与国家图标
→ 用户选择节点
→ 可选：测试全部节点真实延迟
→ 生成 Mihomo 运行配置
→ 启动 mihomo.exe
→ 等待 Controller 就绪
→ 切换到用户选择的节点
→ 备份 Windows 原系统代理
→ 启用 127.0.0.1:7890 系统代理
→ 首页显示已连接
```

## Windows 系统代理安全机制

设置页默认开启“连接时使用 Windows 系统代理”。

连接时：

```text
读取当前 Windows 代理设置
→ 写入 proxy-backup.json
→ 启用 127.0.0.1:7890
→ 通知 Windows 刷新 Internet Settings
```

断开时：

```text
先恢复原 Windows 代理设置
→ 恢复成功后停止 Mihomo
```

如果恢复失败，客户端不会停止 Mihomo，避免系统代理继续指向已经关闭的本地端口而导致断网。

程序正常退出时会先恢复代理，再停止 Mihomo。Mihomo 意外退出时也会立即尝试恢复代理。如果程序被强制结束或电脑断电，下次启动客户端会检测备份文件并自动恢复。

系统代理备份路径：

```text
%LocalAppData%\KuaiyunClient\state\proxy-backup.json
```

如果备份文件损坏，客户端会关闭 Windows 手动代理，并将损坏文件保留为：

```text
proxy-backup.json.corrupt-年月日-时分秒
```

客户端只修改当前 Windows 用户的 Internet Settings，不修改 WinHTTP 全局代理。

## 真实延迟测速

节点页提供“全部测速”按钮：

```text
检查订阅和节点
→ 未运行时临时启动 Mihomo
→ 最多并发测试 6 个节点
→ GET /proxies/{节点名}/delay
→ 单节点最多等待 5 秒
→ 实时显示 ms 或超时
→ 临时启动的 Mihomo 在测速结束后自动停止
```

当前测试地址：

```text
https://www.gstatic.com/generate_204
```

测速不会自动切换当前节点，也不会开启 Windows 系统代理。

## Mihomo 运行范围

客户端生成的运行配置固定包含：

```yaml
mixed-port: 7890
allow-lan: false
bind-address: 127.0.0.1
unified-delay: true
external-controller: 127.0.0.1:9090
tun:
  enable: false
```

即使订阅配置启用了 TUN，客户端也会强制关闭，只通过本机混合代理端口运行。

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

客户端设置：

```text
%LocalAppData%\KuaiyunClient\settings\client-settings.json
```

## Mihomo 内核

内核不会直接提交到 Git 仓库。开发或本地打包前运行：

```powershell
.\scripts\download-mihomo.ps1
```

脚本会下载 Windows amd64 兼容版并保存到：

```text
src\KuaiyunClient\core\mihomo.exe
```

GitHub Actions 会在构建前自动执行下载脚本。

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

后台固定使用 V2Board，订阅格式固定为 `meta`，不配置 `RemoteType` 和 `SubFlag`。

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

1. 接入 `BuiltInProxy` 应急代理与 API/OSS 故障恢复。
2. 实现客户端自动更新。
3. 实现开机启动和自动连接。
4. 统一精修 UI、托盘图标、窗口按钮和连接动画。
