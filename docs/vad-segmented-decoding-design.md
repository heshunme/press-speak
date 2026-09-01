# Press Speak 非流式 VAD 分段解码落地设计

## 1. 目标

这份文档定义的是在现有非流式识别链路上接入 VAD 分段解码的落地方式，目的只有一个：**缩短用户松开热键之后的等待时间**。

要解决的实际场景是：用户按住录音键说话，说着说着中间需要思考、停顿一会儿，再继续说。按当前"录完整段再送 ASR"的实现，这些停顿也算进音频长度，解码时间随之拉长，松键后的等待明显。

完成后，行为应当保持下面五点：

1. 热键、录音、写回的主链路语义不变，仍然是"按下录音、松开结束、结束后写回"。
2. 标点和后处理仍然只作用于**最终拼接文本**，不作用于录音期间的预览。
3. VAD 不可用、分段失败时必须能回落到当前的全量解码路径，不能中断听写。
4. 不做增量写回目标输入框，录音期间只更新浮窗预览。
5. 分段解码只作用于 `RecognitionMode.NonStreaming`，混合模式和纯流式不受影响。

## 2. 当前实现与瓶颈

当前非流式路径在 [`src/HsAsrDictation.Core/Services/DictationCoordinator.cs`](../src/HsAsrDictation.Core/Services/DictationCoordinator.cs) 的 `FinalizeRecordingCoreAsync` 里是一次性完成的：

```text
热键松开 → 等尾录（默认 1s）→ StopAsync 拿全量 samples
        → AudioSilenceTrimmer 头尾裁剪 → TranscribeAsync(整段)
        → 标点 → 后处理 → 写回
```

用户感知等待 = `尾录时长 + 整段解码时间 + 标点 + 后处理 + 写回`。

离线引擎是 `sherpa-onnx` 的 FunASR-Nano int8（Qwen3-0.6B 解码的 LLM 式模型，见 [`SherpaFunAsrNanoEngine`](../src/HsAsrDictation.Core/Asr/SherpaFunAsrNanoEngine.cs)），解码耗时随音频长度增长。中间的思考停顿全部计入音频长度。

关键观察：**说话期间 CPU 基本是空闲的**，只有 40ms 一次的 PCM 拷贝（`WaveInAudioCaptureService.OnDataAvailable`）。本方案就是把解码工作搬进这段空闲时间。

## 3. 核心思路

用 VAD 在录音过程中按自然停顿切段，段一封口就立刻送进离线引擎解码。用户松键时，前面段落的文本已经就绪，只剩最后一段需要等待。

```text
WaveIn 40ms chunk
   ├─→ 全量缓冲（保留不变，供降级路径与录音时长上限使用）
   └─→ Channel<float[]> → VAD 循环 → 段队列 → 顺序解码 worker → 有序文本累积
                                                                    ↓
                                                            浮窗累积预览
松键 → 尾录（可自适应跳过）→ StopAsync → Flush 封口最后一段
     → 等 worker 排空 → 拼接 → 标点 → 后处理 → 写回
```

感知等待变成 `尾录 + 最后一段解码 + 队列积压 + 标点 + 后处理 + 写回`。若用户在自然停顿后才松手（常见：说完一句再松键），最后一段往往已解完，只剩收尾。

## 4. 已实测的 VAD API 事实

以下都是在 `org.k2fsa.sherpa.onnx 1.12.34` 上用 `SherpaOnnx.VoiceActivityDetector` + `silero_vad.onnx` 实跑得到的结论（Linux x64，包内自带 runtime），是本方案的设计依据而非推测：

