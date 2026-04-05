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
- 本地识别（离线 + 可选流式）
- 录音期间的流式预览
- 最终文本的离线标点后处理（可开关）
- `SendInput` 注入
- 剪贴板回退
- 最小设置窗
- 本地日志
- 识别模式、输入设备、点击录入的新热键、模型目录、自动下载、剪贴板回退、标点和流式预览等本地配置

不包含：

- 持续边说边写回目标输入框
- 热词偏置
- ITN 显式开关
- 云同步
- 管理员窗口兼容保证

## 2. 实现决策

### 2.1 技术栈

- 桌面壳：`.NET 8 + WPF`，托盘与部分辅助窗口使用 Windows Forms 能力
- 音频：`NAudio`
- ASR：`org.k2fsa.sherpa.onnx`
- 模型解压：`SharpCompress`
- 标点后处理：`sherpa-onnx` 离线标点组件

### 2.2 热键实现

设计稿原先写的是 `RegisterHotKey`，但最终实现改为 `WH_KEYBOARD_LL` 全局低级键盘钩子。

原因：

- `RegisterHotKey` 适合触发一次性动作。
- 本项目需要“按下开始、释放结束”的 PTT 语义。
- 低级键盘钩子可以稳定追踪组合键的按下和释放状态。

默认热键仍为 `Ctrl + Alt + Space`。

### 2.3 音频链路

- 当前实现使用 `WaveInEvent`
- 采样格式固定为 `16 kHz / mono / 16-bit PCM`
- 单次录音上限 `30 秒`
- 结束录音后先做简单头尾静音修剪，再送入 ASR

说明：

- 当前实现先用 `WaveInEvent` 降低首版集成风险。
- 如果后续设备兼容性不足，再评估切换到 `WasapiCapture`。
- 录音期间会产出流式识别预览，但最终写回仍发生在结束录音之后。
- 当前首版没有接入真正的 VAD 模型，只做了能量阈值静音裁剪。

### 2.4 文本回写

优先级固定：

1. `SendInput(KEYEVENTF_UNICODE)`
2. 剪贴板回退

约束：

- 识别到密码框时拒绝注入。
- 高权限窗口导致注入失败时，最多回退到剪贴板粘贴。
- 不抢主窗口焦点；回写前尝试恢复原前台窗口。
- 标点只作用于最终写回文本，不影响录音期间的流式预览。

### 2.5 模型供应

默认模型来源：

- 离线模型：`https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-funasr-nano-int8-2025-12-30.tar.bz2`
- 流式模型：`https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-streaming-paraformer-bilingual-zh-en.tar.bz2`
- 标点模型：`https://github.com/k2-fsa/sherpa-onnx/releases/download/punctuation-models/sherpa-onnx-punct-ct-transformer-zh-en-vocab272727-2024-04-12-int8.tar.bz2`

校验条件：

离线模型：

- `embedding.int8.onnx`
- `encoder_adaptor.int8.onnx`
- `llm.int8.onnx`
- `Qwen3-0.6B/`

流式模型：

- `encoder.int8.onnx`
- `decoder.int8.onnx`
- `tokens.txt`

标点模型：

- `model.int8.onnx`

默认目录：

- `%LOCALAPPDATA%/HsAsrDictation/models`
- `%LOCALAPPDATA%/HsAsrDictation/models/offline`
- `%LOCALAPPDATA%/HsAsrDictation/models/streaming`
- `%LOCALAPPDATA%/HsAsrDictation/models/punctuation`

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
  Overlay/
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
- `SherpaStreamingParaformerEngine`：封装流式 sherpa-onnx 推理
- `PunctuationModelProvisioningService`：标点模型下载、解压、校验
- `SherpaOfflinePunctuationService`：离线标点后处理
- `TextInsertionService`：注入和剪贴板回退
- `TrayIconService`：托盘菜单和气泡通知
- `SettingsService`：本地设置读写

## 4. 状态与异常

状态机固定为：

```text
Idle -> Recording -> Finalizing -> Decoding -> Inserting -> Idle
```

说明：

- 标点后处理发生在写回前，不单独占用状态。
- 录音期间的流式预览只用于提示当前识别进展，不会持续改写目标输入框。

异常处理：

- 录音少于 `150 ms`：直接忽略
- 修剪后无有效语音：提示“未检测到清晰语音”
- 模型未就绪：提示下载或修复模型
- ASR 失败：提示错误信息
- 注入失败：尝试剪贴板回退；仍失败则提示

## 5. 发布方式

发布命令由 [scripts/publish-win-x64.ps1](/root/proj/hs-asr/scripts/publish-win-x64.ps1) 提供，目标是：

- `RuntimeIdentifier=win-x64`
- framework-dependent 发布
- 输出可执行文件及依赖文件
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
- 设置页可点击录入新组合键，并修改输入设备、识别模式、模型目录、自动下载、剪贴板回退、标点和流式预览开关
- 录音期间可以看到流式预览
- 启用标点时，最终文本会先做一次离线标点后处理
- 失败和性能信息可在日志目录看到

## 7. 当前实现限制

- 当前首版没有接入真正的 VAD 模型，只做了能量阈值静音裁剪。
- 当前只提供录音期间的流式预览，不做持续边说边写回目标输入框。
- 标点处理只面向最终写回文本，不会实时修改流式预览。
- 管理员权限窗口、远程桌面、企业 IM 等输入控件还需要在真实 Windows 机器上补充回归测试。
