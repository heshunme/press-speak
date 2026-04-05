# HsAsrDictation

`HsAsrDictation` 是一个面向 `Windows 11 x64` 的本地听写应用。它常驻托盘，按住全局热键说话，松开后把识别结果直接写回当前输入位置。整个主链路基于本地 CPU 和 `sherpa-onnx`，支持离线识别、可选流式预览、离线标点和通用后处理规则。

## 项目亮点

- 托盘常驻，启动后不打断当前工作流
- 全局 Push-to-Talk 热键，支持按下开始、松开结束
- 麦克风录音、首尾静音裁剪和 30 秒上限保护
- 本地离线识别，支持可选流式预览
- 首次运行可自动下载、解压并校验模型
- 最终文本支持离线标点和可配置后处理规则
- 优先使用 `SendInput(KEYEVENTF_UNICODE)` 写回，失败后可回退到剪贴板粘贴
- 提供最小设置窗、本地日志和可测试的纯逻辑模块

## 工作方式

1. 按住热键开始录音。
2. 松开热键结束录音。
3. 程序先裁剪静音，再进行本地识别。
4. 对最终文本执行标点和后处理。
5. 将结果写回当前输入框。

## 技术栈

- `.NET 8`
- `WPF` 和 `Windows Forms` 托盘能力
- `NAudio`
- `org.k2fsa.sherpa.onnx`
- `SharpCompress`

## 环境要求

- Windows 11 x64
- .NET 8 SDK
- 支持麦克风输入的设备

说明：

- 仓库没有 `.sln`，请直接使用项目文件进行构建和测试。
- 当前实现是 Windows-only；Linux 可以阅读和测试部分纯逻辑代码，但不能完成完整桌面行为验证。

## 快速开始

### 构建

```bash
dotnet build src/HsAsrDictation/HsAsrDictation.csproj
```

### 运行

```bash
dotnet run --project src/HsAsrDictation/HsAsrDictation.csproj
```

### 测试

```bash
dotnet test tests/HsAsrDictation.Tests/HsAsrDictation.Tests.csproj
```

### 发布

Linux / Bash：

```bash
bash scripts/publish-win-x64.sh Release
```

PowerShell：

```powershell
pwsh ./scripts/publish-win-x64.ps1 -Configuration Release
```

发布产物默认输出到 `artifacts/publish/win-x64/`。

## 首次运行

首次启动时，应用会优先使用本地模型目录；如果模型不存在且设置允许自动下载，就会自动下载所需模型文件。

默认本地路径位于：

- 设置文件：`%LOCALAPPDATA%\HsAsrDictation\settings.json`
- 后处理规则文件：`%LOCALAPPDATA%\HsAsrDictation\postprocessing-rules.user.json`
- 日志目录：`%LOCALAPPDATA%\HsAsrDictation\logs`
- 模型根目录：`%LOCALAPPDATA%\HsAsrDictation\models`
- 离线模型目录：`%LOCALAPPDATA%\HsAsrDictation\models\offline`
- 流式模型目录：`%LOCALAPPDATA%\HsAsrDictation\models\streaming`
- 标点模型目录：`%LOCALAPPDATA%\HsAsrDictation\models\punctuation`

默认热键是 `Alt + Oem3`。你可以在设置页点击“开始录入”，然后按下新的组合键保存。

设置页支持：

- 输入设备选择
- 热键录入
- 识别模式切换（非流式 / 混合 / 仅流式）
- 离线和流式模型目录配置
- 自动下载模型开关
- 剪贴板回退开关
- 标点开关
- 通用后处理规则开关
- 流式预览开关
- 后处理规则列表、测试和恢复默认规则

## 核心模块

- `Services/DictationCoordinator.cs`：串起录音、识别、状态流转和写回
- `Hotkeys/LowLevelKeyboardHotkeyManager.cs`：全局按下和松开热键检测
- `Audio/WaveInAudioCaptureService.cs`：麦克风录音
- `Audio/AudioSilenceTrimmer.cs`：首尾静音裁剪
- `Models/ModelProvisioningService.cs`：ASR 模型下载、解压和校验
- `Asr/SherpaFunAsrNanoEngine.cs`：离线识别封装
- `Asr/SherpaStreamingParaformerEngine.cs`：流式识别封装
- `Asr/SherpaOfflinePunctuationService.cs`：离线标点后处理
- `PostProcessing/Engine/PostProcessingService.cs`：通用后处理规则执行
- `Insertion/TextInsertionService.cs`：文本注入与剪贴板回退
- `Tray/TrayIconService.cs`：托盘菜单和状态提示
- `Views/SettingsWindow.xaml`：设置界面

## 项目结构

```text
src/HsAsrDictation/        WPF 桌面应用
tests/HsAsrDictation.Tests/  xUnit 测试
scripts/                   发布脚本
docs/                      设计与实现说明
```

## 测试覆盖

当前自动化测试主要覆盖可复用逻辑，例如：

- `AudioSilenceTrimmer`
- `ModelManifest`
- `ModelResidencyManager`
- `PunctuationModelProvisioningService`
- `SherpaOfflinePunctuationService`
- `DictationOverlayController`
- 后处理规则引擎、规则仓储与默认规则

## 已知限制

- 当前首版没有接入真正的 VAD，只做了能量阈值静音裁剪。
- 当前只提供录音期间的流式预览，不做持续边说边写回目标输入框。
- 标点和通用后处理只作用于最终写回文本，不改写流式预览。
- 管理员窗口、远程桌面和企业 IM 等输入环境还需要在真实 Windows 机器上补充回归测试。

## 文档

如果你想了解设计目标和当前实现状态，可以继续看：

- `docs/design.md`
- `docs/implementation-status-report.md`
- `docs/post-processing-design.md`
- `docs/punctuation-design.md`

## 贡献

欢迎通过 issue 和 PR 参与改进。涉及热键、托盘、模型下载、文本写回等 Windows-only 功能时，建议附上操作系统版本、输入法和复现步骤，方便快速定位问题。

## 许可证

当前仓库未单独声明许可证；如果需要对外分发，建议先补充 `LICENSE` 文件。
