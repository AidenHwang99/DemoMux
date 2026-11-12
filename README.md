# DemoMux

<img src="DemoMuxWindows/Assets/AppIcon.png" width="88" height="88" alt="DemoMux 应用图标">

DemoMux 是一个手动启停的桌面演示工具，提供 macOS 和 Windows 实现。可把 HTML、静态项目文件夹和已启动的本机 HTTP 服务混合发布，通过 Tailscale Funnel 同时分享最多三个项目。

## 下载与平台版本

| 平台 | 源码与使用说明 | 下载 | 验证状态 |
| --- | --- | --- | --- |
| macOS 26 及以上 · Apple 芯片 | [源码](DemoMux/) · [首次使用](DemoMux/首次使用.md) · [0.3.6 更新说明](DemoMux/CHANGELOG.md) | [macOS v0.3.6](https://github.com/AidenHwang99/DemoMux/releases/tag/macos-v0.3.6) | macOS 27.2.1 最终 ZIP 启动、三项目真实 HTTPS 与浏览器访问、退出清理及自动回归通过；[完整范围与未覆盖项](docs/verification/macos-v0.3.6.md)。 |
| Windows 10 22H2 / Windows 11 · x64 | [源码](DemoMuxWindows/) · [首次使用](DemoMuxWindows/首次使用.md) · [更新说明](DemoMuxWindows/CHANGELOG.md) | **[安装包（推荐）](https://github.com/AidenHwang99/DemoMux/releases/download/windows-v0.3.7/DemoMux-Windows-0.3.7-Setup.exe)** · [便携 ZIP](https://github.com/AidenHwang99/DemoMux/releases/download/windows-v0.3.7/DemoMux-Windows-x64.zip) | Windows 0.3.7；[验证范围与未覆盖项](docs/verification/windows-v0.3.7.md)。 |

两个平台独立发布。Mac 0.3.3 同步 Windows Preview 4 的五步门禁、自动重试、共享 SVG 和固定操作栏；平台专属组件与原生控件分别保留。全部版本记录见 [CHANGELOG](CHANGELOG.md)。

Windows 图标操作栏：[Preview 4](https://github.com/AidenHwang99/DemoMux/releases/tag/windows-v0.3.3-preview.4)提供固定位置的启动／停止和发布／取消发布图标切换；共享 SVG 与 Mac 同步规范见 [Shared/Icons](Shared/Icons/README.md)。

Windows 菜单修复：[Preview 3](https://github.com/AidenHwang99/DemoMux/releases/tag/windows-v0.3.3-preview.3)修复菜单遮挡，统一项目操作栏与导入入口。见 [验收记录](docs/verification/windows-v0.3.3-layout.md)。

Windows 公网重试修复：[0.3.3 Preview 2](https://github.com/AidenHwang99/DemoMux/releases/tag/windows-v0.3.3-preview.2)增加公网自动重试、次数与倒计时、停止／恢复校验，并修复 EFS 状态写入的另一错误分支。见 [重试验收](docs/verification/windows-v0.3.3-retry.md)。

Windows 界面更新：[0.3.3 UI 预览版](https://github.com/AidenHwang99/DemoMux/releases/tag/windows-v0.3.3-preview.1)在独立分支 `windows/ui-polish` 延续 Mac 的黑红配色和布局，统一主界面、引导、账号、发布与输入弹窗，增加清晰的操作状态和键盘入口。0.3.2 下载继续保留；见 [UI 验收记录](docs/verification/windows-v0.3.3-ui.md)。

Windows 0.3.2 在独立分支 `windows/0.3.2` 实施，以 `macos-v0.3.2` 为固定基线；包含 HTML/文件夹/端口项目、三名额、私有账号管理、公共 DNS/HTTPS 验证、随包 WebView2。当前覆盖与待验收项见 [Windows 验收记录](docs/verification/windows-v0.3.2.md)、[平台对照](docs/windows-platform-parity.md) 和 [Windows 更新说明](DemoMuxWindows/CHANGELOG.md)。

macOS ZIP 内含 `DemoMux.app`，使用临时签名，未经 Apple 公证。Windows 提供完整 EXE 安装包和可选便携 ZIP，具体下载与使用步骤见下方 Windows 指引。当前未签名。首次运行请参阅对应平台说明并校验安装包或 ZIP 的 SHA-256；发行渠道不等于全部设备与网络场景均已覆盖。

## Windows

<!-- windows-release:start -->
Windows **0.3.7** 正式版：**[下载安装包（推荐）](https://github.com/AidenHwang99/DemoMux/releases/download/windows-v0.3.7/DemoMux-Windows-0.3.7-Setup.exe)** · [发行说明](https://github.com/AidenHwang99/DemoMux/releases/tag/windows-v0.3.7) · [SHA-256](https://github.com/AidenHwang99/DemoMux/releases/download/windows-v0.3.7/DemoMux-Windows-0.3.7-Setup.exe.sha256)。

下载一个 EXE，双击安装即可；自动管理安装位置、开始菜单和桌面快捷方式。内置 .NET、WebView2、Caddy 和专用 Tailscale，准备组件无需另行下载，登录和公网分享仍需联网。升级先从托盘退出旧版，然后运行新安装包；账号、项目列表和偏好保留。

可选：[便携 ZIP](https://github.com/AidenHwang99/DemoMux/releases/download/windows-v0.3.7/DemoMux-Windows-x64.zip)。便携版需完整解压；安装版可在设置或 Windows「已安装的应用」中卸载，默认保留用户数据和原始项目。说明与校验附件无需单独下载即可安装使用。

附件已公开下载并逐项校验。覆盖与限制见 [Windows 0.3.7 验收记录](docs/verification/windows-v0.3.7.md)。
<!-- windows-release:end -->

连接本机 HTTP 服务时，可输入端口或完整地址，如 `http://console.localhost:8080/`（Windows 0.3.7 起）。独立入口适用于从根路径加载资源的服务。按项目名发布会提示越出项目子路径的脚本或样式。

Mac 与 Windows 共同以最新 `origin/main` 为基线，独立构建和发布。旧 Windows 分支仅供追溯，协作流程见 [开发说明](docs/development.md)。

Windows 保留发布方式下拉框、英文项目路径建议、本机路径预检、托盘继续分享、设置自适应布局、私有账号、EFS 和 Job Object 修复。安装版新增标准卸载；便携版保留手动卸载说明。两种方式都保护原始项目文件。

## macOS 0.3.6

修复升级 macOS 27 后无法启动：系统要求由仅允许 26 改为最低 26，保留原数据和私有组件。本轮实测 macOS 27.2.1；未来版本允许启动不等于已验收。见 [更新说明](DemoMux/CHANGELOG.md) 和 [验收记录](docs/verification/macos-v0.3.6.md)。

### 0.3.5 修复

修复设置打开数据目录时 Finder 分栏内容位于屏幕外的问题，增加路径显示、复制及文件说明。见 [修复验收](docs/verification/macos-v0.3.5.md)。

### 0.3.4 功能

引导页和主界面随窗口宽度重排，限制宽屏阅读宽度，底栏独立占位。新增设置、窗口大小记忆、组件 / 存储和可保留数据的卸载；卸载先停止私有进程，再移到废纸篓。见 [窗口缩放验收](docs/verification/macos-v0.3.4.md)。保留 0.3.3 的五步门禁、重试与发布功能。

- 五步准备只标记真实通过的步骤；账号与设备身份未同步时，不进入公网校验。
- 网络故障按 2 / 4 / 8 / 15 秒重试，支持停止与恢复；证书、标记、跳转和身份错误停止等待处理。
- 共享 SVG、固定五槽图标操作栏、统一添加与更多菜单、响应式原生玻璃底栏。
- 同时发布最多三个项目：单文件 HTML、带资源的项目文件夹、已经运行的本机 HTTP 端口。
- 使用同一域名的 `/p/项目标识/` 路径，或为依赖根路径的项目选择独立 HTTPS 入口。
- 显示当前 Tailscale 账号与所属网络，提供添加、切换、暂停、退出及 Funnel 引导。
- 首个项目接管公网首页；使用说明嵌在 App 内，停止服务后仍能阅读。
- 公网验证不再等待整页下载，区分超时、握手和证书错误，修复重试旧提示残留。
- 组件按需下载并校验，使用私有配置；不添加开机启动项，退出时清理自有组件。

静态 HTML 无需 Node.js 或 Python。已有项目按自己的 README 安装运行环境并先启动，再把 HTTP 端口添加到 DemoMux。公网链接可被任何拿到链接的人访问，发布前应检查项目内容。

## 构建与开发验证

- **macOS**：在配有 macOS 26 SDK 的 Apple 芯片 Mac 上运行 `bash DemoMux/build.sh`。输出在 `outputs/DemoMux/`。
- **macOS 回归**：先启动 App 下载固定组件，再退出 App 释放 8081；运行 `bash DemoMux/Tests/run.sh`。详见 [测试说明](DemoMux/Tests/README.md) 与 [0.3.3 回归记录](docs/verification/macos-v0.3.3-regression.log)。Swift/Python 仅供开发验证，使用 App 不需要它们。
- **Windows**：使用 .NET 10 SDK 和 PowerShell 7，从仓库根目录运行 `pwsh -File DemoMuxWindows/build.ps1`，输出完整 EXE 安装包、便携 ZIP、SHA-256、中文说明、独立 HTML 指南和固定版本 WebView2。脚本验证官方运行时 CAB 校验值。`build.sh` 转发到此 Windows 构建流程。
- **Windows 回归**：`dotnet run --project DemoMuxWindows/Tests/DemoMux.Tests.csproj -c Release`，见 [测试说明](DemoMuxWindows/Tests/README.md)。
- **Windows 干净环境／托盘**：回归命令末尾增加 `-- --fresh-data` 使用全新隔离数据；`-- --tray-smoke` 检查有 Explorer 的桌面原生托盘，`-- --tray-ui` 打开隔离托盘窗口。不读取真实授权；不用于宣称公网验证通过。
- **Windows 独立说明**：构建会在 ZIP 旁同时导出 Windows 专用 HTML、首次使用、图标、更新及验收记录。也可仅运行 `pwsh -File DemoMuxWindows/export-docs.ps1` 同步说明文件，不构建或发布应用。

入口校验通过表示最近一次 HTTPS 与路由标记验证通过。页面资源、交互及访客网络仍需实际验收；网络或代理异常可能导致间歇连接中断。

## 组件下载

macOS 版按固定版本与 SHA-256 下载 Caddy 2.11.9、Tailscale 1.104.5，无需安装 Homebrew，也不使用系统 Tailscale 的登录状态。

Windows 完整发行包内置已锁定的组件；开发构建缺少内置组件时从 [Tailscale 1.104.5-demomux.2 组件 Release](https://github.com/AidenHwang99/DemoMux/releases/tag/windows-components-v1.104.5-demomux.2) 获取命令行组件，并依据 `DemoMuxWindows/dependencies.json` 校验 ZIP 与可执行文件。此构建仅补齐 Windows EFS 状态写入兼容，仍基于固定 Tailscale 1.104.5 源码；见 [来源、许可和边界](DemoMuxWindows/Components/README.md)。使用 Go 1.27.3 和 `pwsh -File DemoMuxWindows/build-tailscale.ps1` 可复现。
