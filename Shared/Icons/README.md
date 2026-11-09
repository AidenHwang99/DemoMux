# DemoMux 共享图标

项目卡片图标使用此目录内固定提交的 Lucide SVG，不能依赖 Windows 字体截图。SVG 为唯一源文件，Windows 原生矢量绘制直接读取这些文件；Mac 端同步相同 SVG／路径时不应自行重画图标。来源、固定提交及 SHA-256 见 `source.json`，完整上游许可见 `lucide/LICENSE`，随 Windows 包分发。

| 动作 ID | 文件 | 中文提示 | 可用状态 |
| --- | --- | --- | --- |
| project.preview | eye.svg | 本机预览 | 未发布项目 |
| project.open | external-link.svg | 打开网页 | 已发布且非验证中 |
| project.copy | copy.svg | 复制链接 | 已发布且非验证中 |
| project.publish | cloud-upload.svg | 发布 | 入口就绪、有名额 |
| project.unpublish | cloud-off.svg | 取消发布 | 已发布且非验证中 |
| project.start | circle-play.svg | 启动项目 | 项目未启动 |
| project.stop | circle-stop.svg | 停止项目 | 项目运行中 |
| project.more | ellipsis.svg | 更多操作 | 当前无阻塞操作 |

固定五个槽位：预览／打开、复制、启动／停止、发布／取消发布、更多。启动成功后原位显示停止，停止后原位显示启动。发布校验成功后原位显示取消发布，取消后原位显示发布；发布中禁用并显示提示。未发布时复制按钮禁用并保留位置，避免其他动作跳动。按钮组位于卡片右侧垂直居中，图标位于按钮中心。

尺寸约定：SVG viewBox `0 0 24 24`，描边 2、圆端点／圆连接；展示 18 DIP，按钮 36 × 36 DIP，间距 6 DIP。使用当前前景色，不把主题颜色写入源 SVG。复制／发布为主操作，其余保持次级层次。所有图标按钮必须保留中文 tooltip 和 accessibility label，不能只用形状传达动作。

额外收录 `user-round.svg`、`check.svg`、`chevron-down.svg`，用于替代已有 Windows 系统字形的跨平台映射；Windows 当前账号／复选控件仍可使用系统模板字形。此仓库不复制或分发 Windows 字体。Mac 可从这些 SVG 生成平台需要的 PDF／矢量资产，保留许可和动作 ID。

修改流程：更新原始 SVG → 更新 `source.json` 中的来源及校验值 → 同时检查 Windows 与 Mac 的动作映射、禁用状态和无障碍名称。此轮没有修改已发布的 Mac 代码。
