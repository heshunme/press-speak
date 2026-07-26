# Press Speak

`Press Speak` 是一个面向 `Windows 11 x64` 的本地听写应用。它常驻托盘，按住全局热键说话，松开后把识别结果直接写回当前输入位置。整个主链路基于本地 CPU 和 `sherpa-onnx`，支持离线识别、可选流式预览、离线标点和通用后处理规则。

说明：

- 代码仓库内部仍保留部分 `HsAsrDictation` 命名空间和目录名，README 中的产品名以 `Press Speak` 为准。

## 项目亮点

- 托盘常驻，启动后不打断当前工作流
- 全局 Push-to-Talk 热键，支持按下开始、松开结束
- 麦克风录音、首尾静音裁剪，以及可配置的单次录音上限和松键尾录延迟
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

如果开发终端本身已经以管理员身份运行，可以用下面的参数启动源码构建：

```bash
dotnet run --project src/HsAsrDictation/HsAsrDictation.csproj -- --admin
```

发布后的可执行文件同样支持：

```text
HsAsrDictation.exe --admin
```

从普通终端请求 UAC 提升时，发布版必须安装在普通权限进程不可修改的受保护目录；源码目录和常规 `bin` 输出通常可写，因此会在弹出 UAC 前被安全校验拒绝。如果只是安装目录自身的属主/ACL 允许当前用户写入（例如自建在非 C 盘的发布目录），应用会先弹窗询问是否修复，确认后单独提权 Windows 自带的 `icacls.exe` 加固该目录，再继续原有的管理员重启流程——这会比平时多一次 UAC 确认。这个自动修复只处理安装目录自身，不会改动其上级目录；如果失败原因是上级目录（例如自建的 `D:\Program Files`）本身允许普通账号修改自己的 ACL，需要手动处理该上级目录或换一个不经过它的安装路径。

说明：

- 应用默认仍以普通权限启动
- 传入 `--admin` 后会请求 UAC，以管理员模式重启自身
- 托盘菜单也提供“以管理员模式重启”入口，便于临时切换
- 管理员重启同样只允许从普通权限进程不可修改的本机固定 NTFS/ReFS 发布目录发起
- 管理员重启必须由当前账号本人确认 UAC；改用另一个管理员账号会被拒绝
- 如果普通模式实例已经在运行，重新用 `--admin` 启动不会自动切换；请回到托盘菜单手动升级到管理员模式

### 登录后自动启动

设置页可以配置是否在当前用户登录 Windows 后自动启动，并可选择普通模式或管理员模式：

- 普通模式使用当前用户启动项，不需要 UAC
- 管理员模式在普通权限下配置或关闭时通常请求一次 UAC，用于维护最高权限登录任务；设置保存失败并恢复原模式时可能再次请求
- 管理员模式配置完成后，后续登录自启动不会再弹 UAC
- 管理员模式只支持当前账号本人配置：账号需属于 Administrators 并启用 UAC，不支持在 UAC 中改用另一个管理员账号代建
- 管理员模式只允许从直接挂载的本机固定 NTFS/ReFS 盘符上的 ACL 受保护发布目录启用；目录与文件只能由 SYSTEM、Administrators 或 TrustedInstaller 修改，任何标准账号可写、所有者不受信任、源码目录、下载目录、网络盘、`SUBST` 盘、可移动盘或重解析点都会被拒绝
- 如果失败原因仅是安装目录自身的属主/ACL 允许当前用户写入，保存时会先弹窗询问是否自动修复（单独提权 `icacls.exe` 加固，多一次 UAC），修复范围不含上级目录
- 启用管理员自启后应持续保持安装目录 ACL，不要手动放宽权限或移动目录；更新安装位置或权限后应重新保存该设置并复核
- 如果检测到旧路径、重复或安全性无法确认的启动项，设置页会提示修复；保存所选模式会重建或清理冲突项
- 自动启动后应用只进入托盘，不主动打开设置窗口

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

说明：

- 这些路径反映的是当前实现的本地数据布局，暂时仍沿用 `HsAsrDictation` 目录名。

默认热键是 `Alt + Oem3`。你可以在设置页点击“开始录入”，然后按下新的组合键保存。

设置页支持：

- 输入设备选择
- 热键录入
- 识别模式切换（非流式 / 混合 / 仅流式）
- 离线和流式模型目录配置
- 单次录音上限配置
- 松键尾录延迟配置
- 自动下载模型开关
- 登录后自动启动及自启权限模式
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
- 管理员权限窗口需要应用自身也以管理员模式运行，普通权限实例无法稳定捕获这类窗口中的全局热键。
- 管理员模式自动启动要求完整发布目录位于普通权限进程不可修改的本机固定 NTFS/ReFS 卷；普通模式自动启动没有该限制。安装目录自身不满足时可自动修复（一次额外 UAC），但修复范围不含上级目录，上级目录本身权限过宽仍需手动处理或更换安装路径。
- 管理员 `Windows Terminal` / `PowerShell` 已完成真实 Windows 回归；远程桌面和企业 IM 等输入环境仍待补测。

## 文档

如果你想了解设计目标和当前实现状态，可以继续看：

- `docs/design.md`
- `docs/implementation-status-report.md`
- `docs/post-processing-design.md`
- `docs/punctuation-design.md`

## 贡献

欢迎通过 issue 和 PR 参与改进。涉及热键、托盘、模型下载、文本写回等 Windows-only 功能时，建议附上操作系统版本、输入法和复现步骤，方便快速定位问题。

## 许可证

本项目采用 Apache License 2.0，详见 [`LICENSE`](LICENSE)。
