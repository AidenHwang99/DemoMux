# DemoMux 应用图标

`AppIcon.png` 是 2026-11-09 确认使用的黑红图标原图：猩红色金属切面 D，中央镂空播放箭头，黑色圆角底座，外部透明。原图由 OpenAI imagegen 生成。

macOS 与 Windows 共用同一原图。在 macOS 安装系统命令行工具后，从仓库根目录运行：

```sh
python3 Shared/Brand/build-icons.py
```

脚本使用系统 `sips`、`iconutil` 与 Python 标准库，无第三方依赖，生成：

- `DemoMux/Resources/AppIcon.icns`：16–1024 像素，含 Retina 尺寸。
- `DemoMuxWindows/Assets/DemoMux.ico`：16、24、32、48、64、128、256 像素，保留透明度。

两平台已有构建配置直接引用上述文件。此目录用于应用品牌图标，项目操作按钮的共享 SVG 仍由 `Shared/Icons` 维护。
