# DemoMux

<img src="DemoMuxWindows/Assets/AppIcon.png" width="88" height="88" alt="DemoMux 应用图标">

DemoMux 是一个手动启停的桌面演示工具，提供 macOS 和 Windows 实现。可把 HTML、静态项目文件夹和已启动的本机 HTTP 服务混合发布，通过 Tailscale Funnel 同时分享最多三个项目。

## 下载与平台版本

| 平台 | 源码与使用说明 | 下载 | 验证状态 |
| --- | --- | --- | --- |
| macOS 26 · Apple 芯片 | [源码](DemoMux/) · [首次使用](DemoMux/首次使用.md) · [0.3.5 更新说明](DemoMux/CHANGELOG.md) | [macOS v0.3.5](https://github.com/AidenHwang99/DemoMux/releases/tag/macos-v0.3.5) | 0.3.4 全回归、响应式布局及隔离运行 App 自卸载通过；真实公网继承验证范围见 [验收记录](docs/verification/macos-v0.3.4.md)。 |
| Windows 10 22H2 / Windows 11 · x64 | [源码](DemoMuxWindows/) · [首次使用](DemoMuxWindows/首次使用.md) · [更新说明](DemoMuxWindows/CHANGELOG.md) | [Windows 0.3.5](https://github.com/AidenHwang99/DemoMux/releases/tag/windows-v0.3.5) · [历史 Preview 4](https://github.com/AidenHwang99/DemoMux/releases/tag/windows-v0.3.3-preview.4) | 0.3.5 正式发行构建；202 项 Windows 回归、5 项图标边界及隔离原生托盘检查通过。完整已验证范围与未覆盖项见 [发行记录](docs/verification/windows-v0.3.5.md)，附件及 SHA-256 由发布流程生成并回下载核对。 |

两个平台独立发布。Mac 0.3.3 同步 Windows Preview 4 的五步门禁、自动重试、共享 SVG 和固定操作栏；平台专属组件与原生控件分别保留。全部版本记录见 [CHANGELOG](CHANGELOG.md)。

Windows 图标操作栏：[Preview 4](https://github.com/AidenHwang99/DemoMux/releases/tag/windows-v0.3.3-preview.4)提供固定位置的启动／停止和发布／取消发布图标切换；共享 SVG 与 Mac 同步规范见 [Shared/Icons](Shared/Icons/README.md)。

Windows 菜单修复：[Preview 3](https://github.com/AidenHwang99/DemoMux/releases/tag/windows-v0.3.3-preview.3)修复菜单遮挡，统一项目操作栏与导入入口。见 [验收记录](docs/verification/windows-v0.3.3-layout.md)。

Windows 公网重试修复：[0.3.3 Preview 2](https://github.com/AidenHwang99/DemoMux/releases/tag/windows-v0.3.3-preview.2)增加公网自动重试、次数与倒计时、停止／恢复校验，并修复 EFS 状态写入的另一错误分支。见 [重试验收](docs/verification/windows-v0.3.3-retry.md)。

Windows 界面更新：[0.3.3 UI 预览版](https://github.com/AidenHwang99/DemoMux/releases/tag/windows-v0.3.3-preview.1)在独立分支 `windows/ui-polish` 延续 Mac 的黑红配色和布局，统一主界面、引导、账号、发布与输入弹窗，增加清晰的操作状态和键盘入口。0.3.2 下载继续保留；见 [UI 验收记录](docs/verification/windows-v0.3.3-ui.md)。

Windows 0.3.2 在独立分支 `windows/0.3.2` 实施，以 `macos-v0.3.2` 为固定基线；包含 HTML/文件夹/端口项目、三名额、私有账号管理、公共 DNS/HTTPS 验证、随包 WebView2。当前覆盖与待验收项见 [Windows 验收记录](docs/verification/windows-v0.3.2.md)、[平台对照](docs/windows-platform-parity.md) 和 [Windows 更新说明](DemoMuxWindows/CHANGELOG.md)。

macOS ZIP 内含 `DemoMux.app`，使用临时签名，未经 Apple 公证。Windows ZIP 是未签名的 .NET 自包含发行包，解压整个 ZIP 后打开 `DemoMux.exe`。首次运行请参阅对应平台说明并校验 ZIP 的 SHA-256；发行渠道不等于全部设备与网络场景均已覆盖。

## Windows 0.3.5

Windows 最新源码已整合到默认 [`main`](https://github.com/AidenHwang99/DemoMux/tree/main)，保留 Windows EFS、私有账号、Job Object 和网络校验修复，并完善发布与桌面使用体验。当前版本为 `0.3.5`，发行标签 `windows-v0.3.5`，不标记为预发布；实际公开附件和交付校验以该 Release 为准。

自 2026-11-10 起，两台主机统一从最新 `origin/main` 继续开发：Mac 与 Windows 当前版本均为 `0.3.5`，独立构建和发布。旧 `windows/macos-0.3.5-parity` 分支保留作历史，不再作为最新源码入口。[跨主机协作说明](docs/development.md)列出同步步骤、平台目录和图标来源；[主分支整合记录](docs/verification/windows-main-integration-2026-11-10.md)保留验证范围。

- **发布方式下拉框**：直接选择「按项目名发布」或「使用独立入口」。本机端口默认推荐独立入口；路径模式先检测本机服务是否支持目标子路径，404、权限、超时等会在发布前解释，不提前占用名额。
- **项目名称与链接**：提取名称中的英文和数字，例如 `teleConnect_AI主动模式原型_155` → `teleconnect-ai-155`。未发布的自动路径随改名更新；自定义及已发布路径保持，可按项目名重新生成并明确确认旧链接影响。不修改原项目的基础路径配置。
- **系统托盘**：在「设置 → 关闭主窗口时」选择「隐藏到系统托盘（继续分享）」。默认仍是关闭即退出；启用后点关闭只隐藏，托盘点击恢复，右键可打开设置或明确退出。图标可能位于任务栏 ^ 中，位置由 Windows 管理；没有开机自启。
- **设置布局**：偏好、组件、数据目录、支持分别显示；操作按钮右侧对齐，完成固定在右下角，窄窗自动换行。支持窗口偏好、组件／存储信息和受限诊断。
- **快捷手动卸载**：设置固定底栏提供卸载说明、保留／清除选项及目录定位，然后真正退出。由用户在 Explorer 手动删除，不自动删除主副本、账号或原始项目。
- **新应用图标**：使用用户提供的红黑标志，程序、窗口和托盘统一；[原始图案与转换说明](DemoMuxWindows/Assets/README.md)随源码保留，Mac 资源不变。

本轮在 Windows 10 22H2 主机执行自动回归、真实本机 HTTP／Caddy 夹具、WPF 生命周期与布局检查，以及原生托盘注册／隐藏／恢复／退出检查。完整截图、物理多 DPI、真实公网持续分享、托盘菜单人工点击、Explorer 重启、注销和最终生产包真实身份桌面启动仍未完成，不把隔离测试记作全部计划验收通过。详细证据见 [发行记录](docs/verification/windows-v0.3.5.md)，[开发阶段记录](docs/verification/windows-v0.3.5-preview.1.md)继续保留。

## macOS 0.3.5

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
- **Windows**：使用 .NET 10 SDK 和 PowerShell 7，从仓库根目录运行 `pwsh -File DemoMuxWindows/build.ps1`，输出自包含 ZIP、SHA-256、中文说明、独立 HTML 指南和固定版本 WebView2。脚本验证官方运行时 CAB 校验值。`build.sh` 转发到此 Windows 构建流程。
- **Windows 回归**：`dotnet run --project DemoMuxWindows/Tests/DemoMux.Tests.csproj -c Release`，见 [测试说明](DemoMuxWindows/Tests/README.md)。
- **Windows 干净环境／托盘**：回归命令末尾增加 `-- --fresh-data` 使用全新隔离数据；`-- --tray-smoke` 检查有 Explorer 的桌面原生托盘，`-- --tray-ui` 打开隔离托盘窗口。不读取真实授权；不用于宣称公网验证通过。
- **Windows 独立说明**：构建会在 ZIP 旁同时导出 Windows 专用 HTML、首次使用、图标、更新及验收记录。也可仅运行 `pwsh -File DemoMuxWindows/export-docs.ps1` 同步说明文件，不构建或发布应用。

入口校验通过表示最近一次 HTTPS 与路由标记验证通过。页面资源、交互及访客网络仍需实际验收；网络或代理异常可能导致间歇连接中断。

## 组件下载

macOS 版按固定版本与 SHA-256 下载 Caddy 2.11.9、Tailscale 1.104.5，无需安装 Homebrew，也不使用系统 Tailscale 的登录状态。

Windows 应用从 [Tailscale 1.104.5-demomux.2 组件 Release](https://github.com/AidenHwang99/DemoMux/releases/tag/windows-components-v1.104.5-demomux.2) 按需下载命令行组件，并依据 `DemoMuxWindows/dependencies.json` 校验 ZIP 与可执行文件。此构建仅补齐 Windows EFS 状态写入兼容，仍基于固定 Tailscale 1.104.5 源码；见 [来源、许可和边界](DemoMuxWindows/Components/README.md)。使用 Go 1.27.3 和 `pwsh -File DemoMuxWindows/build-tailscale.ps1` 可复现。
