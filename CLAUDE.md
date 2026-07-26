# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 项目概述

`Press Speak` 是面向 Windows 11 x64 的本地听写托盘应用（.NET 8 + WPF + WinForms 托盘）：按住全局热键录音，松开后本地 `sherpa-onnx` 识别，经标点和后处理规则加工，再写回当前输入位置。产品名为 `Press Speak`，但代码命名空间、目录和本地数据路径仍沿用 `HsAsrDictation`。

## 常用命令

仓库没有 `.sln`，一律直接对项目文件操作：

```bash
# 构建 WPF 应用（会连带构建 Core；Linux 上因 EnableWindowsTargeting 也能编译，但无法运行）
dotnet build src/HsAsrDictation/HsAsrDictation.csproj

# 运行（仅 Windows）
dotnet run --project src/HsAsrDictation/HsAsrDictation.csproj

# 测试（xUnit，测试工程 target 为纯 net8.0，Linux 上可直接运行）
dotnet test tests/HsAsrDictation.Tests/HsAsrDictation.Tests.csproj

# 运行单个测试类 / 单个测试
dotnet test tests/HsAsrDictation.Tests/HsAsrDictation.Tests.csproj --filter "FullyQualifiedName~AudioSilenceTrimmerTests"
dotnet test tests/HsAsrDictation.Tests/HsAsrDictation.Tests.csproj --filter "FullyQualifiedName~AudioSilenceTrimmerTests.Trim_RemovesLeadingAndTrailingSilence"

# 发布 win-x64（产物在 artifacts/publish/win-x64/）
bash scripts/publish-win-x64.sh Release
```

## Core / App 双项目结构（重要）

代码拆成两个项目，命名空间统一沿用 `HsAsrDictation.*`：

- `src/HsAsrDictation.Core`（纯 `net8.0`，`RootNamespace=HsAsrDictation`）：所有可在 Linux 上编译和测试的逻辑，包括主链路编排 `DictationCoordinator`、ASR/标点引擎、后处理、设置、自启动纯逻辑与安全校验、ViewModel。仅声明 P/Invoke 的 `Interop/Win32.cs` 与带 `[SupportedOSPlatform("windows")]` 的 `WindowsStartupRegistrationSecurityValidator` 也在 Core（声明可跨平台编译）。包引用：sherpa-onnx、SharpCompress。
- `src/HsAsrDictation`（`net8.0-windows10.0.22621.0`，WPF/WinForms）：Windows 实现与 UI——`WaveInAudioCaptureService`、`LowLevelKeyboard*`、`ForegroundContextService`、`TextInsertionService`、`ElevationService`、`SingleInstanceCoordinator`、`WindowsStartupRegistrationPlatform`、托盘/浮窗/通知/窗口、组合根 `App.xaml.cs`。包引用：NAudio。引用 Core。

约定：

1. 新增纯逻辑放 Core（编译器会强制它不依赖 Windows API），新增 Windows 交互放 App。惯例是把纯逻辑与平台实现拆开，例如 `StartupRegistrationService`（Core，被测试）与 `WindowsStartupRegistrationPlatform`（App）、`DictationOverlayController`（Core）与 `StatusOverlayService`（App）。
2. `tests/HsAsrDictation.Tests`（纯 `net8.0`）只引用 Core，因此可在 Linux 直接运行。Core 通过 `InternalsVisibleTo` 对 `HsAsrDictation` 与 `HsAsrDictation.Tests` 暴露 internal。
3. `Foreground/ForegroundContext.FocusedElement` 在 Core 中是 `object?`（Windows 下实际为 `AutomationElement`，由 `ForegroundContextService` cast），保持 Core 无 UI 依赖。
4. 嵌入的默认后处理规则 `Resources/PostProcessing/default-rules.json` 在 Core 程序集中，逻辑名仍为 `HsAsrDictation.Resources.PostProcessing.default-rules.json`。

## 架构

### 组合根与主链路

没有 DI 容器；所有服务在 `src/HsAsrDictation/App.xaml.cs` 的 `OnStartup` 中手动 new 并接线。`Services/DictationCoordinator.cs` 是编排中枢，串起完整主链路：

