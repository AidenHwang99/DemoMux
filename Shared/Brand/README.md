# DemoMux 应用图标

`AppIcon.png` 是 2026-11-09 更新的黑色 / 玫红图标原图：金属切面 D，中央镂空播放箭头，黑色圆角底座，外部透明。按原终端图标的玫红色调调整（取样约 `#FF5A6A`），保留金属明暗层次。原图由 OpenAI imagegen 生成并调色。

此原图是 Mac 当前采用的版本。Windows 以本机确认的 `DemoMuxWindows/Assets/AppIcon.png` 为准，图源与九档 ICO／说明图标同步维护。两者均保留已认可的玫红配色，不通过覆盖另一平台资源来同步版本。

在 macOS 安装系统命令行工具后，从仓库根目录运行：

```sh
python3 Shared/Brand/build-icons.py
```

脚本使用系统 `sips`、`iconutil` 与 Python 标准库，无第三方依赖。**默认仅更新 Mac**，生成：

- `DemoMux/Resources/AppIcon.icns`：16–1024 像素，含 Retina 尺寸。
- `DemoMux/Resources/Sample/index.html` 内嵌的页头图标与 favicon：分别使用 128、32 像素 PNG，离线指南无需外部图片。

Windows 推荐在 Windows 主机运行 `./DemoMuxWindows/update-icon.ps1`。Mac 若明确需要生成 Windows 资源，可加 `--platform windows`，脚本会读取 Windows 自己的 PNG，生成 16、20、24、32、40、48、64、128、256 px ICO、GuideIcon.png 及 Windows 指南的内嵌图片；`--platform all` 才同时更新两平台。

运行 `python3 Shared/Brand/test-build-icons.py` 可验证平台写入边界，无第三方依赖，也不要求原生转换命令。Mac 的 sips 与 Windows WPF 使用不同缩放实现，不要求重新生成的 PNG／ICO 跨系统逐字节一致；Windows 已确认产物以 Windows 脚本和包清单为准。

此目录用于应用品牌图标，项目操作按钮的共享 SVG 仍由 `Shared/Icons` 维护。两台主机的共同源码基线与协作约定见[开发说明](../../docs/development.md)。
