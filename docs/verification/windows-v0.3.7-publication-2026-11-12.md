# Windows 0.3.7 正式交付校验

2026-11-12 10:02:39（北京时间）正式发布。标签为 `windows-v0.3.7`，发行源码为 `b4bd25432dbd79127681055c1c5af5d69f68525d`。

[完整 EXE](https://github.com/AidenHwang99/DemoMux/releases/download/windows-v0.3.7/DemoMux-Windows-0.3.7-Setup.exe) · [Release](https://github.com/AidenHwang99/DemoMux/releases/tag/windows-v0.3.7) · [发布 CI](https://github.com/AidenHwang99/DemoMux/actions/runs/43137604273)

| 附件 | 字节数 | SHA-256 |
| --- | ---: | --- |
| DemoMux-Windows-0.3.7-Setup.exe | 408541726 | `e8a6f7a17c5e4ce701ef82f4ca58b59ced05c6d4896bd132605410777a53af5b` |
| DemoMux-Windows-x64.zip | 423644430 | `90b02464dd295f9800f40dffb299d0b041ece30e441bd534f021e62bacdfd216` |

- 本机和云端各通过 225 项 Windows 回归，5 项平台图标边界检查通过。本机隔离安装 19 项通过，包括从正式 0.3.6 升级。
- 云端实际安装最终生产 EXE，17 项验证通过，包括全包哈希、快捷方式、修复、卸载和重装。
- CI 和本机各自回下载全部 10 个公开附件并校验 SHA-256。本机解压 ZIP 后核对了 679 个清单文件和构建版本。
- 发布脚本提交 README 直链并关闭发布开关。本机已同步该提交。

真实 8080 项目及隔离网关的浏览器验证见 [功能验收](windows-v0.3.7.md)。真实 Funnel 公网访问待实机验证。下载与哈希证据位于本机 `outputs/verification/windows-v0.3.7-public-download.log`，已验证的本机安装器位于 `outputs/DemoMuxWindows/DemoMux-Windows-0.3.7-Setup.exe`。
