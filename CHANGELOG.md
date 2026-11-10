# 版本记录

平台版本独立维护，以各平台对应版本说明与验收记录为准。

## Windows 0.3.6 · 2026-11-10

新增完整 EXE 安装包、内置离线组件、标准升级及卸载；默认保留用户数据。下载与实际发布状态见 [README](README.md#windows)，验证范围见 [验收记录](docs/verification/windows-v0.3.6.md)。

## Windows 0.3.5 · 2026-11-10

[正式 Release](https://github.com/AidenHwang99/DemoMux/releases/tag/windows-v0.3.5) · [发行记录](docs/verification/windows-v0.3.5.md)。正式版本号 0.3.5，保留本机确认的功能、Windows 修复和玫红图标。主分支统一源码；发行流程区分正式／预发布，上传完整包、独立说明与校验值，公开后回下载核对附件，再标为 Latest。202 项 Windows 回归、5 项平台图标边界测试及 4 项本机隔离原生托盘检查通过；已知限制如实随包说明，正式发行不宣称所有原计划验收项通过。

## Windows 0.3.5 Preview 1 · 开发整合历史

该阶段已整合到主分支，预览标签未单独发布，后续由正式 0.3.5 接续。[当前源码](https://github.com/AidenHwang99/DemoMux/tree/main) · [验收记录](docs/verification/windows-v0.3.5-preview.1.md)。增加发布方式下拉框、本机路径兼容性检测、Mac 规则名称标识、可配置系统托盘、设置与按钮布局、快捷手动卸载，以及用户提供的新应用图标。202 项自动回归及隔离原生托盘检查通过，完整视觉与真实公网等缺项仍单独保留；不改写历史版本的验证结论。

主分支作为两台主机继续开发的共同基线。保留 Mac 已有图标与说明资源，Windows 使用本机确认的玫红图标；图标生成默认按平台隔离，详见[协作说明](docs/development.md)。

## 应用图标更新 · 2026-11-09（未发布）

macOS 与 Windows 采用黑红金属切面 D / 镂空播放箭头图标。新增 [品牌原图与生成脚本](Shared/Brand/README.md)。当时分别生成含 Retina 尺寸的 ICNS 与七尺寸透明 ICO；后续 Windows 以本机确认的配色和九尺寸 ICO 为准。已核验 Mac 资源结构并完成 macOS 构建及签名检查；本段保留当次记录，Windows 后续验证见上节。

应用内使用说明、独立 HTML 指南和公网介绍页的共用源文件同步替换页头旧终端符号，并添加新 favicon；图片内嵌，保持离线可读。

按原终端图标的玫红色调调整 D 图标，降低此前猩红色的强烈感；应用图标、指南页头和 favicon 共用调整后的原图。

## macOS 0.3.5 · 2026-11-09

[下载](https://github.com/AidenHwang99/DemoMux/releases/tag/macos-v0.3.5) · [验收记录](docs/verification/macos-v0.3.5.md)。修复数据目录在 Finder 分栏中不可见，增加路径显示 / 复制、文件说明和失败反馈。

## macOS 0.3.4 · 2026-11-09

[下载](https://github.com/AidenHwang99/DemoMux/releases/tag/macos-v0.3.4-build10) · [窗口验收](docs/verification/macos-v0.3.4.md)。引导页横向五步条 / 两栏切换，主界面宽屏双栏与窄卡操作换行，居中限宽和独立底栏；新增设置、偏好、诊断与可保留数据的卸载。全回归及隔离运行 App 自卸载通过。

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
