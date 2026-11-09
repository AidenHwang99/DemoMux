# Mac 0.3.3 与 Windows 更新对照

同步时间：2026-11-09。参考 Windows `windows-v0.3.3-preview.4` 与主分支 `9820174`。本表描述代码实现与验证方法，不把离屏或隔离测试当成真实公网验收。

| Windows 改动 | Mac 对应实现 | 自动覆盖 |
| --- | --- | --- |
| 五步完成/当前/停止/错误状态，未执行不画为成功 | Verification.swift / SetupProgress；SetupInterface.swift | 实际完成状态、未执行步骤、停止与重新开始 |
| 未同步账号不能进入第五步 | Accounts.swift / requireSynchronized；AccountController.swift | 缺少用户主体、域名、当前身份或连接；旧缓存兼容 |
| 探测前后校验账号 | main.swift / waitForPublic | 预期账号与域名门禁；中途身份变化不重试 |
| DNS、超时、连接、握手错误自动重试 | VerificationRetry，2/4/8/15 秒退避 | 尝试次数、逐秒倒计时、取消、致命错误不重试 |
| 第五步失败/停止后只恢复校验 | SetupVerificationContext | 前置步骤状态保留；停止准备页 Funnel，保留私有预览；账号命令及时取消 |
| 倒计时不重建动画或控件 | VerificationActivityView，原生 Core Animation | 文字变化时活动视图实例保持；减少动态效果遵循系统设置 |
| 项目动作固定五个槽位 | Interface.swift / projectRow | 停止、运行、已发布、发布中五槽一致，36 点按钮，禁用复制占位 |
| 共享 SVG | Shared/Icons，Mac 直接使用原生 SVG 图像表示 | 11 个源文件 SHA-256、许可、NSImage 读取 |
| 统一添加、更多菜单与单行操作 | NativeControls.swift | 菜单与动作路由；长名称省略；无障碍名称与提示 |
| 底栏只显示启动或结束，窄窗口调整 | ResponsiveFooter | 最小布局与现有控件尺寸；实际窗口观察单独记录 |
| 刷新 Caddy 后使用新 HTTP 连接 | 每次探测启动独立 curl 请求，保留 Mac 0.3.2 行为 | 路由重载、正确标记与错误标记拒绝 |
| 更换来源后不沿用旧预览 | relocate，移除旧快照/代理，下一次重新准备 | 原有源文件失效与路由撤下覆盖；新增交互另行验收 |

## Mac 原生取舍

保留系统字体、NSMenu、文件选择器和 NSGlassEffectView；用 Core Animation 做持续流光。项目图标直接使用共享 SVG，没有重新描画，也没有复制 Windows 字体。SVG 来源、固定提交和许可随 App 分发。

Windows 专属 EFS 状态替换修复、Windows 窗口材质和 WPF 菜单模板不移植。Mac 使用既有 0700 私有目录、0600 账号元数据、官方 ARM64 Tailscale 1.104.5 与用户态独立 socket。

## 复核方法

完整回归：`bash DemoMux/Tests/run.sh`。

真实窗口的隔离夹具：`bash DemoMux/Tests/build-interaction.sh`。脚本生成 Layout QA 与 Retry QA 两个独立 App，支持目录替换为独立临时目录，不读取真实账号缓存、不登录、不访问公网。窗口显眼标注合成测试；未执行的前四步显示「未执行 · 隔离测试」。Retry QA 可停止/继续，⌘R 模拟网络恢复。此快捷键仅为夹具，正式 App 的 ⌘R 是检测公网。

真实安装版仍需观察首次准备、账号同步、公网入口、菜单、窄窗口、真实发布与退出清理。锁屏时不能把无法取得的桌面观察标记为通过。
