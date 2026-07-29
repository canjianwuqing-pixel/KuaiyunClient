# Windows 构建验证

此分支用于触发 GitHub Actions，对当前 Windows x64 客户端执行完整编译、发布和产物检查。

验证内容：

- .NET 8 WPF 编译
- Mihomo Windows amd64 内核下载
- Windows x64 自包含发布
- `KuaiyunClient.exe`、`bootstrap.json`、`core/mihomo.exe` 产物检查
- `KuaiyunClient-win-x64` 构建产物上传