热键按下（`Hotkeys/LowLevelKeyboardHotkeyManager`，用 `WH_KEYBOARD_LL` 低级键盘钩子而非 `RegisterHotKey`，因为需要"按下开始、松开结束"的 PTT 语义）→ 录音（`Audio/WaveInAudioCaptureService`，16 kHz/mono/16-bit）→ 首尾静音裁剪（`Audio/AudioSilenceTrimmer`，能量阈值，非真 VAD）→ 离线识别（`Asr/SherpaFunAsrNanoEngine`；可选流式预览走 `SherpaStreamingParaformerEngine`）→ 离线标点（`Asr/SherpaOfflinePunctuationService`）→ 后处理规则（`PostProcessing/Engine/PostProcessingService`）→ 写回（`Insertion/TextInsertionService`：优先 `SendInput(KEYEVENTF_UNICODE)`，失败回退剪贴板粘贴）。

标点和后处理只作用于最终写回文本，不改写流式预览。

### 启动流程的分支模式

`App.OnStartup` 按顺序处理多个提前退出的分支，改动启动逻辑时必须理解这个顺序：

1. `StartupOptions.Parse` 解析命令行（`--admin`、自启动、维护命令等）
2. 计划任务维护模式（`ExecuteStartupTaskMaintenance`）：执行后直接退出
3. 管理员启动账号预检 / 账号一致性校验（拒绝 UAC 中切换到其他管理员账号）
4. `SingleInstanceCoordinator`：单实例协调，可能把启动请求转发给已运行实例后退出
5. `--admin` 时经 `ElevationService` 请求 UAC 重启，成功则当前实例退出

### 权限与自启动安全模型（安全敏感区）

`--admin` 提升和管理员模式登录自启动都有严格的安全校验，核心在 `Services/WindowsStartupRegistrationSecurityValidator.cs` 和 `ElevationService.cs`：只允许从普通权限进程不可修改的本机固定 NTFS/ReFS 发布目录发起（目录/文件只能由 SYSTEM、Administrators、TrustedInstaller 修改；源码目录、bin 输出、网络盘、SUBST 盘、可移动盘、重解析点一律拒绝），且必须是当前账号本人确认 UAC。改动这一区域时不要放宽校验，并同步更新相关测试（`StartupRegistrationServiceTests`、`WindowsStartupRegistrationSecurityValidatorTests`、`StartupTaskSecurityDescriptorTests`）。

### 后处理规则引擎

`PostProcessing/` 按 `Abstractions`（接口）、`Engine`（服务/仓储/工厂）、`Rules`（具体规则）、`Models`、`Validation`（含正则安全校验 `RegexSafetyValidator`）分层。默认规则是嵌入资源 `Resources/PostProcessing/default-rules.json`，用户规则存于 `%LOCALAPPDATA%\HsAsrDictation\postprocessing-rules.user.json`。

### 设置保存的回滚约定

`SettingsWindow.SettingsSaveRequested` 处理器（App.xaml.cs）把自启动注册、后处理规则、应用设置作为一个整体保存：任一步失败会回滚之前已保存的部分并抛 `InvalidOperationException` 给设置窗显示。新增需要持久化的设置项时要遵守这个先注册后保存、失败即回滚的模式。

## 代码风格与提交

- C#：4 空格缩进、文件作用域命名空间、类型/方法 `PascalCase`、私有字段 `_camelCase`、接口 `I` 前缀；nullable 与 implicit usings 已启用，保持零警告。
- 按功能区目录组织代码（`Audio/`、`Asr/`、`Hotkeys/` 等），命名空间与目录一致。
- 提交使用 Conventional Commit 前缀 + 简短中文描述，例如 `feat: 增加单实例协调与权限状态提示`。
- 涉及热键、托盘、模型下载、文本写回、UAC 等 Windows-only 行为的改动，在 Linux 上无法验证；需在 PR/说明中注明未经真实 Windows 回归，并参考 `docs/windows-regression-checklist.md`。

## 文档

设计与现状见 `docs/design.md`、`docs/implementation-status-report.md`、`docs/post-processing-design.md`、`docs/punctuation-design.md`。仓库另有 `AGENTS.md`（内容与本文件部分重叠）。
