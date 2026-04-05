# HsAsrDictation 标点能力落地设计

## 1. 目标

这份文档定义的是 `HsAsrDictation` 在现有架构上接入标点能力的落地方式，而不是重新设计一套听写流程。

完成后，行为应当保持下面四点：

1. 录音、识别、写回的主链路不变。
2. 只对**最终识别结果**做标点后处理。
3. **不**把标点加到录音期间的流式预览里。
4. 标点失败时必须回退原文，不能中断写回。

本次采用的标点模型固定为 sherpa-onnx 官方离线中英标点模型：

`sherpa-onnx-punct-ct-transformer-zh-en-vocab272727-2024-04-12-int8`

它适合当前项目的原因是：项目已经基于 `sherpa-onnx` 做本地识别，而当前仓库已有的流式预览能力只是“预览文本”，不是持续上屏；标点放在最终结果后处理层最稳，也最符合当前代码结构。

## 2. 当前仓库现状

当前仓库已经有一条完整的听写主链路，标点能力应该挂在这条链路的末端，而不是插进识别模型内部。

关键位置如下：

- [`src/HsAsrDictation/Settings/AppSettings.cs`](src/HsAsrDictation/Settings/AppSettings.cs) 和 `SettingsService` 负责本地设置读写。
- [`src/HsAsrDictation/Views/SettingsWindow.xaml`](src/HsAsrDictation/Views/SettingsWindow.xaml) 和 `SettingsWindowViewModel` 负责设置页。
- [`src/HsAsrDictation/App.xaml.cs`](src/HsAsrDictation/App.xaml.cs) 负责启动装配和服务初始化。
- [`src/HsAsrDictation/Services/DictationCoordinator.cs`](src/HsAsrDictation/Services/DictationCoordinator.cs) 负责录音结束后的识别、插入和状态流转。
- [`src/HsAsrDictation/Models/ModelProvisioningService.cs`](src/HsAsrDictation/Models/ModelProvisioningService.cs) 和 [`src/HsAsrDictation/Models/ModelManifest.cs`](src/HsAsrDictation/Models/ModelManifest.cs) 负责现有 ASR 模型的下载、解压和校验。

这意味着标点能力最自然的落点是：

`识别完成 -> 标点后处理 -> 通用后处理 -> 文本写回`

而不是：

- 改录音链路
- 改 ASR 引擎
- 改流式预览
- 改插入策略

## 3. 方案边界

### 3.1 做什么

- 增加一个用户可见开关：`启用标点`。
- 增加一个独立的标点服务，负责初始化模型和对最终文本加标点。
- 在最终识别结果写回前接入标点服务。
- 模型缺失、损坏、初始化失败、推理异常时回退原文。

### 3.2 不做什么

- 不做流式中文标点。
- 不把标点结果写回到录音期间的预览层。
- 不把标点模型并入现有 `AsrModelKind`。
- 不新增面向用户的标点高级设置项，比如模型目录和线程数。
- 不改变现有热键、录音、插入、托盘或日志体系。

## 4. 设计落地

### 4.1 设置与配置

现有设置模型以 `AppSettings` 为中心，所以标点开关应该直接加到这个对象里。

建议新增字段：

```csharp
public bool EnablePunctuation { get; init; } = false;
```

要点：

- 默认值必须是 `false`，保证旧配置和新配置的行为一致。
- `Normalize()` 需要保留这个字段，避免旧 `settings.json` 反序列化后丢失默认值。
- `SettingsWindowViewModel` 需要把这个字段带到设置页。
- `SettingsWindow.xaml` 只新增一个复选框，放在现有“选项”区域里即可。

建议文案：

- 复选框：`启用标点`
- 说明：`识别完成后，对最终文本自动补充标点符号。`

### 4.2 模型目录与校验

标点模型单独管理，不混入现有离线模型和流式模型目录。

建议落盘路径：

```text
%LOCALAPPDATA%/HsAsrDictation/models/punctuation/ct-transformer-zh-en-int8/model.int8.onnx
```

建议下载包固定为：

```text
https://github.com/k2-fsa/sherpa-onnx/releases/download/punctuation-models/sherpa-onnx-punct-ct-transformer-zh-en-vocab272727-2024-04-12-int8.tar.bz2
```

约定如下：

- 标点模型目录和 ASR 模型目录分开。
- `model.int8.onnx` 是唯一硬性必需文件。
- 不要求用户保留解压包中的其他文件。
- 路径和线程数都作为内部默认值，不暴露到设置页。
- 当前实现会把下载包先解压到临时目录，再把最终的 `model.int8.onnx` 复制到固定的模型目录里。

当前实现复用现有“下载 -> 解压 -> 校验 -> 重新加载”的思路，校验条件只看最终的 `model.int8.onnx` 是否存在。