| 事项 | 实测结果 |
|---|---|
| 模型文件 | `silero_vad.onnx`，643 KB，裸 `.onnx`（不是 tar.bz2） |
| 喂入长度 | **不要求**是 `WindowSize`(512) 的整数倍，内部自行缓冲；现有 40ms/640 样本的 chunk 可直接喂 |
| CPU 成本 | 60s 音频耗时 152ms，RTF ≈ 0.0025；单次 40ms chunk 最坏 556 µs |
| 构造成本 | 首次 19ms，之后 3~5ms；`Reset()` < 0.2ms（可跨会话复用实例） |
| `SpeechSegment.Start` | **相对流起点的全局样本偏移**，跨段单调递增 |
| 松键时仍在说话 | `Flush()` 会把当前未封口的段吐出来（实测 2.0s 语音完整取回） |
| `Flush()` 后能否继续 | 可以。flush 后 `IsSpeechDetected()` 归 false，继续喂音频照常出段，`Start` 继续累加 |
| `IsSpeechDetected()` | 提供"此刻是否在说话"的实时信号，用于自适应尾录 |
| `MaxSpeechDuration` | **不可靠**。maxSpeech=5 时首段仍出 10.77s；maxSpeech=8 时 14s 连续语音一段没切。不能用它做硬上限 |
| 缓冲容量 | 构造第二参 `bufferSizeInSeconds`；超出会自动扩容并向 stderr 打 `Overflow!`，无数据丢失但产生噪声输出 |
| `MinSpeechDuration` | 短于该值的语音段被整段丢弃（实测 0.25f 时 0.2s 语音 → 0 段） |

由此得到两条硬性设计约束：

1. **强制切分必须自己实现**（主动调 `Flush()`），不能依赖 `MaxSpeechDuration`。
2. **buffer 容量按录音时长上限给足**，避免 overflow 噪声日志。

## 5. 已确定的产品决策

| 决策项 | 结论 |
|---|---|
| 能力暴露 | 改造 `RecognitionMode.NonStreaming` 本身，另给一个 `启用 VAD 分段解码` 开关可关回旧行为。不新增第四种识别模式 |
| 模型分发 | 随发布包内置。仓库内提交 `silero_vad.onnx`，用 `Content` + `CopyToPublishDirectory` 带进产物，不走网络下载 |
| 录音期反馈 | 浮窗累积预览：把已解码段落拼起来显示，复用现有 `DictationStatus.PreviewText` 通道。**不**增量写回目标输入框 |
| 参数取向 | 激进档 + 自适应尾录：停顿阈值 0.5s、最小批 1.5s、硬上限 10s；松键时若不在说话且队列已空则跳过尾录直接结束 |

### 5.1 内置分发的两个连带影响

**provisioning 层退化。** 因为不走下载，VAD 模型不需要完整的 provisioning 服务，只需要"定位安装目录下的模型文件并校验存在"。不新增下载路径、不碰 `AutoDownloadModel` 语义、不新增模型目录设置项。文件缺失（安装不完整）时直接走第 8 节的降级。

**仓库第一个二进制文件。** 当前仓库最大的 tracked 文件是 38 KB 源码，`silero_vad.onnx` 是 643 KB 二进制。接受这个代价，换取构建可离线、`publish-win-x64.{sh,ps1}` 与 `sync-local-win-install.sh` 三个脚本零改动（后者靠 hash manifest 自动同步）。

### 5.2 预览开关复用

混合模式和纯流式已经在往 `DictationStatus.PreviewText` 写流式假名。非流式分段预览复用同一通道与同一个 `EnableStreamingPreview` 开关——两者不会同时发生（分段只在非流式跑），开关名字略不精确，但避免设置页再长一项。

## 6. 组件划分

按仓库的 Core/App 约定，全部落在 Core（sherpa 包引用在 Core，且这些都是可在 Linux 编译测试的纯逻辑）：

| 文件 | 职责 |
|---|---|
| `Core/Audio/AudioSegment.cs` | `record AudioSegment(long StartSample, float[] Samples)`，带 `EndSampleExclusive` |
| `Core/Audio/IAudioSegmenter.cs` | `AcceptChunk` / `TryDequeue` / `Flush` / `IsSpeechActive` / `Dispose`。纯接口，测试注入假实现 |
| `Core/Audio/SileroVadSegmenter.cs` | 包装 `VoiceActivityDetector`，单线程使用，自己实现硬上限切分 |
| `Core/Audio/IAudioSegmenterFactory.cs` | 按会话创建分段器；返回 null 表示分段不可用 |
| `Core/Audio/SileroVadSegmenterFactory.cs` | 用内置模型创建分段器，模型缺失/构造失败只记日志返回 null |
| `Core/Models/VadModelLocator.cs` | 定位并校验安装目录下的 `silero_vad.onnx` |
| `Core/Services/SegmentedDecodeOptions.cs` | 分段参数（停顿阈值、最小批、硬上限、边界补齐） |
| `Core/Services/SegmentedDecodePipeline.cs` | 流水线本体：VAD 循环、批合并、顺序解码 worker、有序文本累积、预览发布 |

