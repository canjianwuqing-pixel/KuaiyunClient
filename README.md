# 快云客户端

全新的 Windows 客户端项目，从干净框架开始开发。

## 当前阶段

当前仓库只包含 UI 和项目结构：

- 登录页
- 首页
- 节点页
- 设置页
- 主窗口导航
- OSS 配置示例
- GitHub Actions 编译检查

当前暂未接入 V2Board、订阅、Mihomo、系统代理、更新和内置代理。

## 技术栈

- .NET 8
- WPF
- Windows x64

## 构建

```powershell
dotnet build .\src\KuaiyunClient\KuaiyunClient.csproj -c Release
```