### 4.3 标点服务

标点能力建议做成一个独立服务，避免和 ASR 引擎耦合。

当前实现的接口形态：

```csharp
public interface IPunctuationService : IDisposable
{
    bool IsEnabled { get; }
    bool IsReady { get; }

    void Reload(PunctuationRuntimeOptions options);

    string TryAddPunctuation(string text);
}
```

运行时参数建议只保留最小集合：

```csharp
public sealed class PunctuationRuntimeOptions
{
    public bool Enabled { get; init; }
    public string ModelPath { get; init; } = "";
    public int NumThreads { get; init; } = 1;
}
```

实现原则：

1. 没有启用时不初始化模型。
2. 模型文件不存在时视为未就绪。
3. 任何异常都返回原文。
4. 重新加载时先释放旧实例，再初始化新实例。

当前实现使用 sherpa-onnx 的官方 C# API，底层调用走离线标点模型对应的 `OfflinePunctuationConfig` / `OfflinePunctuation` / `AddPunct()` 这一条路，并固定为 `cpu` + `NumThreads=1`。

### 4.4 启动和设置更新

标点服务应在两个时机刷新：

1. 应用启动并读入设置后。
2. 设置保存后。

推荐接线方式：

- `App.xaml.cs` 在现有服务装配中新增标点服务实例。
- 应用启动时调用一次 `Reload(...)`。
- 设置保存后再次调用 `Reload(...)`。
- 如果标点模型当前不可用，不要阻塞主界面和听写主流程，只记录日志并保持未就绪状态。

这和现有 `App.xaml.cs` 的启动风格一致：启动装配、托盘就绪、模型后台准备、设置变更后重新加载。

### 4.5 最终文本写回前接入

标点只应该插在最终文本写回前，随后再进入通用后处理规则。

当前协调器已经负责“录音结束 -> 识别 -> 插入”，所以接入点应该在 `DictationCoordinator` 的最终结果处理处。

推荐流程：

```text
Recording -> Finalizing -> Decoding -> Punctuation -> PostProcessing -> Inserting -> Idle
```

逻辑上等价于：

```csharp
var finalText = recognitionResult.Text;
finalText = _punctuationService.TryAddPunctuation(finalText);
finalText = _postProcessingService.TryProcess(finalText, context);
await _textInsertionService.InsertAsync(finalText, context);
```

注意事项：

- `RecognitionMode.NonStreaming`、`RecognitionMode.Hybrid`、`RecognitionMode.StreamingOnly` 都应该共用同一条“最终文本 -> 标点 -> 写回”路径。
- 流式预览仍然显示原始预览文本，不做标点补全。
- 如果标点结果为空、异常或未就绪，写回原始识别文本。
- 通用后处理规则继续只作用于最终写回文本，不影响录音期间的流式预览。

## 5. 失败、日志与回退

标点能力必须是一个“可选增强层”，不能成为主流程的单点故障。

建议记录的日志至少包括：

- 标点开关状态。
- 标点模型路径。
- 模型是否就绪。
- 标点前后文本长度。
- 初始化失败或推理失败原因。

推荐的回退策略是 fail-open：

- 启用但未下载模型 -> 直接写回原文。
- 模型文件损坏 -> 直接写回原文。
- 初始化异常 -> 直接写回原文。
- 推理异常 -> 直接写回原文。

这能保证用户即使在标点模型异常时，听写主功能也不会被拖垮。

## 6. 验收标准

满足下面这些条件，就说明标点设计在当前仓库里是落地的。

### 6.1 功能

- 设置页可以看到 `启用标点`。
- 关闭标点时，行为和当前版本一致。
- 开启标点并且模型就绪时，最终写回文本会自动补标点。
- 录音期间的流式预览保持原样，不引入中文标点。
- 现有 ASR 下载和识别流程不受影响。

### 6.2 回退

- 标点模型缺失时，听写仍然可用。
- 标点模型损坏时，听写仍然可用。
- 标点初始化失败时，听写仍然可用。
- 标点推理异常时，听写仍然可用。

### 6.3 示例

可用下面的文本做人工验收：

```text
我们都是木头人不会说话不会动
```

期望接近：

```text
我们都是木头人，不会说话，不会动。
```

```text
这是一个测试你好吗How are you我很好thank you are you ok谢谢你
```

期望接近：

```text
这是一个测试，你好吗？How are you? 我很好，thank you. Are you ok? 谢谢你。
```

## 7. 结论

这次标点能力的落地方式，不是给现有系统增加一个新的听写分支，而是在现有主链路末端增加一层稳定的后处理。

这样做的好处是：

- 和当前仓库架构一致。
- 对设置页改动最小。
- 和通用后处理规则链路可以自然串接。
- 对 ASR 模型和流式预览没有侵入。
- 失败可回退，风险可控。
