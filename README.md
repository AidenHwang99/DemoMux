# DemoMux

DemoMux 是一个桌面演示工具。它可以通过同一个本机预览地址切换正在运行的项目，并在需要时通过 Tailscale Funnel 生成公网链接。

## 应用下载与源码

| 平台 | 源码与首次使用说明 | ZIP 与 SHA-256 | 试用状态 |
| --- | --- | --- | --- |
| macOS 26 · Apple 芯片 | [源码](DemoMux/) · [首次使用](DemoMux/首次使用.md) | [macOS v0.1.0](https://github.com/AidenHwang99/DemoMux/releases/tag/macos-v0.1.0) | 已在开发 Mac 验证本机预览与项目切换。独立设备及公网访问待验收。 |
| Windows 10 22H2 / Windows 11 · x64 | [源码](DemoMuxWindows/) · [首次使用](DemoMuxWindows/首次使用.md) | [Windows v0.1.0](https://github.com/AidenHwang99/DemoMux/releases/tag/windows-v0.1.0) | 已交叉构建并校验压缩包。两种 Windows 系统的运行与公网访问待验收。 |

macOS ZIP 内含 `DemoMux.app`，使用临时签名，未经 Apple 公证。Windows ZIP 是未签名的 .NET 自包含包，解压整个 ZIP 后打开 `DemoMux.exe`。首次运行请参阅对应平台说明。

## 构建

- **macOS** 在配有 macOS 26 SDK 的 Apple 芯片 Mac 上运行 `bash DemoMux/build.sh`。
- **Windows** 使用 .NET 10 SDK 从仓库根目录运行 `dotnet publish DemoMuxWindows/DemoMux.csproj -c Release -r win-x64 --self-contained true`。`DemoMuxWindows/build.sh` 用于生成分发 ZIP 和 SHA-256，接收 Tailscale 组件 ZIP 及其固定的公开下载地址。

## Windows 运行依赖

Windows 应用从 [Tailscale 1.104.5 组件 Release](https://github.com/AidenHwang99/DemoMux/releases/tag/windows-components-v1.104.5) 按需下载命令行组件，并依据 `DemoMuxWindows/dependencies.json` 校验 SHA-256。此 Release 专供应用下载依赖。
