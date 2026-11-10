# Mac 与 Windows 跨主机协作

## 共同基线

默认 `main` 保存最新集成源码。2026-11-10 已将本机 Windows 开发成果整合入主分支，同时保留远端 Mac 更新。两台主机以最新 `origin/main` 开始新工作，旧 `windows/macos-0.3.5-parity` 等分支和历史标签仅用于追溯。

当前平台版本分别为 macOS 0.3.5、Windows 0.3.5-preview.1。两者独立构建、测试和发布；Windows 发布开关仍为 false。主分支源码较公开 Windows 下载包更新，不能仅凭 Release 页面判断当前源码版本。

开始工作先执行 `git fetch origin`、`git status --short --branch`，检查未提交工作和本地分支差异。工作区干净且无独立本地提交时，可切换 main 并 `git pull --ff-only origin main`；若不能快进，先核对并保留本地提交，合并／变基处理，不能强制重置。新任务可从最新 main 创建 `codex/<任务名>` 分支，完成验证后整合回 main。

## 平台与资源边界

| 内容 | 来源与操作 |
| --- | --- |
| Mac 应用 | DemoMux/；在 Mac 按该目录构建和回归，不用旧 Windows 分支覆盖仓库 |
| Windows 应用 | DemoMuxWindows/；保留 EFS、私有账号、Job Object、网络校验、托盘及发布流程 |
| Mac 品牌图标 | Shared/Brand/AppIcon.png；`python3 Shared/Brand/build-icons.py` 默认仅更新 Mac 图标与 Mac 指南 |
| Windows 品牌图标 | DemoMuxWindows/Assets/AppIcon.png；在 Windows 用 `./DemoMuxWindows/update-icon.ps1` 同步九档 ICO、文档图标和内嵌图片 |
| 显式跨平台图标生成 | Mac 可用 `--platform windows` 或 `--platform all`，分别读取各平台已确认图源；需检查该命令产生的所有差异 |
| 项目操作图标 | Shared/Icons/ 的 Lucide SVG，与应用品牌图标分开维护 |
| Windows 独立说明 | `./DemoMuxWindows/export-docs.ps1`；与构建、发布共用同一 Windows 来源 |

两平台已确认的图标原图分别保留，Mac 生成脚本不再默认重写 Windows ICO。不要只替换 ICO 而遗漏 Windows 指南、favicon 或 Markdown 图片，也不要将 Mac 指南作为 Windows 说明导出。

## 验证与合并

- Windows 完整回归：`dotnet run --project DemoMuxWindows/Tests/DemoMux.Tests.csproj -c Release`；增加 `-- --fresh-data` 可使用新隔离目录。
- 桌面托盘检查：`-- --tray-smoke`；交互夹具为 `-- --tray-ui`，均不读取真实账号。
- 图标写入边界：`python3 Shared/Brand/test-build-icons.py`，使用标准库和模拟转换，不要求 sips／iconutil。
- Windows CI 覆盖 push main 和面向 main 的 PR，回归与发布分成不同任务。只有主分支非 PR 运行且 release.json 显式启用时才允许发布；普通源码合并不自动创建 Release。
- 修改哪一平台，就在对应主机验证。Mac 原生构建不能由 Windows 模拟测试代替，Windows 桌面、真实公网及物理 DPI 未覆盖项必须继续记录。

当前整合证据及限制见[整合记录](verification/windows-main-integration-2026-11-10.md)和[Windows 验收](verification/windows-v0.3.5-preview.1.md)。
