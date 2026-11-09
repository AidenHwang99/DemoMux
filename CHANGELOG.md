# 版本记录

平台版本独立维护，以各平台对应版本说明与验收记录为准。

## macOS 0.3.3 · 2026-11-09

[下载](https://github.com/AidenHwang99/DemoMux/releases/tag/macos-v0.3.3) · [验收记录](docs/verification/macos-v0.3.3.md)

[完整更新与平台对照](DemoMux/CHANGELOG.md) · [Windows 同步映射](DemoMux/WINDOWS-PARITY.md)。同步 Windows Preview 4：五步真实状态与账号门禁、2/4/8/15 秒可取消重试、原生持续流光、共享 SVG、固定五槽操作与响应式底栏。保留既有三项目路由和账号数据，Apple 芯片/macOS 26 临时签名试用包。

## Windows 0.3.3 Preview 2 · 2026-11-09

[下载](https://github.com/AidenHwang99/DemoMux/releases/tag/windows-v0.3.3-preview.2)。公网自动重试、倒计时、停止／恢复校验；原生连续流光、账号连接与恢复入口、身份同步校验。补齐 Tailscale EFS 替换错误，48 项回归及 EFS 专项通过。

## Windows 0.3.3 UI 预览版 · 2026-11-08

[下载](https://github.com/AidenHwang99/DemoMux/releases/tag/windows-v0.3.3-preview.1)。按 Mac 主界面参考图统一 Windows 原生界面：黑红色板、柔和背景、紧凑项目卡、主次按钮、深色菜单与弹窗、步骤和错误反馈；补齐输入校验、取消和复制反馈，以及项目 ID 关联的快速预览。保留 0.3.2 与全部 macOS 版本。见 [UI 验收](docs/verification/windows-v0.3.3-ui.md)。

## Windows 0.3.2 · 2026-11-08

[Windows 试用版](https://github.com/AidenHwang99/DemoMux/releases/tag/windows-v0.3.2)。独立分支对齐 Mac 项目发布、账号、界面层次和响应头公网验证；随包固定 WebView2，自包含 .NET。Windows 11 真实三项目公网、慢页面、取消恢复及清理通过，38 项回归与 EFS 专项通过。未覆盖场景见 [Windows 更新说明](DemoMuxWindows/CHANGELOG.md) 与 [实机验收记录](docs/verification/windows-v0.3.2.md)。

## macOS 0.3.2 · 2026-11-07

[下载](https://github.com/AidenHwang99/DemoMux/releases/tag/macos-v0.3.2) · [完整更新、升级说明与验收边界](DemoMux/CHANGELOG.md)

- 汇总从公开 0.1.0 以来的本地开发：最多三项目并发分享、HTML 与项目文件夹导入、本机 HTTP 端口、路径和独立入口、账号管理、首页接管、App 内说明及进程保护。
- 修复大页面公网验证误报、错误分类不准确、重试旧提示残留和项目/账号状态混杂。
- 增加慢速响应、取消、重试身份查询时序、路由、账号和进程清理回归。
- 仍为 Apple 芯片、macOS 26 试用包，临时签名，未经 Apple 公证；未覆盖的设备与网络场景在完整说明中列明。

## macOS 0.1.0 · 2026-11-06

[历史下载](https://github.com/AidenHwang99/DemoMux/releases/tag/macos-v0.1.0)

初始试用版：本机端口预览和切换、按需组件、Tailscale Funnel 公网分享。

## Windows 0.1.0 · 2026-11-06

[下载](https://github.com/AidenHwang99/DemoMux/releases/tag/windows-v0.1.0)

Windows 10 22H2 / Windows 11 x64 初始试用包，.NET 自包含发布；真实 Windows GUI、登录、Funnel 和退出清理仍待设备验收。

## Windows Tailscale 组件 1.104.5 · 2026-11-06

[组件下载](https://github.com/AidenHwang99/DemoMux/releases/tag/windows-components-v1.104.5)

按需下载所用的固定版本 Tailscale 命令行组件，独立于应用版本发布。
