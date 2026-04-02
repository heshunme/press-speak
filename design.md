# HsAsrDictation 实施设计

## 1. 目标与交付

本项目交付一个 `Windows 11 x64` 常驻听写程序 `HsAsrDictation.exe`，行为固定为：

1. 全局热键按下开始录音。
2. 热键释放结束录音。
3. 本地 CPU 使用 `sherpa-onnx / Fun-ASR-Nano int8` 完成离线识别。
4. 将文本写回触发时所在的输入位置。
5. 程序不内置模型；首次运行或设置页可下载模型到本地目录。

当前仓库的 MVP 范围包含：

- 托盘常驻
- 全局 PTT 热键
- 麦克风录音
- 模型自动下载与校验
- 本地识别
- `SendInput` 注入
- 剪贴板回退
- 最小设置窗
- 本地日志

不包含：

- 实时流式上屏
- 热词偏置
- ITN 显式开关
- 云同步
- 管理员窗口兼容保证

## 2. 实现决策

### 2.1 技术栈

- 桌面壳：`.NET 8 + WPF`
- 音频：`NAudio`
- ASR：`org.k2fsa.sherpa.onnx`
- 模型解压：`SharpCompress`

### 2.2 热键实现

设计稿原先写的是 `RegisterHotKey`，但最终实现改为 `WH_KEYBOARD_LL` 全局低级键盘钩子。

原因：

- `RegisterHotKey` 适合触发一次性动作。
- 本项目需要“按下开始、释放结束”的 PTT 语义。
- 低级键盘钩子可以稳定追踪组合键的按下和释放状态。

默认热键仍为 `Ctrl + Alt + Space`。

### 2.3 音频链路

- MVP 使用 `WaveInEvent`
- 采样格式固定为 `16 kHz / mono / 16-bit PCM`
- 单次录音上限 `30 秒`
- 结束录音后先做简单头尾静音修剪，再送入 ASR

说明：

- 设计稿建议 WASAPI；当前实现先用 `WaveInEvent` 降低首版集成风险。
- 如果后续设备兼容性不足，再切换到 `WasapiCapture`。

### 2.4 文本回写

优先级固定：

1. `SendInput(KEYEVENTF_UNICODE)`
2. 剪贴板回退

约束：

- 识别到密码框时拒绝注入。
- 高权限窗口导致注入失败时，最多回退到剪贴板粘贴。
- 不抢主窗口焦点；回写前尝试恢复原前台窗口。

### 2.5 模型供应

默认模型来源：

- `https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-funasr-nano-int8-2025-12-30.tar.bz2`

校验条件：

- `embedding.int8.onnx`
- `encoder_adaptor.int8.onnx`
- `llm.int8.onnx`
- `Qwen3-0.6B/`

默认目录：

- `%LOCALAPPDATA%/HsAsrDictation/models`

## 3. 代码结构

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
  Services/
  Settings/
  Tray/
  Views/
tests/HsAsrDictation.Tests/
scripts/
```

核心职责：

- `DictationCoordinator`：串起状态机、录音、识别和文本回写
- `LowLevelKeyboardHotkeyManager`：全局热键按下/释放检测
- `WaveInAudioCaptureService`：录音与样本缓存
- `ModelProvisioningService`：模型下载、解压、校验
- `SherpaFunAsrNanoEngine`：封装 sherpa-onnx 推理
- `TextInsertionService`：注入和剪贴板回退
- `TrayIconService`：托盘菜单和气泡通知
- `SettingsService`：本地设置读写

## 4. 状态与异常

状态机固定为：

```text
Idle -> Recording -> Finalizing -> Decoding -> Inserting -> Idle
```

异常处理：

- 录音少于 `150 ms`：直接忽略
- 修剪后无有效语音：提示“未检测到清晰语音”
- 模型未就绪：提示下载或修复模型
- ASR 失败：提示错误信息
- 注入失败：尝试剪贴板回退；仍失败则提示

## 5. 发布方式

发布命令由 [scripts/publish-win-x64.ps1](/root/proj/hs-asr/scripts/publish-win-x64.ps1) 提供，目标是：

- `RuntimeIdentifier=win-x64`
- 自包含发布
- 单文件 `.exe`
- 模型文件不打进单文件，放在外部目录

建议产物目录：

- `artifacts/publish/win-x64/`

## 6. 验收标准

至少满足以下场景：

- 应用启动后仅显示托盘图标，不弹主界面
- 按住热键说话，释放后开始识别
- 首次无模型时可以下载模型
- Notepad 中可以成功回写文本
- `SendInput` 失败时可回退到剪贴板粘贴
- 设置页可修改热键、输入设备和模型目录
- 失败和性能信息可在日志目录看到

## 7. 当前实现限制

- 当前开发环境是 Linux，仓库内代码已按 Windows 目标实现，但 `.exe` 产物需要安装 `.NET 8 SDK` 后再执行发布脚本。
- 当前首版没有接入真正的 VAD 模型，只做了能量阈值静音裁剪。
- 管理员权限窗口、远程桌面、企业 IM 等输入控件还需要在真实 Windows 机器上补充回归测试。
