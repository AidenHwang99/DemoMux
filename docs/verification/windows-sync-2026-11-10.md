# Windows 仓库与说明同步复核

复核日期：2026-11-10。版本：0.3.5-preview.1，开发中，尚未发布。

当前结论：用户随后明确要求把本机最新 Windows 版本作为仓库共同基线，现已整合到 main，详见[整合记录](windows-main-integration-2026-11-10.md)。以下“尚未合并”等描述保留的是本次整合前的复核状态，不代表当前分支关系。

## 分支与本地状态

- 复核开始时工作区干净。重新 fetch origin 后，本地 Windows 开发分支与 origin/windows/macos-0.3.5-parity 均为 32688285fc6cb51d253258c7517bd3186bd56747，没有领先或落后提交。
- 复核开始、Windows 基线为 3268828 时，默认 main 为 0aa2eb77e27a0086b0d4191a853c2892d07026fe，两分支分别有 2／13 个独立提交。main 新增共享品牌资产并更新 Mac 和 Windows 图标；Windows 分支保留本轮独立图标处理、文档和功能实现，两边图标文件不相同。本次文档修正提交在该 Windows 基线上继续追加。
- main 的 README 尚不包含本轮 Windows 开发功能。此次保留各分支，不擅自覆盖共享图标或 Mac 资源，不把已同步的开发分支描述为已合并 main。后续合并需要同时核对二进制图标冲突、共享品牌来源和文档链接。

## 文档核对与修正

- README、根及 Windows CHANGELOG、首次使用、Tests/README、Assets/README 已包含发布选择、名称标识、托盘、设置布局、手动卸载与新图标；必要图标引用和本地包文件已核对。
- 原计划仍写“待实施”，平台对照只显示 0.3.2，开发验收的早期缺项容易被当作当前状态。此次补充最新状态、用户调整后的手动卸载范围和历史段落说明，保留原始验证记录。
- 发现 publish-windows.ps1 独立附件仍从 Mac 示例导出说明，且首次使用附件的 Assets/GuideIcon.png 在平铺附件目录中不存在。新增 export-docs.ps1，构建和发布共用 Windows 文档来源；独立 Markdown 使用同目录 GuideIcon.png，ZIP 内保留原 Assets/ 结构。
- 单独导出会生成 DemoMux-Guide.html、First-Run.zh-CN.md、GuideIcon.png、Acceptance.zh-CN.md、CHANGELOG.md，不执行发布。普通构建也会在 ZIP 旁同步这些文件。

## 验证边界

- 应用基线 3268828 已有 202 项本机及 [CI 回归](https://github.com/AidenHwang99/DemoMux/actions/runs/42626941568)通过；此次应用功能源码未改动，不把既有回归记作本轮重新运行。
- 本轮检查 PowerShell 脚本语法、独立文档导出、图标引用、Markdown 本地链接及包清单一致性；未调用实际发布脚本，发布开关保持 false。
- 三个 PowerShell 脚本通过语法解析；隔离导出的五个文件及图标路径与源文件核对通过。必要说明的本地链接和图像引用检查通过，构建同时刷新 ZIP 内文档和旁边的独立附件。
- 完整桌面截图、物理多 DPI、真实公网持续分享、托盘菜单点击、Explorer 重启、注销和生产包桌面启动仍未覆盖。历史 main／Mac 版本的验收不自动转移到本开发包。
