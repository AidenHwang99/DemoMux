# Windows 应用图标

- `AppIcon-source.jpg`：用户提供的原始图案，保留不覆盖。
- `AppIcon.png`：通过内置 imagegen 处理外侧白底，再按旧应用图标的玫红配色调整后的透明 PNG，保留黑色圆角底板与立体图案。旧图标主色取样为 `#ff4057`（提交 `7be6fd4` 的 ICO）。
- `DemoMux.ico`：由 PNG 转换的 Windows 多尺寸图标，包含 16、20、24、32、40、48、64、128、256 px、32 位透明帧。
- `GuideIcon.png`：128 px 文档图标，与 ICO 同帧；首次使用 Markdown 引用它，离线 HTML 内嵌同一图像及 32 px 浏览器标签图标。

Windows 程序文件、窗口和系统托盘共用此图标。Mac 源码与资源未修改。操作按钮仍使用 `Shared/Icons` 中的 Lucide SVG。

在 Windows PowerShell 中运行 `./DemoMuxWindows/update-icon.ps1` 可从已保存的 PNG 重新生成 ICO，并同步文档 PNG、HTML 页头和 favicon，无需重新生成图像或安装 Python 图片库。图像处理仅用于替换应用标志；不修改项目文件。两份打包 HTML 自包含图标，移动 HTML 后仍可离线显示。

图像处理提示词（内置工具，透明背景开启）：

> Edit this supplied application icon only to remove the pure white exterior background, making those outside pixels genuinely transparent. Preserve the entire original black rounded-square tile, its exact rounded silhouette, red dimensional D/play symbol, dark bevels, red glow, proportions, perspective, lighting and color as closely as possible. No redesign, no replacement logo, no added text, no extra marks. Keep the same square composition with a modest transparent margin. Output a clean high-resolution RGBA application icon on actual transparency; the black interior and triangular play-shaped opening must remain opaque, only the white exterior becomes transparent.

按用户反馈调整配色的第二轮提示词（目标为上述 PNG，旧图标仅作颜色参考）：

> Image 1 is the edit target: retain its exact dimensional D/play symbol, rounded black square tile, silhouette, composition and transparency. Image 2 is ONLY the previous app icon's color reference, not a shape reference. Recolor only the red D/play material and red reflections in image 1 to match the softer rosy coral red used by the terminal symbol in image 2 (sampled primary accent #ff4057, with the original muted rose/burgundy shading). The current target is too intensely crimson/signal-red. Replace pure deep red surfaces with a softer rose-coral material, desaturate the broad front face moderately, soften the luminous edge highlights and reduce red glow, while retaining realistic bevel shading and dimensional depth. Main highlights should read as #ff4057 rose-red rather than pure #ff0000; shaded front surfaces can be around #cf3e55. Do not copy the terminal glyph, do not alter the D outline or triangular opening, do not add text or ornaments. Keep black interior opaque and preserve genuine transparent outer background.
