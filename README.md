# 快云客户端

全新的 Windows 官方客户端项目，从干净框架开始开发。

## 当前阶段

当前仓库已经完成：

- 登录、首页、节点、设置四个页面
- 多 OSS 配置读取、本地缓存和失败回退
- V2Board 邮箱密码登录和多个 API 地址重试
- 账号、流量、到期时间和订阅地址读取
- 下载并缓存 `flag=meta` 订阅
- 解析真实 Mihomo 节点
- 252 个国家、地区和特殊区域图标目录
- Mihomo 内核下载、启动、停止、日志和健康检查
- 真实节点切换和最多 6 路并发延迟测速
- Windows 系统代理备份、启用、恢复和异常保护
- `BuiltInProxy` 应急代理和故障恢复
- 设置项本地保存
- GitHub Actions Windows x64 构建与发布

当前尚未完成：

- 自动更新
- 开机启动和启动后自动连接的实际执行逻辑
- 最终 UI 精修、托盘和连接动画

## 正常连接流程

```text
读取 OSS 配置
→ 登录 V2Board
→ 获取账号和订阅
→ 解析真实节点
→ 选择或测速节点
→ 启动 Mihomo
→ 切换节点
→ 备份 Windows 原系统代理
→ 启用 127.0.0.1:7890
→ 首页显示已连接
```

## BuiltInProxy 应急代理

客户端始终优先直连，不会默认使用应急代理。

触发顺序：

```text
正常直连
→ 所有 OSS/API/订阅地址直连失败
→ 按 BuiltInProxy 配置顺序逐个尝试
→ 第一个成功的应急代理继续原请求
→ 全部失败后使用旧缓存或显示明确错误
```

支持的格式：

```text
http://用户名:密码@服务器:端口
https://用户名:密码@服务器:端口
socks4://用户名:密码@服务器:端口
socks4a://用户名:密码@服务器:端口
socks5://用户名:密码@服务器:端口
ss://完整的 Shadowsocks 分享链接
```

HTTP 和 SOCKS 代理由 `HttpClient` 直接使用。Shadowsocks 会临时启动一个独立 Mihomo 恢复通道，请求结束后立即停止并清理临时目录。

应急通道：

- 不修改 Windows 系统代理
- 不接管用户浏览器流量
- 不改变主连接节点
- 不与正常的 Mihomo 运行配置共用端口
- 错误信息不会显示代理密码

### OSS 恢复限制

`BuiltInProxy` 位于远程 `config.json` 中。首次安装且本地完全没有配置缓存时，客户端尚不知道应急代理内容，因此只能直连多个 OSS。

客户端至少成功读取过一次配置后，会把 `BuiltInProxy` 缓存到：

```text
%LocalAppData%\KuaiyunClient\config\config-cache.json
```

以后 OSS 直连全部失败时，可以使用缓存中的应急代理刷新远程配置。无论应急代理是否成功，仍保留旧缓存作为最终回退。

### Shadowsocks 限制

当前支持标准 SIP002 `ss://` 分享链接，包括：

```text
ss://BASE64(method:password)@server:port
ss://BASE64(method:password@server:port)
```

暂不支持带 `plugin` 参数的 Shadowsocks 分享链接。

### 配置安全

代理地址、用户名和密码会存在 OSS `config.json` 以及客户端本地配置缓存中。不要把含真实代理凭据的配置文件放在公开仓库；应限制 OSS 文件访问或使用专门的低权限应急代理。

建议只配置 2–3 条不同网络和地区的应急代理，避免全部位于同一服务商或同一 IP 段。

## OSS config.json

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

配置真实应急代理时，把完整代理 URI 放进数组。例如：

```json
{
  "BuiltInProxy": [
    "socks5://user:password@proxy-a.example.com:1080",
    "http://user:password@proxy-b.example.com:8080"
  ]
}
```

Shadowsocks 请直接粘贴服务商提供的完整 `ss://` 分享链接，不要手工拆分字段。

## bootstrap.json

安装包中的 `bootstrap.json` 只保存多个 OSS 地址和刷新时间：

```json
{
  "CloudConfig": [
    "https://software.lvoeky.com/config.json",
    "https://备用OSS/config.json"
  ],
  "CloudUpdateHours": 3
}
```

## Windows 系统代理安全机制

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

程序被强制结束或电脑断电后，下次启动会检测备份并自动恢复。

备份路径：

```text
%LocalAppData%\KuaiyunClient\state\proxy-backup.json
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
→ 测速结束后停止临时 Mihomo
```

测试地址：

```text
https://www.gstatic.com/generate_204
```

测速不会切换节点，也不会开启 Windows 系统代理。

## Mihomo 运行范围

```yaml
mixed-port: 7890
allow-lan: false
bind-address: 127.0.0.1
unified-delay: true
external-controller: 127.0.0.1:9090
tun:
  enable: false
```

即使订阅启用了 TUN，客户端也会强制关闭，只通过本机混合代理端口运行。

## 本地文件

```text
%LocalAppData%\KuaiyunClient\config\config-cache.json
%LocalAppData%\KuaiyunClient\subscription\current.yaml
%LocalAppData%\KuaiyunClient\runtime\config.yaml
%LocalAppData%\KuaiyunClient\runtime\mihomo.log
%LocalAppData%\KuaiyunClient\settings\client-settings.json
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

1. 实现客户端自动更新。
2. 实现开机启动和自动连接。
3. 统一精修 UI、托盘图标、窗口按钮和连接动画。