改动既有文件：

- `RecordingSession`：新增 `TryInitializeSegmentedDecode` / `CompleteSegmentedDecodeAsync`（与现有 `InitializeStreamingAsync` / `CompleteStreamingAsync` 对称），`AcceptChunk` 同时喂两条 channel，`PreviewText` 优先返回分段累积文本。因为原生 VAD 必须等循环任务退出后才能释放，`IDisposable` 改为 `IAsyncDisposable`。
- `DictationCoordinator`：`DecodeOfflineAsync` 旁边新增 `DecodeSegmentedAsync(audio, session)` 分支；尾录等待抽出 `WaitForHotkeyReleaseTailAsync` 支持自适应跳过；构造函数新增两个可选参数（分段器工厂、分段参数）供测试注入。
- `AppSettings` / `SettingsWindowViewModel` / `SettingsWindow.xaml`：新增 `EnableVadSegmentedDecoding` 开关。
- `src/HsAsrDictation/HsAsrDictation.csproj`：`Content` 项带上模型文件。

流水线单独成类而不是塞进 `RecordingSession`——后者已经带了一条流式管线，再加一条会变成两套并发状态挤在同一个对象里。

## 7. 关键策略

这五条决定方案成败。

### 7.1 批合并，防止被 LLM 固定开销吃掉收益

LLM 式解码每次调用都有 prefill + 采样的固定成本，切太碎会让总解码时间反而超过整段一次解码。所以 VAD 吐出的段不立即解码，而是累计语音时长 ≥ `MinDecodeBatchSeconds`（默认 1.5s）或录音结束时才派发一批。段内多个 VAD 片段拼成一个连续 buffer 送解码。

### 7.2 自己做硬上限强制切分

一段连续语音超过 `MaxOpenSegmentSeconds`（默认 10s）就主动 `Flush()` 封口，避免"一口气说 60 秒"退化成当前行为。代价是可能切在词中间，边界处识别质量会下降——这是明确接受的取舍。

### 7.3 段边界补齐

这版 VAD 不暴露 pad 参数，实测段边界紧贴语音起止，词头可能被削。由于 `Start` 是全局偏移，可以从自己保留的原始样本里按 `Start ± 120ms` 重切一份送解码。流水线维护滚动缓冲，派发后丢弃已消费部分，内存只占"当前未封口段"量级，不会把整段录音再存一份。

### 7.4 顺序单 worker

离线引擎内部有 `_decodeLock`（[`SherpaFunAsrNanoEngine.cs:123`](../src/HsAsrDictation.Core/Asr/SherpaFunAsrNanoEngine.cs)），并行解码没有收益。单 worker 顺序消费同时天然保证拼接顺序。

### 7.5 自适应尾录

松键时若 `IsSpeechActive == false` 且队列已排空，说明用户在自然停顿后松手，尾录纯属白等，可直接结束。设置里的尾录时长退化为"上限"。常见用法下白拿约 1 秒。

**"队列已排空"必须包含尚未被 VAD 消费的音频。** 录音回调只把音频写进 channel，VAD 循环是另一个任务：如果只看"已切出但未解码的段数"，刚喂进去还没轮到处理的音频会让判断误报为"没活了"，尾录被提前砍掉，还没识别的语音直接丢失。实现上用一对 accepted/processed 计数（先登记后入队）保证宁可短暂多报、绝不漏报；VAD 循环异常退出时把计数追平，否则该标志会永久为真、自适应退化成一直等到上限。这一条有单测覆盖（`HotkeyRelease_WaitsForTail_WhenStillSpeaking`）。

### 7.6 拼接与标点

每段先过 `DictationTextNormalizer.Normalize`，段间默认**无分隔符**直接拼接（中文场景），断句交给标点模型。这也是保留"标点只作用于最终文本"这个既有约定的原因——分段不改变标点/后处理的位置。标点关闭时段间补一个空格，否则读起来会糊。

## 8. 降级与失败处理

分段解码是"性能优化层"，不能成为主链路的单点故障。全量缓冲始终保留，任何分段侧的失败都能回落。

