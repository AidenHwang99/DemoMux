# Windows 0.3.5 公开交付校验

日期：2026-11-10（Asia/Shanghai）。正式 Release 已于 10:47:22 公开。

## 公开版本

- [Release 页面](https://github.com/AidenHwang99/DemoMux/releases/tag/windows-v0.3.5)：draft=false、prerelease=false，已由仓库 releases/latest 确认为 Latest。
- 标签 windows-v0.3.5 指向 296c07ab041450d8229ff35cfe0f67ef2ac916d9，与公开 EXE 的 ProductVersion `0.3.5+296c07ab041450d8229ff35cfe0f67ef2ac916d9` 对应。
- [发布流水线](https://github.com/AidenHwang99/DemoMux/actions/runs/42744478276)成功：主分支回归、平台图标测试、锁定组件重建和校验、打包、上传、公开回下载验证全部完成。
- 发布后关闭 release.json 的一次性 publish 开关，保留版本 0.3.5、正式渠道和标签信息；后续普通 main 更新不重新发布或替换公开资产。

## 下载与 SHA-256

[Windows x64 ZIP](https://github.com/AidenHwang99/DemoMux/releases/download/windows-v0.3.5/DemoMux-Windows-x64.zip)，大小 387403135 字节。

```text
0e7f004954b723b57ebb56d5b626a78e18e200d5daa5409a2cf17d8550c5c3a9  DemoMux-Windows-x64.zip
```

[ZIP 校验文件](https://github.com/AidenHwang99/DemoMux/releases/download/windows-v0.3.5/DemoMux-Windows-x64.zip.sha256) · [附件校验清单](https://github.com/AidenHwang99/DemoMux/releases/download/windows-v0.3.5/SHA256SUMS.txt)

共 8 个公开附件：ZIP、ZIP.sha256、独立 HTML、首次使用 Markdown、GuideIcon.png、验收记录、CHANGELOG 和 SHA256SUMS.txt。发布流水线已在公开后重新下载核对，本机又从公开地址独立下载所有附件，与 GitHub 返回的各项 SHA-256 digest、SHA256SUMS 和 ZIP sidecar 逐项比对通过。

## 本机独立检查

- 解压公开 ZIP 到新目录，先检查归档路径与链接类型，再核对 673 个包清单文件及文件总数。
- 确认正式 ProductVersion 和来源提交；ZIP 内两份指南、验收记录、更新说明及图标与独立附件一致。
- 未发现账号状态、项目目录数据或测试程序；未关闭或替换正在使用的主副本，未修改真实身份和原始项目。
- 本地输出目录已替换为从公开地址下载的官方文件，保留独立下载记录及解压副本。日志为 outputs/verification/windows-v0.3.5-public-download.log、windows-v0.3.5-public-package.log。

发布前本机另通过 202 项完整回归、5 项图标生成边界测试和 4 项原生托盘检查。正式发行渠道不宣称所有实机矩阵都已覆盖；完整限制继续保留在[发行记录](windows-v0.3.5.md)。本次公开资产保持不可变，后续文档交付记录由 main 继续维护。
