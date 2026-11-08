# Windows 0.3.2 验收记录

日期：2026-11-08（Asia/Shanghai）。基线：macos-v0.3.2。开发分支：windows/0.3.2。

本机：Windows 11 专业版 x64，10.0.26100；开发 SDK 10.0.403，包内 .NET 10.0.14。Windows 10 22H2 未实测。微软官方支持范围与兼容目标分别记录：[.NET 10 支持表](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)。

| 场景 | 状态与证据 |
| --- | --- |
| Release 编译、自包含发布 | 通过，零警告零错误 |
| 三名额与独立入口顺序、路径混合、第四项阻止、释放后补位 | 自动回归通过 |
| HTML 文件夹快照、资源、隐藏项、Windows 联接、入口越界 | 自动回归通过 |
| 自定义 HTML 入口、相对资源、未知路径 404 | 真实 Caddy 2.11.9 回归通过 |
| 首页接管、旧介绍页转向、取消全部恢复 | 真实 Caddy 回归通过 |
| 当前标记、旧标记拒绝、站外和其他项目跳转拒绝 | 自动回归通过 |
| 正常释放及强制结束父进程后 Job Object 自有子进程退出 | 自动回归通过 |
| 组件首次下载、固定 SHA-256 校验 | 工作区隔离目录实测通过；默认 AppData 首轮移动目录失败，需普通桌面启动复测 |
| Tailscale 私有命名管道与 daemon | 实测启动，管道归属检查通过 |
| 首次 Tailscale 登录 / Funnel / 公网 DNS / HTTPS | 等待用户完成授权，尚未通过 |
| WPF 窗口、中文、原生标题栏、黑红配色 | 真实窗口启动并用 UIAutomation / PrintWindow 观察；控制台完整视觉及交互待授权后验收 |
| 内嵌说明 | 包内固定 WebView2 进程启动，文档“认识 DemoMux”可访问；关闭后包内 WebView2 进程全部退出；断网场景待测 |
| 混合三项目真实公网同时发布 | 待授权后验收 |
| 账号切换、重新授权、暂停退出、真实第二账号 | 待验收；第二账号需要用户条件 |
| 慢页面、HTTPS 故障细分、取消重试 | 待补充回归和公网验收 |
| 剪贴板、缩放、100/125/150/200% DPI | 尚未逐项覆盖 |
| 默认浏览器、未连接 Tailscale 的外部网络 | 待验收 |
| 主窗关闭、强制结束、重启、自启检查 | 组件 Job 回归已过；完整 GUI 与强制结束待复测 |
| 干净系统、Windows 10、另一台 Windows 11 | 无设备条件，待验收 |
| 公共 Release、全部附件公开下载与 SHA-256 | 尚未发布 |

30 项本机回归通过（2026-11-08，Windows 11 26100 / .NET 10.0.14）。[回归日志](windows-v0.3.2-regression.log)。新增真实 HTTP 服务转发、停止条目不结束原服务、不影响其他独立入口、绝对资源和占用端口诊断。

问题及修复：

1. 原 Windows 0.1.0 只有单端口逻辑：拆为 Catalog、PreparedContent、PublicationSlots、Gateway、AccountSession、PublicVerifier，界面接入操作状态。
2. 重建网关会影响其他项目：使用 Caddy `/load` 原子重载；真实资源与首页回归通过。
3. Tailscale 状态目录报 Access denied：上游 Windows 目录逻辑同时重设 owner/group。使用独立 PrivateState，设置受保护用户/SYSTEM/管理员 DACL 并保留 owner/group；复测已进入真实登录。
4. 桌面 CopyFromScreen 返回全黑：使用针对自身窗口的 PrintWindow 获得截图；尚未将未观察的控制台效果记为通过。

WebView2：157.0.4341.42 x64，微软官方 CAB，SHA-256 `cedaf8f3d12202c8803bdbae727b01c1be34540b78e8f0a10c6b401c2a8be97d`，主可执行文件 Authenticode 签名 Valid / Microsoft Corporation。[官方分发要求](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution)。