| 场景 | 行为 |
|---|---|
| VAD 模型文件缺失 / 构造失败 | 本次会话走全量解码路径，只记 Warn，不打断录音 |
| VAD 循环抛异常 | 标记 `SegmentationFailed`，丢弃已解码段落，回落全量路径重解 |
| 某一批解码失败 | 该批文本记为空并 Warn，其余段落照常拼接，不整体失败 |
| 无任何段落产出 | 复用现有"未检测到清晰语音"提示 |
| 录音时长上限触发 | 现有 `MaxDurationReached` 路径不变，Flush 后正常收尾 |
| 会话中途保存设置改了热词 | `TranscribeAsync` 会在会话中途重建识别器，同一次听写的不同段可能由不同识别器解码。影响很小，此处记录为已知行为 |

## 9. 状态机与 UI

`DictationState` **不新增状态**。`Recording` 期间后台已在解码；`Decoding` 状态的含义收窄为"收尾解码最后一段"。

累积预览复用现有 `DictationStatus.PreviewText` 与 `DictationOverlayController`，不改浮窗结构。

## 10. 测试

已落地的测试分三层，全部可在 Linux 运行。

**`SegmentedDecodePipelineTests`**（假 `IAudioSegmenter` + 假引擎，9 个）：段序保持与有序拼接、批合并阈值、收尾派发不足一批的余量、单批失败隔离、VAD 循环异常标记失败、预览累积与通知次数、`IsSpeechActive` 收尾归位、`HasPendingWork` 归零、`DisposeAsync` 幂等且释放分段器。

**`DictationCoordinatorSegmentedTests`**（假分段器工厂，8 个）：分段文本写回、开关关闭走整段、模型不可用降级、分段器抛异常降级、全部分批失败降级、自适应尾录跳过、仍在说话时不跳过、混合模式不启用分段。

**`SileroVadSegmenterTests`**（真 VAD 模型，4 个）：按停顿切分与偏移单调递增、语音持续到最后时 `Flush()` 能封口、硬上限强制切分且切点连续、`Flush()` 后可继续使用。模型缺失时整类跳过（可用 `VAD_MODEL_PATH` 指定路径）。

关键分支都做过变异验证：关掉批合并、去掉收尾派发、让单批异常打断解码循环、退回不含未消费音频的 `HasPendingWork`——四处改动分别只让对应的那个测试失败。

**Windows 真机回归**（加进 [`docs/windows-regression-checklist.md`](windows-regression-checklist.md)）：带停顿的长句、一口气长句（触发硬上限）、松键即停、录音时长上限、VAD 模型缺失降级、关闭开关后行为与旧版一致。

## 11. 落地顺序

代码部分已全部落地（模型内置 → 分段器 → 流水线 → 接线 → 设置项 → 文档），本节保留的是**尚未完成的参数校准**。

**待做：在真机上量解码曲线，据此校准参数。** 整个方案的收益取决于 FunASR-Nano 的解码耗时——固定开销多大、RTF 是否小于 1。两条日志已经就位：整段路径打 `离线解码耗时（整段）`，分段路径每批打 `离线解码耗时（分段）`，都含音频时长、解码毫秒数和 RTF。按回归清单 6.5 跑一轮即可读到。

**如果分段批次的 RTF 持续大于 1**（LLM 式模型在 CPU 上很可能），解码追不上说话速度，队列会越积越长，松键后等的是队列积压而不是最后一段。那时激进档的正确形态是**调大** `SegmentedDecodeOptions.MinDecodeBatch` 而不是调小。这属于参数校准，不是方案变更。

参数集中在 `Core/Services/SegmentedDecodeOptions.cs`，改默认值不涉及其他文件。

## 12. 验收标准

### 12.1 功能

- 设置页可见 `启用 VAD 分段解码`，关闭后行为与当前版本完全一致。
- 开启后，非流式模式下带停顿的长句，松键后的等待明显短于关闭时。
- 录音期间浮窗能看到已解码段落的累积文本。
- 目标输入框在录音期间不被写入，仍然只在结束后一次性写回。
- 混合模式与纯流式行为不受影响。

### 12.2 回退

- VAD 模型文件缺失时，听写仍然可用（走全量解码）。
- VAD 构造失败时，听写仍然可用。
- 单批解码失败时，其余段落仍能写回。
- 分段管线异常时，能回落全量解码并写回完整文本。
