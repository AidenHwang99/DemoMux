# Windows Tailscale 1.104.5-demomux.1

基于 Tailscale 1.104.5，源码提交固定为 `73a48057aaf5f60e4f770b398e9b7af576b01dbe`，Go SDK 固定为 1.27.3。来源与 ZIP 校验值见 `source.json`；本地修复见 `atomicfile_windows.go`。

本机 `%LOCALAPPDATA%` 使用 NTFS EFS。原 Windows `ReplaceFileW` 合并加密元数据时失败；同目录的 EFS 移动还可能返回 `ERROR_NOT_SAME_DEVICE`。兼容分支仅在同目录、源文件已加密且目标同样加密并具有相同 DACL 时使用 Windows 移动回退；新文件要求目标目录也已加密。不会请求解密、放宽 DACL 或操作系统 Tailscale。

EFS 回退使用 `MOVEFILE_COPY_ALLOWED`，Windows 在需要时可能执行复制后删除，因此不能声称此回退在断电时具备普通同卷重命名的原子性。项目目录与偏好仍由应用单独保存。此边界已列入 Windows 试用版验收说明。

本机专项回归验证新建与替换后的内容、EFS 标记和 DACL。组件使用自有 Job Object 生命周期，不安装 Windows 服务，不创建开机启动项。

构建：在 Windows 使用 Go 1.27.3，运行 `pwsh -File DemoMuxWindows/build-tailscale.ps1`。源 ZIP、补丁、构建参数和依赖许可随组件存档。ZIP 使用固定时间戳及无压缩打包以便复现；应用仍在下载后校验整个 ZIP 和两个可执行文件。
