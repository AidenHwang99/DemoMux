# DemoMux Windows UI 对照验收

final result: passed

## 对照范围

- 源图：用户提供的 Mac 主界面，`C:/Users/Keepu/AppData/Local/Temp/codex-clipboard-dda880d8-7ddb-47b9-91fd-1dc52891dd34.png`，1840 × 1384 像素。
- 最终实机截图：`outputs/ui-final-dashboard.png`，1840 × 1384 像素；Windows 真实 DPI 192 / 200%，窗口约 920 × 692 DIP。Mac 源图的实际 DPI 未知；只按相同像素尺寸、内容区和状态比较，不宣称跨平台逐像素相等。
- 状态：入口已验证、0 / 3 分享、一个停止的 HTML 测试项目。账号与项目文本使用本机真实身份和测试夹具，未复制参考图中的邮箱或项目文件。
- 原生 WPF 正式实现；按用户要求直接修改已有生产应用，未另建网页原型。Windows 标题栏、系统字体和原生文件选择器为明确的平台约束。
- 两次对照均在同一图像输入中同时打开源图和实机图。截图仅保存在本机，含账号信息，不上传公开仓库。

## 比较历史

1. 首轮：`outputs/ui-dashboard-before-fix.png`。发现 P2 账号卡片行距过大、区块间距推迟项目列表、浅色系统滚动条突兀。保持阻塞，未据此交付。
2. 修复：压缩身份卡文字边距、调整行高和区块节奏、使用细深色滚动条；统一输入框和弹窗。另发现项目卡片操作区偏高，已缩小项目按钮与内边距。
3. 复核：`outputs/ui-final-dashboard.png`。重新按 1840 × 1384 尺寸与源图同屏比较，项目及底栏均在首屏；无仍需修复的 P0/P1/P2 项。

## 五项表面检查

| 项目 | 结论与证据 |
| --- | --- |
| 字体 | 大标题、编号、分区标题和辅助信息层次清晰。使用 Segoe UI / Microsoft YaHei UI，保留 Windows 字形，不伪装成 Mac 字体；长项目名省略并附完整提示。 |
| 间距与布局 | 24 DIP 外边距、紧凑身份卡和项目卡、固定底栏保持参考图结构。用户要求优化交互，因此低频操作归入「更多」。最小窗口见 `outputs/ui-minimum.png`，底栏换行且内容可滚动。 |
| 颜色 | 采用 Mac 源码既有 `#100d11 / #331522 / #19151a / #44303b / #faf2f5 / #c8b6bf / #ff5b75 / #ffa3b4 / #8e354c`，统一主次操作、焦点、错误和禁用。 |
| 图像与图标 | 保留原应用 ICO；使用系统 Segoe MDL2 Assets 图标库。无新照片、插画或需生成的栅格资产；账号采用 Windows 联系人图标，属于平台适配。 |
| 文案 | 保留本机项目／公网演示、身份、项目、发布、底栏等核心语义。发布前有预计链接、路径说明和访问提示；非法名称／端口即时显示错误。 |

## 状态与操作证据

- 发布弹窗：`outputs/ui-publish-dialog.png`；端口独立入口：`outputs/ui-port-publish.png`；路径模式待确认：`outputs/ui-port-path-consent.png`。
- 名称错误：`outputs/ui-rename-validation.png`；端口错误：`outputs/ui-invalid-port.png`。
- 账号管理：`outputs/ui-account.png`；取消默认焦点的确认框：`outputs/ui-account-confirm.png`。
- 端口重定位后快速预览：`outputs/ui-relocated-quick-error.png`，明确提示新端口未响应，未崩溃或使用旧端口。
- 真实 HTML 发布显示 1 / 3，改名与取消发布可操作；菜单、端口添加、路径模式切换和确认校验均实际操作。
- 全图和独立弹窗截图中的文字、边框及控件已清晰可读；无需另行裁剪放大区域。弹窗截图提供表单与焦点的细节证据。
- `outputs/ui-clipboard-busy.png` 记录本次剪贴板占用错误，重试后仍为 1 / 3，不触发重复发布；本次没有标记复制成功。
- `outputs/ui-guide-offline.png` 显示停止服务后的原版内嵌说明，采用应用包内 WebView2。原指南显示基线版本 0.3.2，Windows 新界面版本见安装包和更新说明。
- 38 项既有功能回归通过，详见 `docs/verification/windows-v0.3.3-ui-regression.log`。

## 保留差异与待测

Windows 原生字体、联系人图标、标题栏和较大的常用按钮是平台适配；项目菜单分组、空状态与表单校验属于本次交互改进。原版独立使用说明 HTML 保持原样。Win10、其他真实 DPI、所有硬件快捷键组合及独立外部设备网络仍待测，不以截图或自动回归代替。

本版不存在阻塞交付的视觉问题。P3：后续可在更多 Windows 字体渲染和高对比度环境继续细化，未声称这些环境已完成验收。

## Preview 2 追加复核

重试倒计时、停止／恢复的隔离测试已实际操作；后续明确标识隔离窗口且前置步骤显示未执行，避免与真实登录窗口混淆。正式账号管理使用 outputs/retry-real-account.png 复核，显示真实账号及公网验证状态，补齐连接／恢复入口。倒计时文字更新不重建控件，流光使用合成器驱动的位移动画，并尊重系统关闭动画设置。48 项回归及加密状态写入专项通过。

## Preview 3 菜单与操作栏复核

本轮源图为用户提交 codex-clipboard-0fa01b3d-e9eb-4c62-a98d-22c6acd1a312.png。P1：默认菜单背景灰栏遮住文字；P2：两排项目按钮堆叠、重复启动状态入口。已更换完整 ContextMenu 模板及分隔线；项目操作同排、次要维护操作归入更多，导入统一入口，底栏按状态显示会话操作。最终弹层截图 outputs/layout-menu-final.png 已打开检查，文字与边框无遮挡；窄窗口截图 outputs/layout-narrow.png 为 1440 × 1120 像素 / DPI 192。布局夹具使用示例账号与数据、显眼隔离标识，不读取真实授权。菜单修改名称及添加端口实际打开表单并取消。样式、间距、文案、图标与原 Mac 色板保持一致；菜单字体及焦点完整。48 项回归通过，未声称 Win10、多屏、多真实 DPI 或完整辅助技术验收。final result: passed。
