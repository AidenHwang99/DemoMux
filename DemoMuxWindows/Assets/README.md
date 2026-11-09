# Windows 应用图标

- `AppIcon-source.jpg`：用户提供的原始图案，保留不覆盖。
- `AppIcon.png`：通过内置 imagegen 处理外侧白底后的透明 PNG，保留黑色圆角底板与红色立体图案。
- `DemoMux.ico`：由 PNG 转换的 Windows 多尺寸图标，包含 16、20、24、32、40、48、64、128、256 px、32 位透明帧。

Windows 程序文件、窗口和系统托盘共用此图标。Mac 源码与资源未修改。操作按钮仍使用 `Shared/Icons` 中的 Lucide SVG。

在 Windows PowerShell 中运行 `./DemoMuxWindows/update-icon.ps1` 可从已保存的 PNG 重新生成 ICO，无需重新生成图像或安装 Python 图片库。图像处理仅用于替换应用标志；不修改项目文件。

图像处理提示词（内置工具，透明背景开启）：

> Edit this supplied application icon only to remove the pure white exterior background, making those outside pixels genuinely transparent. Preserve the entire original black rounded-square tile, its exact rounded silhouette, red dimensional D/play symbol, dark bevels, red glow, proportions, perspective, lighting and color as closely as possible. No redesign, no replacement logo, no added text, no extra marks. Keep the same square composition with a modest transparent margin. Output a clean high-resolution RGBA application icon on actual transparency; the black interior and triangular play-shaped opening must remain opaque, only the white exterior becomes transparent.
