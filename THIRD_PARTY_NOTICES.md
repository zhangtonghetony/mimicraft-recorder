# 第三方组件与分发说明

Mimicraft Recorder 自身代码采用 [MIT](LICENSE)。以下组件保留各自的版权与许可证，不能用本项目的 MIT 许可证替代。

## 随 Windows 便携包提供的组件

| 组件 | 版本 / 来源 | 许可与材料 |
| --- | --- | --- |
| .NET / Windows Desktop Runtime | 10.0.14，Microsoft | MIT及所附第三方条款；见 `third_party/licenses/DOTNET-*` 与包内 Notices |
| NAudio及其模块 | 2.2.1，NuGet | MIT；包版本、版权和原始许可见 `third_party/nuget-runtime.json` 与 `licenses/nuget/` |
| Vortice.Direct3D11 / DXGI / DirectX | 3.8.3，NuGet | MIT；许可与署名同上 |
| Vortice.Mathematics | 2.1.0，NuGet | MIT；许可与署名同上 |
| SharpGen.Runtime / COM | 2.4.2-beta，NuGet | MIT；许可与署名同上 |
| Windows SDK .NET bindings | Windows SDK托管引用 | Microsoft包中原有许可，见 `licenses/nuget/` |
| FFmpeg / FFprobe / FFplay及libav*共享库 | n8.1.3-24-g92611900bb-20261026，BtbN LGPL shared构建 | **LGPL v3**及依赖各自许可，完整文本、参数、来源见下文 |

测试使用的xUnit、FlaUI、Microsoft.NET.Test.Sdk等通过NuGet还原，不把测试工具的二进制文件放入普通用户包。

## FFmpeg

This software uses FFmpeg code licensed under LGPL v3. Corresponding FFmpeg source and build materials are available from the [same Release](https://github.com/zhangtonghetony/mimicraft-recorder/releases/tag/v0.3.2), in `FFmpeg-Sources-v0.3.2.zip`.

- FFmpeg源码提交：`92611900bb112123e9bf997fdcc2d78e4c4922fc`。
- 上游构建配方：BtbN/FFmpeg-Builds，`b4a2b1d7cc241786eb8e2b588a7ade40cad48bbd`（2026-10-26构建标签）。
- 原始工具ZIP的SHA-256：`09772a308ccee4304e93ad1137d513e16710e83f56161bc626e2df4e5d3b6b5b`。
- 构建使用`--enable-version3 --enable-shared --disable-static`，未启用GPL-only的x264/x265；**不能因此把本构建写成LGPL 2.1**。
- 完整构建参数见 [ffmpeg-buildconf.txt](third_party/ffmpeg-buildconf.txt)。
- 项目不修改FFmpeg。通过独立进程调用工具，保留可替换的EXE/DLL，不限制用户替换或调试这些组件。

源码材料包含FFmpeg、固定版本的构建配方、依赖源码快照及检索信息。部分上游仓库含构建工具或其他平台的配方，不代表全部都启用于此Windows构建；实际启用项以构建参数为准。依赖清单见 [media-sources.json](third_party/media-sources.json)。

媒体组件的原始许可/版权文本位于 [third_party/licenses/media](third_party/licenses/media)。LGPL v3及其引用的GPL v3文本分别在`licenses/LGPL-3.0.txt`、`licenses/GPL-3.0.txt`。它们也随便携包提供。

## 重建与替换

开发工具的固定下载地址和摘要记录于 [scripts/toolchain.lock.json](scripts/toolchain.lock.json)。公开Release保留工具原包和源码材料，避免仅依赖会变化的latest链接。

要重新构建FFmpeg，按所附BtbN构建配方选择`win64 lgpl-shared 8.1`，将FFmpeg来源固定到上述提交，并对照附带构建参数。涉及的工具链与依赖来源在配方及清单内。源码快照保留原始许可证；不要把这些第三方代码重新标为MIT。

要替换媒体工具，替换整套相互匹配的FFmpeg/FFprobe/共享DLL，或使用`MIMICRAFT_FFMPEG_DIR`指定目录。其他构建可能缺少本应用使用的滤镜或编码器，请自行验证相应功能。

上游资料：[FFmpeg许可说明](https://ffmpeg.org/legal.html)、[BtbN构建仓库](https://github.com/BtbN/FFmpeg-Builds)、[NAudio](https://github.com/naudio/NAudio)、[Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows)、[SharpGenTools](https://github.com/SharpGenTools/SharpGenTools)、[.NET](https://github.com/dotnet/runtime)。
