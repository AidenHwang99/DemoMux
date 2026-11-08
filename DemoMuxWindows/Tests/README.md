# Windows 回归

使用 .NET 10 SDK，从仓库根目录运行（夹具使用临时端口，可以与应用并行）：

```powershell
dotnet run --project DemoMuxWindows/Tests/DemoMux.Tests.csproj -c Release
```

测试在 `.tools/test-data` 使用隔离数据目录，首次按锁定版本下载 Caddy；不读取或改写用户账号。夹具使用 Windows 目录联接，无需启用开发者符号链接权限。回归失败返回非零退出码。

当前 38 项覆盖名额分配与释放、静态快照和隐藏项/联接排除、入口越界、真实 Caddy 路由和资源、首页与介绍页、账号过渡态和身份变化、发布标记与跳转拦截、缓存篡改拒绝、运行时只读权限、证书错误及 Job Object 强制退出。

`--probe <URL> <标记>` 可独立验证公网入口；`--install-fresh` 在独立目录验证公开组件下载，不读取用户授权。`--http-fixture <模式文件>` 用于实机 HTTP 项目测试，请将其发布到独立的输出目录，避免后台夹具锁住回归构建目录。

Tailscale EFS 专项：按组件说明准备固定源码，复制 `Components/atomicfile_windows_efs_test.go` 到源码 `atomicfile/`，设置 `DEMOMUX_EFS_TEST_DIR` 为已有加密测试目录，再运行 `go test -v ./atomicfile -run TestDemoMuxEFSAtomicReplace`。仅操作新建的测试子目录，不修改账号状态。

GUI、Tailscale 授权、独立外部网络和多 DPI 场景需另做实机验收，不能以这组回归代替。见仓库验收记录。
