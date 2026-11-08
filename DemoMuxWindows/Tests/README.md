# Windows 回归

使用 .NET 10 SDK，从仓库根目录运行（夹具使用临时端口，可以与应用并行）：

```powershell
dotnet run --project DemoMuxWindows/Tests/DemoMux.Tests.csproj -c Release
```

测试在 `.tools/test-data` 使用隔离数据目录，首次按锁定版本下载 Caddy；不读取或改写用户账号。夹具使用 Windows 目录联接，无需启用开发者符号链接权限。回归失败返回非零退出码。

覆盖名额分配和失败释放、静态快照与隐藏项/联接排除、入口越界、真实 Caddy 路由和资源、首页与介绍页、发布标记和跳转拦截，以及 Job Object 子进程退出。

GUI、Tailscale 授权、独立外部网络和多 DPI 场景需另做实机验收，不能以这组回归代替。见仓库验收记录。
