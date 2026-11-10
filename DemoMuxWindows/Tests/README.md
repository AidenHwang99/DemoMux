# Windows 回归

使用 .NET 10 SDK，从仓库根目录运行（夹具使用临时端口，可以与应用并行）：

```powershell
dotnet run --project DemoMuxWindows/Tests/DemoMux.Tests.csproj -c Release
```

测试在 `.tools/test-data` 使用隔离数据目录，首次按锁定版本下载 Caddy；不读取或改写用户账号。夹具使用 Windows 目录联接，无需启用开发者符号链接权限。回归失败返回非零退出码。

在命令末尾增加 `-- --fresh-data` 可使用全新的 `.tools/test-data-UUID`，核验首次运行不依赖已有目录或组件；该隔离目录保留以便排查。

当前 208 项覆盖名额分配与释放、静态快照和隐藏项/联接排除、入口越界、真实 Caddy 路由和资源、首页与介绍页、账号过渡态和身份变化、发布标记与跳转拦截、缓存篡改拒绝、运行时只读权限、证书错误及 Job Object 强制退出。

`--probe <URL> <标记>` 可独立验证公网入口；`--install-fresh` 在独立目录验证公开组件下载，不读取用户授权。`--http-fixture <模式文件>` 用于实机 HTTP 项目测试，请将其发布到独立的输出目录，避免后台夹具锁住回归构建目录。

Tailscale EFS 专项：按组件说明准备固定源码，复制 `Components/atomicfile_windows_efs_test.go` 到源码 `atomicfile/`，设置 `DEMOMUX_EFS_TEST_DIR` 为已有加密测试目录，再运行 `go test -v ./atomicfile -run TestDemoMuxEFSAtomicReplace`。仅操作新建的测试子目录，不修改账号状态。

GUI、Tailscale 授权、独立外部网络和多 DPI 场景需另做实机验收，不能以这组回归代替。见仓库验收记录。

`--retry-ui` 为独立的界面故障夹具，不启动网络组件、不读取真实授权；窗口明确标记为隔离交互测试，不代表真实账号登录或前置步骤通过。可检查倒计时、停止、恢复和流光连续播放。


发布方式预检新增真实隔离 HTTP 夹具和 WPF 弹窗回归，覆盖子路径 404、已配置路径、SPA 任意路径 200、根路径 404 而子路径成功、权限、超时、连接失败、同前缀跳转、三跳限制和越界拦截。会话测试覆盖编辑防抖、过期重测、模式／端口／项目键匹配、慢响应丢弃及关闭取消。WPF 回归验证固定底栏、窄窗、键盘焦点、剪贴板失败反馈、草稿保留、名额未提前占用，以及公网 404 后显式切回推荐方式。

名称与路径回归覆盖 Mac 混合语言规则、40 字符截断、纯中文／重名 UUID 回退、自动路径改名、自定义／已发布路径保护、独立入口锁定规则和目录持久化。真实 WPF 弹窗覆盖发布方式下拉框、已发布路径重新生成的确认／取消、预览同步、旧预检失效，以及生成草稿不修改保存路径或占用名额。

`--publish-ui` 打开推荐方式的隔离发布弹窗；增加 `--advanced` 直接选择下拉框中的按项目名发布，显示混合语言名称生成结果及子路径 404。夹具只监听随机本机端口，不启动 Tailscale、不访问公网、不读取真实授权。不要在夹具里点击真正发布；此夹具用于预检与选择流程的交互验证，不代表公网发布成功。

托盘生命周期新增 14 项 WPF 自动回归，使用可控托盘实现覆盖偏好保存、隐藏时不取消任务或清理项目／名额、恢复与设置、明确退出取消和清理、托盘失败防止隐形驻留、关闭方式回退，以及系统会话结束。`--tray-smoke` 在有 Explorer 的 Windows 桌面检查真实 Shell_NotifyIcon 注册、隐藏、恢复和退出；`--tray-ui` 留下隔离托盘窗口供人工测试。两个入口使用随机 `.tools/tray-test-UUID`，不启动网络组件或读取真实账号。CI 不把无交互桌面的原生托盘检查当成通过。

设置布局另增加 3 项实际 WPF 几何检查：底栏左右分布、支持操作右侧对齐、440×400 窄窗按钮不越界及固定底栏可见。调整布局不重建偏好控件。

文档导出可独立运行 `pwsh -File DemoMuxWindows/export-docs.ps1 -Destination <隔离目录>`：应生成 Windows 专用 HTML、首次使用、GuideIcon.png、验收和更新记录共五个文件。核对 HTML 与 Assets/Guide.html 散列一致、Markdown 图标引用为同目录 GuideIcon.png；该步骤不下载组件或创建 Release。完整构建及发布脚本共用同一导出入口。

## 完整安装包

`--bundled-components <解压目录>` 在隔离数据目录将下载地址改为不可达，验证实际内置 Caddy 和 Tailscale 安装及哈希。

`pwsh -File DemoMuxWindows/Tests/InstallerSmoke.ps1 -Payload <发行目录> -PreviousPayload <已校验的旧版目录>` 以独立注册身份检查中文空格路径、全包哈希、快捷方式、升级、修复、降级保护、运行互斥、卸载和重装。默认卸载测试副本；不会读取或删除真实账号和原始项目。
