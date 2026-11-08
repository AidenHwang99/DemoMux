# DemoMux

DemoMux 是一个手动启停的桌面演示工具。macOS 版可把 HTML、静态项目文件夹和已启动的本机 HTTP 服务混合发布，通过 Tailscale Funnel 同时分享最多三个项目。

## 下载与平台版本

| 平台 | 源码与使用说明 | 下载 | 验证状态 |
| --- | --- | --- | --- |
| macOS 26 · Apple 芯片 | [源码](DemoMux/) · [首次使用](DemoMux/首次使用.md) · [0.3.2 更新说明](DemoMux/CHANGELOG.md) | [macOS v0.3.2](https://github.com/AidenHwang99/DemoMux/releases/tag/macos-v0.3.2) | 开发 Mac 已实测公网 HTML、HTTP 端口、失败回退与重试；自动回归通过。独立设备等边界见更新说明。 |
| Windows 10 22H2 / Windows 11 · x64 | [源码](DemoMuxWindows/) · [首次使用](DemoMuxWindows/首次使用.md) · [更新说明](DemoMuxWindows/CHANGELOG.md) | [Windows v0.3.2 试用版](https://github.com/AidenHwang99/DemoMux/releases/tag/windows-v0.3.2) | Windows 11 x64 实测三项目公网、账号暂停恢复、慢页面与退出清理，38 项回归及 EFS 专项通过。Windows 10 等待测项见验收记录。 |

两个平台独立发布，0.3.2 的项目发布、账号、界面层次与公网验证已同步到 Windows。全部版本记录见 [CHANGELOG](CHANGELOG.md)。

Windows 界面更新：[0.3.3 UI 预览版](https://github.com/AidenHwang99/DemoMux/releases/tag/windows-v0.3.3-preview.1)在独立分支 `windows/ui-polish` 延续 Mac 的黑红配色和布局，统一主界面、引导、账号、发布与输入弹窗，增加清晰的操作状态和键盘入口。0.3.2 下载继续保留；见 [UI 验收记录](docs/verification/windows-v0.3.3-ui.md)。

Windows 0.3.2 在独立分支 `windows/0.3.2` 实施，以 `macos-v0.3.2` 为固定基线；包含 HTML/文件夹/端口项目、三名额、私有账号管理、公共 DNS/HTTPS 验证、随包 WebView2。当前覆盖与待验收项见 [Windows 验收记录](docs/verification/windows-v0.3.2.md)、[平台对照](docs/windows-platform-parity.md) 和 [Windows 更新说明](DemoMuxWindows/CHANGELOG.md)。

macOS ZIP 内含 `DemoMux.app`，使用临时签名，未经 Apple 公证。Windows ZIP 是未签名的 .NET 自包含包，解压整个 ZIP 后打开 `DemoMux.exe`。均为试用版，首次运行请参阅对应平台说明并校验 ZIP 的 SHA-256。

## macOS 0.3.2

- 同时发布最多三个项目：单文件 HTML、带资源的项目文件夹、已经运行的本机 HTTP 端口。
- 使用同一域名的 `/p/项目标识/` 路径，或为依赖根路径的项目选择独立 HTTPS 入口。
- 显示当前 Tailscale 账号与所属网络，提供添加、切换、暂停、退出及 Funnel 引导。
- 首个项目接管公网首页；使用说明嵌在 App 内，停止服务后仍能阅读。
- 公网验证不再等待整页下载，区分超时、握手和证书错误，修复重试旧提示残留。
- 组件按需下载并校验，使用私有配置；不添加开机启动项，退出时清理自有组件。

静态 HTML 无需 Node.js 或 Python。已有项目按自己的 README 安装运行环境并先启动，再把 HTTP 端口添加到 DemoMux。公网链接可被任何拿到链接的人访问，发布前应检查项目内容。

## 构建与开发验证

- **macOS**：在配有 macOS 26 SDK 的 Apple 芯片 Mac 上运行 `bash DemoMux/build.sh`。输出在 `outputs/DemoMux/`。
- **macOS 回归**：先启动 App 下载固定组件，再退出 App 释放 8081；运行 `bash DemoMux/Tests/run.sh`。详见 [测试说明](DemoMux/Tests/README.md) 与 [0.3.2 回归记录](docs/verification/macos-v0.3.2-regression.log)。Swift/Python 仅供开发验证，使用 App 不需要它们。
- **Windows**：使用 .NET 10 SDK 和 PowerShell 7，从仓库根目录运行 `pwsh -File DemoMuxWindows/build.ps1`，输出自包含 ZIP、SHA-256、中文说明、独立 HTML 指南和固定版本 WebView2。脚本验证官方运行时 CAB 校验值。`build.sh` 转发到此 Windows 构建流程。
- **Windows 回归**：`dotnet run --project DemoMuxWindows/Tests/DemoMux.Tests.csproj -c Release`，见 [测试说明](DemoMuxWindows/Tests/README.md)。

入口校验通过表示最近一次 HTTPS 与路由标记验证通过。页面资源、交互及访客网络仍需实际验收；网络或代理异常可能导致间歇连接中断。

## 组件下载

macOS 版按固定版本与 SHA-256 下载 Caddy 2.11.9、Tailscale 1.104.5，无需安装 Homebrew，也不使用系统 Tailscale 的登录状态。

Windows 应用从 [Tailscale 1.104.5-demomux.1 组件 Release](https://github.com/AidenHwang99/DemoMux/releases/tag/windows-components-v1.104.5-demomux.1) 按需下载命令行组件，并依据 `DemoMuxWindows/dependencies.json` 校验 ZIP 与可执行文件。此构建仅补齐 Windows EFS 状态写入兼容，仍基于固定 Tailscale 1.104.5 源码；见 [来源、许可和边界](DemoMuxWindows/Components/README.md)。使用 Go 1.27.3 和 `pwsh -File DemoMuxWindows/build-tailscale.ps1` 可复现。
