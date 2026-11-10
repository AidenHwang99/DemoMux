# Windows 最新开发成果整合至 main

日期：2026-11-10。用户明确要求以本机最新 Windows 版本作为仓库最新源码，供另一台 Mac 主机继续工作。

## 整合来源与结果

- 保留远端 main 的 0aa2eb77e27a0086b0d4191a853c2892d07026fe 及其前序 Mac／品牌更新。
- 合入本机和远端已同步的 Windows 分支 fa05f588cae09daef9a35d8b8d0c8e908d3b04cb，包含发布预检、名称标识、托盘、设置布局、手动卸载、最新 Windows 图标和文档导出修复。
- 两处合并冲突分别处理：CHANGELOG 保留双方记录；Windows ICO 采用本机确认的九尺寸玫红版本。Mac 的应用源码、ICNS、指南 HTML 和共享 Mac 原图保持相对整合前 main 不变。
- 最新源码入口统一为 main；整合当时 Mac 版本 0.3.5，Windows 0.3.5-preview.1，后续由正式 Windows 0.3.5 接续。旧 Windows 分支留作历史，后续工作不能再以它代替主分支基线。

## 防止跨主机覆盖

- 新增根 AGENTS.md 和 docs/development.md，说明同步、平台目录、构建和验证入口。
- Shared/Brand/build-icons.py 默认仅生成 Mac 资源。明确指定 Windows 或 all 时，Windows 产物读取 Windows 自己已确认的 PNG，并同步九尺寸 ICO、文档 PNG 与 Windows HTML。
- Windows 主机继续使用 update-icon.ps1 作为其图标和文档生成入口。不同系统缩放实现不承诺产物逐字节相同；保留各自已确认图源，不能默认拿 Mac 图源覆盖 Windows。

## 验证与交付

- Windows 合并候选在全新隔离目录实际通过 202 项完整回归和 4 项原生托盘检查。新增 5 项图标边界测试通过，覆盖默认目标、各平台不改另一平台资产、Windows 自有图源与九尺寸 ICO、显式双平台操作。
- 检查文档本地链接、导出附件、包清单及 Windows 图标一致性。应用、账号和原始项目保护边界不变。
- 10 份整合说明中的 52 个本地链接／图像引用核对通过，无合并标记。Mac 应用目录和共享 Mac PNG 相对整合前 origin/main 无差异；Windows ICO 与 fa05f58 中已确认版本的 Git 对象一致。
- CI 扩展为 push main、面向 main 的 PR 及历史 Windows 分支；增加图标边界测试。回归任务只读，独立发布任务仅允许 main 的非 PR 运行并同时满足 publish=true；当前仍为 false。
- 本记录描述源码整合，不代表正式 Windows 0.3.5 发布或全部实机验收通过。真实公网持续分享、物理多 DPI、完整视觉截图、Explorer 重启、注销、第二实例完整唤回和最终生产包桌面启动仍保留为未覆盖；既有隔离测试不能替代这些结论。
- Mac 原生 sips／iconutil 未在 Windows 执行；Mac 已有资源保持不变，边界测试使用模拟转换检查写入范围。

主分支检查见 [Windows CI](https://github.com/AidenHwang99/DemoMux/actions/workflows/windows-release.yml?query=branch%3Amain)。详细应用验证过程见 [Windows 验收记录](windows-v0.3.5-preview.1.md)。
