# Windows 0.3.6 正式交付校验

2026-11-10 17:58:10（北京时间）公开，标签 `windows-v0.3.6`，非草稿、非预发布，GitHub Latest 返回该标签。

- [正式安装包](https://github.com/AidenHwang99/DemoMux/releases/download/windows-v0.3.6/DemoMux-Windows-0.3.6-Setup.exe)
- [Release](https://github.com/AidenHwang99/DemoMux/releases/tag/windows-v0.3.6)
- [成功发布流水线](https://github.com/AidenHwang99/DemoMux/actions/runs/42793901862)
- 发行源码：`4586e97426f1a27fd66abfb1905e2cd0e6253057`。发布后的 README 链接提交为 `623b471`，不替换已公开附件。

| 附件 | 字节数 | SHA-256 |
| --- | ---: | --- |
| DemoMux-Windows-0.3.6-Setup.exe | 408114157 | `4b6f4d5c78d8672f9e9ca14dae6c0b7519c338d8d398598be57f581e4f4f3905` |
| DemoMux-Windows-x64.zip | 423206232 | `d568854fb65db48c497b322c04b2c8f3d3eb7b1aabf9251826899752464c22b8` |

## 验证结果

- 本机 Windows 10 22H2 与云端 10.0.26100 环境各通过 208 项回归，平台图标写入边界 5 项通过。
- 本机隔离旧版升级、修复、禁止降级及卸载重装检查通过；云端快速安装器检查 17 项通过。
- 云端使用最终生产 EXE 本身，在独立目录实际安装、逐文件验证、修复、卸载、重装，共 17 项通过；标准注册、桌面与开始菜单快捷方式正确。仅在无既有 DemoMux 数据和注册的临时 CI 主机执行生产身份测试。
- 英文 Windows 上 WScript.Shell 将中文路径返回为问号，导致首次流水线停止。改用 IShellLinkW 读取真实 Unicode 路径后，严格目标路径校验通过；没有跳过快捷方式检查。
- 发布流程重新下载所有附件逐一比对哈希后，将完整安装包链接写入 README 平台表格与 Windows 使用指引，并关闭一次性发布开关。
- 本机再次独立下载全部 10 个公开附件，分别核对 GitHub 资产摘要、SHA256SUMS.txt 及 EXE／ZIP 旁的校验文件。ZIP 安全解压后，677 个清单文件的 SHA-256 及文件总数全部匹配；未包含测试程序。
- 解压应用 ProductVersion 为 `0.3.6+4586e97426f1a27fd66abfb1905e2cd0e6253057`。包内 Caddy 与 Tailscale ZIP 校验值匹配依赖锁。

本机证据位于 `outputs/verification/windows-v0.3.6-public-download.log`、`windows-v0.3.6-public-verification.json`；下载缓存为 `outputs/DemoMuxWindows/windows-v0.3.6-public-download`，用户可直接运行 `outputs/DemoMuxWindows/DemoMux-Windows-0.3.6-Setup.exe`。

## 覆盖边界

保留 [功能验收记录](windows-v0.3.6.md) 中的未覆盖项：完整视觉及点击受桌面工具超时限制；物理 Windows 11、多屏 DPI、磁盘不足与中断回滚、真实公网持续分享和另一台机器的现场网络未补测。安装包仍未签名。离线组件测试使用不可达下载地址，没有修改整机网络设置。

正在使用的 0.3.5 便携主副本、真实账号及原始项目未被测试覆盖安装或卸载。合并过程中保留 Mac 0.3.6 远端提交，Mac 应用与品牌源没有本任务新增差异。
