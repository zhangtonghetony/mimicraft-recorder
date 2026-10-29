# 开发与构建

命令从本仓库根目录执行。推荐 Windows 11 x64，Python 3.10+，可见桌面会话。项目使用 .NET 10 / WPF，目标框架为 `net10.0-windows10.0.26100.0`。

## 工具

```powershell
python scripts/install-tools.py
$env:DOTNET_ROOT = (Resolve-Path .tools/dotnet).Path
& ./.tools/dotnet/dotnet.exe restore Mimicraft.Recorder.slnx --locked-mode
```

安装脚本使用`scripts/toolchain.lock.json`中的固定地址和摘要，下载到`.tools/`，不修改系统PATH。SDK版本与`global.json`一致；NuGet版本由各项目`packages.lock.json`锁定。已有完整环境可直接使用自己的SDK；媒体工具可通过`MIMICRAFT_FFMPEG_DIR`指定，但不同构建的滤镜/编码器不一定齐全。

NuGet需要联网还原。Windows SDK托管引用随目标框架还原；本项目验证环境另外装有Windows SDK26100。缺少引用时先核对SDK/目标框架，不要把API最低版本当作整个依赖链的系统支持范围。

## 预览

```powershell
& ./.tools/dotnet/dotnet.exe run --project src/Mimicraft.Recorder/Mimicraft.Recorder.csproj
```

或双击`scripts/start.cmd`。开发预览无需正式打包。

```powershell
python scripts/dev.py
```

观察器先构建，再安全重启窗口；编译失败保留原窗口，忙碌时等待录制/导出结束。新窗口不恢复录制。当前采用重建/重启，不声称具备IDE的XAML Hot Reload。

## 检查

```powershell
./scripts/verify-all.ps1
```

真实音视频设备与压力检查是显式开关，见[测试指南](TESTING.md)。环境只读检查为`python scripts/check-env.py`。不要在UI/设备测试运行时并行修改源码或启动另一组设备用例。

## 便携发布

```powershell
python scripts/build-portable.py
```

输出在`artifacts/packages/`，包含Windows x64自包含EXE、媒体工具和许可说明。日常开发用run；发布时才publish。公开Release另外附带SHA-256摘要及第三方源码/构建材料。

发行前需要检查第三方许可、下载包内容、公开截图与测试范围。不要打包本机配置、日志、录屏、摄像头照片、推流地址或PDB。设备/交互验证应在实际解压出来的包中进行。

## 配置与源码边界

应用默认写入用户LocalAppData和Videos，不在安装目录内写入用户录像。公开仓库包含应用、测试、工具脚本和公开文档；私人实验日志与设备样本不属于分发物。
