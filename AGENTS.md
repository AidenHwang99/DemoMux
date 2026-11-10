# DemoMux 工作基线

- 默认 `main` 是 Mac 与 Windows 共同的最新集成源码；开始任务先 fetch，检查工作区及本地／origin/main 差异，保留本地未提交工作。不要将历史 Windows 分支或已发布标签当作当前最新版本，也不要用 reset --hard 清场。
- Windows 当前源码 0.3.6，保留已整合的 0.3.5 修复；Mac 版本独立维护，查看各平台 CHANGELOG 与构建配置。源码合并不自动表示已发布或完成所有实机验收；以实际 Release 与对应发行记录确认发布状态。
- Mac 应用代码位于 DemoMux/，Windows 位于 DemoMuxWindows/。保留另一平台已合并的功能、资源、私有进程及账号边界；只修改本任务涉及的部分，合并冲突不能靠整树覆盖解决。
- Mac 图标源为 Shared/Brand/AppIcon.png，默认运行 Shared/Brand/build-icons.py 仅生成 Mac 资源。Windows 图标源为 DemoMuxWindows/Assets/AppIcon.png，用 DemoMuxWindows/update-icon.ps1 更新 ICO 与 Windows 文档。需要跨平台生成时明确指定平台并检查产物。
- Windows 回归：dotnet run --project DemoMuxWindows/Tests/DemoMux.Tests.csproj -c Release。跨平台图标写入边界：python Shared/Brand/test-build-icons.py。不要修改正在使用的主副本、真实授权或原始项目来做隔离测试。
- 功能、使用方式、版本或交付有变更时，同步 README、对应平台使用说明／CHANGELOG 和验收记录。保持已通过、未覆盖与发布状态可区分。

详细协作与构建入口见 docs/development.md。

## Windows 安装包发布约定

每次 Windows 正式更新必须生成完整 EXE 安装包，以 README 的 Windows 安装包直链作为首选，便携 ZIP 为可选。版本、安装包直链和 Release 链接必须对应；公开附件回下载 SHA-256 校验成功后，发布脚本更新 README 并关闭一次性发布开关。发布前不得标记为已发布或挂未验证下载链接。执行 `update-release-readme.ps1 -Version <版本> -Check` 校验，禁止仅发布 ZIP。安装／卸载验收使用隔离目录与独立注册身份，保留真实数据。
