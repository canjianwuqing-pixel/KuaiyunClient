# 快云客户端

全新的 Windows 官方客户端项目，从干净框架开始开发。

## 当前阶段

当前仓库已经完成：

- `ShellWindow` 主窗口与页面导航
- 登录页、首页、节点页、设置页
- `BootstrapConfig` 与 OSS `AppConfig` 模型
- 多 OSS 配置读取与本地缓存回退
- V2Board 邮箱密码登录
- 多个 V2Board API 地址自动重试
- 获取账号邮箱、流量、到期时间和订阅地址
- 登录成功后下载 `flag=meta` 订阅
- 保存原始订阅 YAML 到本地
- 解析 Mihomo YAML 的真实 `proxies` 节点
- 节点页显示真实名称、协议、服务器和国家/地区
- 自动识别国家旗帜 Emoji
- 支持英文国名、ISO 国家代码、本地语言、中文名称、城市和机场代码
- 手动刷新订阅与选择节点框架
- Mihomo、Windows 系统代理和更新服务接口
- GitHub Actions Windows 编译检查

当前尚未接入 Mihomo 内核、真实延迟测速、系统代理实现、自动更新和内置代理恢复逻辑。

## 登录和订阅流程

```text
读取 OSS 配置
→ 用户输入邮箱和密码
→ 按顺序尝试 RemoteHosts
→ POST /api/v1/passport/auth/login
→ 获取 auth_data
→ GET /api/v1/user/getSubscribe
→ 获取账号流量、到期时间和 subscribe_url
→ 下载 flag=meta 订阅
→ 解析顶层 proxies 节点
→ 自动识别国家/地区旗帜
→ 节点页显示真实线路
```

密码只用于当前登录请求，不写入配置文件或本地缓存。

## 本地文件

OSS 配置缓存：

```text
%LocalAppData%\KuaiyunClient\config\config-cache.json
```

最新订阅缓存：

```text
%LocalAppData%\KuaiyunClient\subscription\current.yaml
```

## 国家和地区图标

节点页使用 Windows 自带的旗帜 Emoji，不需要在安装包中放置大量图片。

识别顺序：

1. 节点名称中已经存在的旗帜。
2. 常见中文、繁体中文、英文国家名称。
3. 常见城市、机场代码和线路简称。
4. .NET `RegionInfo` 提供的 ISO 两位/三位国家代码、本地名称和英文名称。
5. 无法判断时显示 `🌐`，不会伪造国家。

延迟目前显示“未测速”，等待 Mihomo Controller 接入后再显示真实延迟。

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

1. 接入 Mihomo 内核启动和停止。
2. 通过 Mihomo Controller 获取真实延迟并切换节点。
3. 实现 Windows 系统代理设置与异常恢复。
4. 实现自动更新与内置代理恢复逻辑。
