# DemoMux

DemoMux 是一个手动启停的桌面演示工具。macOS 版可把 HTML、静态项目文件夹和已启动的本机 HTTP 服务混合发布，通过 Tailscale Funnel 同时分享最多三个项目。

## 下载与平台版本

| 平台 | 源码与使用说明 | 下载 | 验证状态 |
| --- | --- | --- | --- |
| macOS 26 · Apple 芯片 | [源码](DemoMux/) · [首次使用](DemoMux/首次使用.md) · [0.3.2 更新说明](DemoMux/CHANGELOG.md) | [macOS v0.3.2](https://github.com/AidenHwang99/DemoMux/releases/tag/macos-v0.3.2) | 开发 Mac 已实测公网 HTML、HTTP 端口、失败回退与重试；自动回归通过。独立设备等边界见更新说明。 |
| Windows 10 22H2 / Windows 11 · x64 | [源码](DemoMuxWindows/) · [首次使用](DemoMuxWindows/首次使用.md) | [Windows v0.1.0](https://github.com/AidenHwang99/DemoMux/releases/tag/windows-v0.1.0) | 已交叉构建并校验压缩包。两种 Windows 系统的运行与公网访问待验收。 |

两个平台独立发布；本次 macOS 0.3.2 功能没有同步到 Windows 0.1.0。全部版本记录见 [CHANGELOG](CHANGELOG.md)。

Windows 0.3.2 正在独立分支 `windows/0.3.2` 开发验收：以 `macos-v0.3.2` 为基线，加入 HTML/文件夹/端口项目、三名额发布、私有账号管理、公共 DNS/HTTPS 验证与包内固定 WebView2。尚未替换公开 Windows 0.1.0 下载；当前覆盖与待验收项见 [Windows 验收记录](docs/verification/windows-v0.3.2.md) 和 [Windows 更新说明](DemoMuxWindows/CHANGELOG.md)。

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
- **Windows**：使用 .NET 10 SDK 从仓库根目录运行 `powershell -File DemoMuxWindows/build.ps1`，输出自包含 ZIP、SHA-256、中文说明、独立 HTML 指南和固定版本 WebView2。脚本验证官方运行时 CAB 校验值。原 `build.sh` 为 0.1.0 旧构建入口，不能生成 0.3.2 完整分发包。
- **Windows 回归**：`dotnet run --project DemoMuxWindows/Tests/DemoMux.Tests.csproj -c Release`，见 [测试说明](DemoMuxWindows/Tests/README.md)。

入口校验通过表示最近一次 HTTPS 与路由标记验证通过。页面资源、交互及访客网络仍需实际验收；网络或代理异常可能导致间歇连接中断。

## 组件下载

macOS 版按固定版本与 SHA-256 下载 Caddy 2.11.9、Tailscale 1.104.5，无需安装 Homebrew，也不使用系统 Tailscale 的登录状态。

Windows 应用从 [Tailscale 1.104.5 组件 Release](https://github.com/AidenHwang99/DemoMux/releases/tag/windows-components-v1.104.5) 按需下载命令行组件，并依据 `DemoMuxWindows/dependencies.json` 校验 SHA-256。此 Release 专供应用下载依赖。
