# Windows 安装器

Inno Setup 编译器版本、下载地址与 SHA-256 固定在 `../installer-tool.json`。构建脚本校验后安装到仓库忽略的 `outputs/tooling`；生成当前用户范围的 x64 安装器，程序目录与用户数据目录分离，不添加系统服务或开机启动。

`ChineseSimplified.isl` 来自 jrsoftware/issrc 的 `is-6_7_4` 标签下 `Files/Languages/Unofficial/ChineseSimplified.isl`，Git blob `d6a11c4490de07dad443ade668289fc954dfa1ed`，保留维护者署名。编译器与翻译许可遵循 Inno Setup 上游许可。

默认程序目录 `%LOCALAPPDATA%\Programs\DemoMux`，稳定注册身份 `DemoMux.Windows`。安装器只追踪包文件及自身快捷方式，卸载不递归清理数据目录。旧版便携应用正在运行时提示退出，新版通过安装互斥量保护升级／卸载；不会强制结束进程。

隔离验收通过 `Tests/InstallerSmoke.ps1` 生成专用 AppId、中文带空格安装目录及测试快捷方式，绝不卸载真实安装。`-PreviousPayload` 可指定已校验的旧版解压目录检查升级及禁止降级。测试完成默认卸载，仅 `-KeepInstalled` 为桌面验收保留隔离副本。

每次公开发布必须上传 EXE、ZIP 与校验文件；回下载全部附件后运行 `update-release-readme.ps1`，随后提交 README 和关闭后的 release.json。若 main 已前进则停止文档推送，人工合并最新源码后补齐，不能覆盖他人提交。
