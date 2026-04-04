# HsAsrDictation

HsAsrDictation 是一个面向 `Windows 11 x64` 的常驻听写应用，使用本地 CPU 做离线语音识别，并把识别结果直接回写到触发热键时所在的输入位置。

项目当前已经覆盖 MVP 主链路：

- 托盘常驻
- 全局按住说话热键
- 麦克风录音
- 模型自动下载、解压与校验
- 本地识别
- 最终文本标点后处理
- 文本回写
- 剪贴板回退
- 最小设置窗
- 本地日志

## 功能概览

1. 按住全局热键开始录音，松开后结束录音。
2. 对录音进行首尾静音裁剪。
3. 使用 `sherpa-onnx` 的本地模型完成离线识别。
4. 优先通过 `SendInput(KEYEVENTF_UNICODE)` 写回文本。
5. 如果注入失败，可以回退到剪贴板粘贴。
6. 如果启用标点，会先对最终识别文本做一次离线标点后处理。

默认热键是 `Ctrl + Alt + Space`，可以在设置页修改。

## 项目结构

```text
src/HsAsrDictation/
  Asr/
  Audio/
  Foreground/
  Hotkeys/
  Insertion/
  Interop/
  Logging/
  Models/
  Notifications/
  Overlay/
  Services/
  Settings/
  Tray/
  Views/
tests/HsAsrDictation.Tests/
scripts/
design.md
implementation-status-report.md
```

核心模块：

- `Services/DictationCoordinator.cs`：串联录音、识别、状态切换和写回
- `Hotkeys/LowLevelKeyboardHotkeyManager.cs`：全局按下/释放热键检测
- `Audio/WaveInAudioCaptureService.cs`：录音采集
- `Audio/AudioSilenceTrimmer.cs`：静音裁剪
- `Models/ModelProvisioningService.cs`：模型下载、解压、校验
- `Asr/SherpaFunAsrNanoEngine.cs`：离线识别封装
- `Insertion/TextInsertionService.cs`：文本注入与剪贴板回退
- `Tray/TrayIconService.cs`：托盘菜单与状态提示
- `Settings/SettingsService.cs`：本地设置读写

## 环境要求

- Windows 11 x64
- .NET 8 SDK
- 支持麦克风输入的设备

说明：

- 这是一个 WPF 桌面应用，项目文件是 `src/HsAsrDictation/HsAsrDictation.csproj`，仓库里没有 `.sln`。
- 当前实现是面向 Windows 的，Linux 环境可用于阅读和测试部分纯逻辑代码，但无法完成真正的 `.exe` 发布验证。

## 快速开始

### 1. 构建

```bash
dotnet build src/HsAsrDictation/HsAsrDictation.csproj
```

### 2. 测试

```bash
dotnet test tests/HsAsrDictation.Tests/HsAsrDictation.Tests.csproj
```

### 3. 发布

Linux / Bash：

```bash
bash scripts/publish-win-x64.sh Release
```

PowerShell：

```powershell
pwsh ./scripts/publish-win-x64.ps1 -Configuration Release
```

发布输出默认在：

```text
artifacts/publish/win-x64/
```

## 首次运行

首次启动时，应用会优先使用本地模型目录；如果模型不存在且设置允许自动下载，就会自动下载所需模型文件。

默认本地路径位于：

- 设置文件：`%LOCALAPPDATA%\HsAsrDictation\settings.json`
- 日志目录：`%LOCALAPPDATA%\HsAsrDictation\logs`
- 模型根目录：`%LOCALAPPDATA%\HsAsrDictation\models`

设置页里可以调整：

- 输入设备
- 热键主键和修饰键
- 离线模型目录
- 流式模型目录
- 识别模式
- 是否允许自动下载模型
- 是否允许剪贴板回退
- 是否启用标点
- 是否启用流式预览

## 使用方式

1. 启动应用后，程序会常驻托盘，不会默认弹出主窗口。
2. 在任意输入框中按住热键说话。
3. 松开热键后，应用会开始识别并回写文本。
4. 如果识别失败、模型缺失或注入失败，可以查看托盘提示和本地日志。

## 设计与现状

如果你想了解这个项目的设计目标和当前实现状态，可以继续看：

- [`design.md`](design.md)
- [`implementation-status-report.md`](implementation-status-report.md)

## 已知限制

- 当前首版只实现了静音裁剪，没有接入更复杂的 VAD。
- 实时流式上屏还未完整实现。
- 管理员窗口、远程桌面和企业 IM 等输入环境还需要在真实 Windows 机器上补充回归测试。
- 当前开发环境如果不是 Windows，无法完成完整桌面行为验证。

## 测试说明

目前已有的单元测试主要覆盖可复用逻辑，例如：

- `AudioSilenceTrimmer`
- `ModelManifest`
- `ModelResidencyManager`
- `DictationOverlayController`

测试项目位于 `tests/HsAsrDictation.Tests/`，采用 `xUnit`。

## 许可证

当前仓库未单独声明许可证；如果需要对外分发，建议先补充 LICENSE 文件。
