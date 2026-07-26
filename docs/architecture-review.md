# 架构评估与改进计划（2026-07）

本文档记录一次针对整个仓库的大方向/架构评审结论，以及对应的改进计划与执行状态。评审对象为 `Press Speak`（代码沿用 `HsAsrDictation` 命名）当前主干代码。

总体判断：代码库分层意识良好（纯逻辑与 Win32 拆分、接口抽象、按功能分目录），主要问题集中在**测试工程的结构性方案**、**主链路的并发模型**、**组合根的职责边界**与**设置传播机制**四处，另有若干横切面欠账。

---

## 发现 1：测试架构建立在手工链接文件之上（高优先级，已列入修复）

`tests/HsAsrDictation.Tests.csproj` 通过 85+ 条 `<Compile Include>` 手工链接应用源码，两条硬约束（新文件必须手工登记、被链接文件不能碰 Windows API）完全靠约定维持。

系统性风险：

- **静默漏测**：忘记登记新文件不会报错，覆盖缺口无声积累。
- **依赖闭包靠约定**：被链接文件引入 Windows-only 依赖时，错误以离奇的编译失败形式在测试工程爆出，而非设计时被阻止。
- **同名 shim 类型**：`ForegroundContext` 因 `AutomationElement` 属性变成 Windows-only，测试工程用一份同名同命名空间的 shim 冒充（`ForegroundContextShim.cs`），两份定义可能悄悄漂移。
- 链接清单占测试 csproj 约 80% 篇幅，随每个功能持续增长。

**修复方案**：拆出 `src/HsAsrDictation.Core`（纯 `net8.0`，`RootNamespace=HsAsrDictation`，命名空间不变），应用与测试工程均以 `ProjectReference` 引用。两条硬约束变为编译器强制。`ForegroundContext.FocusedElement` 改为 `object?`，删除 shim。

Core / App 边界原则：能在纯 `net8.0` 零警告编译的进 Core（含仅声明 P/Invoke 的 `Interop/Win32.cs`、带 `[SupportedOSPlatform("windows")]` 标注的 `WindowsStartupRegistrationSecurityValidator`）；运行时 Windows 语义强的留 App（`WaveInAudioCaptureService`、`LowLevelKeyboard*`、`ForegroundContextService`、`TextInsertionService`、`ElevationService`、`SingleInstanceCoordinator`、`WindowsStartupRegistrationPlatform`、托盘/浮窗/通知/窗口）。包引用：sherpa-onnx、SharpCompress 随 Core；NAudio 留 App。

## 发现 2：DictationCoordinator 并发模型脆弱、职责过载（高优先级，已列入修复）

`DictationCoordinator`（约 680 行）存在三类隐患：

- **跨方法的锁所有权**：`_sessionLock` 在 `BeginRecordingAsync` 获取、在 `FinalizeRecordingCoreAsync` 的 `finally` 释放，中间隔着热键释放、尾录延时、音频停止回调三条路径，新增路径极易漏释放/双重释放。
- **两套不一致的同步域**：`_pendingHotkeyReleaseFinalize`/`_finalizationInProgress` 由 `_recordingControlSync` 保护，`_state` 却在 `SetState` 无锁写入、又在 `TryEnterFinalization` 锁内读取；热键钩子回调、音频设备回调、UI 线程三方并发访问同一状态机。
- **事件反复订阅/退订**：`AudioChunkAvailable`、`RecordingStopped` 每次会话订阅，退订散落在正常路径、异常路径、finally 三处。

**修复方案**：引入显式的"录音会话对象"（每次按键创建，持有 channel、streaming session、CTS、设置快照、捕获上下文与状态），生命周期结束整体丢弃；coordinator 只负责会话替换与状态机迁移。解码策略（NonStreaming/Hybrid/StreamingOnly）拆为独立策略方法。

## 发现 3：App.xaml.cs 承担了组合根之外的事务逻辑（中高优先级，已列入修复)

手工 new 接线对该规模是合理选择，问题在于 `App.xaml.cs` 混入了两块**本应可测但测不到**的逻辑：

- **设置保存的事务/回滚编排**（`SettingsSaveRequested` 处理器约 70 行）：先自启动注册、再存规则、再存设置，失败逆序回滚——全仓库最像"事务"的代码写在匿名事件处理器里。
- **启动分支流程**：维护模式 → 账号预检 → 单实例 → UAC 重启的顺序是隐性的（CLAUDE.md 需要专门一节解释它）。

**修复方案**：抽出 `SettingsSaveTransaction`（纯逻辑入 Core，回滚顺序与部分失败场景可单测）；启动分支拆为按固定顺序排列的命名方法（`HandleStartupTaskMaintenance` → `HandleElevationOriginPrecheck` → `HandleElevationOriginMismatch` → `HandleSingleInstanceCoordination` → `HandleAdministratorRestartRequest`），顺序在代码中自明；`App.xaml.cs` 只留接线与 UI 反馈。取舍说明：启动各分支的可测逻辑（`StartupOptions`、单实例消息协议）此前已在 Core 并有测试覆盖，故未再为"顺序"引入额外的状态机抽象。

## 发现 4：设置变更传播是手工且不一致的（中高优先级，已列入修复）

`SettingsService.Current` 是可变共享引用，无变更通知：

- 保存后热键要手动 `UpdateGesture`、模型要手动 `EnsureModelReadyAsync(reinitialize: true)`，刷新逻辑散落在 App.xaml.cs 事件处理器里。
- 录音会话中途各组件随时读 `Current`（尾录时长、`RecognitionMode`、`EnableStreamingPreview` 等），同一会话前后可能读到不同配置。

**修复方案**：`SettingsService` 增加变更事件；录音会话开始时快照一份 `AppSettings` 贯穿整个会话；需要响应设置变化的组件订阅事件而非依赖调用方记得刷新。

## 发现 5：自启动/提权子系统体量与核心功能倒挂（方向性观察，暂不动）

`WindowsStartupRegistrationSecurityValidator`（846 行）+ `WindowsStartupRegistrationPlatform`（576 行）+ command builder / contracts / SDDL 合计约 2400 行，接近仓库四分之一，比 ASR 主链路还大，而交付的功能是"开机自启动（含管理员模式）"。安全校验严谨是对的，但自研整套"可信发布目录校验 + 计划任务 + SDDL"的长期维护成本很高（每一行都是安全敏感代码）。若产品走向正式分发，建议把"安装/信任边界"作为整体重新设计（MSI/MSIX 安装器承担信任边界），而非继续在应用内累积此类代码。本轮不改动该区域逻辑。

**2026-07 补充**：为支持自定义安装目录（如非 C 盘）下的管理员提权，新增了 `WindowsStartupAclRepairService`（App）+ `StartupRegistrationCommandBuilder.BuildAclRepairCommandLine`（Core）——检测到失败原因仅是"应用安装目录自身 ACL/属主问题"时，单独提权 Windows 自带的 `icacls.exe` 修复，再走原有不变的校验/提权流程；绝不放松 `Validate()` 本身，也绝不提权应用自己的 exe/cmd 包装文件（避免自举信任的提权漏洞，详见该服务类顶部注释）。这进一步增加了本区域的代码量，没有改变本条发现的结论——是否走安装器路线仍是后续方向性问题，只是让"自定义安装目录"这个此前完全不可用的场景在现有架构下变得可用。

## 发现 6：横切面欠账（低优先级，部分顺带修复）

- **品牌漂移泄漏到用户界面**：产品叫 Press Speak，但 `"HsAsrDictation"` 作为窗口/通知标题硬编码约 36 处。应收拢为单一常量（随发现 3 顺带处理）。
- **日志是具体类**：`LocalLogService` 以具体类型出现在约 20 个构造函数中，建议后续抽 `ILogService`。
- **设置与规则文件无 schema 版本号**：将来不兼容演进只能靠"反序列化失败回退默认"有损处理，建议尽早加 `schemaVersion`。
- **SettingsWindow 的 MVVM 只做了一半**：约 400 行 code-behind 含数值解析、范围校验，移入 ViewModel 即可被测试覆盖。
- **关闭时不打断在途工作**：`OnExit` 直接 dispose，解码/写回管线不接受 CancellationToken。

---

## 执行顺序与状态

| # | 事项 | 状态 |
|---|------|------|
| 1 | 拆出 `HsAsrDictation.Core`，删除链接文件方案 | 完成（2026-07） |
| 2 | 设置快照与变更传播（`SettingsChanged` 事件 + 会话快照） | 完成（2026-07） |
| 3 | DictationCoordinator 会话对象重构（`RecordingSession` + 单一状态锁；顺带修复"热键释放先于启动完成导致录音不停"的窗口缺陷） | 完成（2026-07） |
| 4 | App.xaml.cs 事务与启动决策抽取（`SettingsSaveTransaction` + 命名启动分支 + `AppInfo.Title` 收拢） | 完成（2026-07） |
| 5 | 自启动子系统成本收益重审 | 观察项 |
| 6 | ILogService / schemaVersion / SettingsWindow 校验下移 / 退出取消 | 待办（后续） |

顺序依据：1 改变项目结构（后续新代码直接进 Core）→ 2 提供会话快照能力 → 3 依赖快照 → 4 收尾组合根。

## 重构后的多维对抗审查结果（2026-07）

重构完成后运行了五维度（并发正确性 / 新旧行为等价 / 项目结构 / 设置事务 / 安全敏感区不变性）的审查，共 9 个发现，其中 6 个经独立验证确认并已修复：

1. **（major，既有缺陷）`StopAsync` 抛异常时跳过流式管线收尾**：流式循环任务滞留在 `ReadAllAsync`，且 `session.Dispose` 与仍在排空的循环竞态释放原生流（最坏 AV 崩溃）。修复：`FinalizeRecordingCoreAsync` 的 finally 统一兜底 `CompleteStreamingAsync`（幂等）后再 Dispose。
2. **（major，既有缺陷，安全区 fail-closed 方向）安全校验器 token 复制级别错误**：提升进程中经 `TokenLinkedToken` 取得的非提升令牌是 Identification 级，`DuplicateToken` 请求 Impersonation 级必以 ERROR_BAD_IMPERSONATION_LEVEL 失败 → 管理员自启创建路径恒失败（不放宽安全但功能不可用）。修复：统一以最低必要的 Identification 级复制（`AccessCheck` 只需 Identification 级）。**需真实 Windows UAC 流程重点回归。**
3. **（minor，既有缺陷）sherpa 引擎 Initialize/Unload 无互斥**：并发初始化泄漏数百 MB 原生识别器；目录变更 reinit 与使用方竞态。修复：两个引擎加 `_initLock` 序列化初始化，识别器替换在解码锁/交换锁内原子完成。已知限制：活跃流式会话持有旧识别器期间发生 reinit 的竞态需引用计数才能彻底解决，留后续。
4. **（minor，重构引入）回滚性 `Save(previousSettings)` 触发同值 `SettingsChanged`**，导致保存失败时无谓重建模型。修复：`SettingsSaveTransaction` 仅在 `Current` 实际变化过时执行设置回滚。
5. **（minor，重构引入）Recording 状态在启动 IO 前发布**，启动失败/缓慢时 UI 假显示"录音中"。修复：状态迁移仍在锁内原子完成（短按窗口修复不受影响），对 UI 的发布推迟到启动成功后，与旧版呈现一致。
6. **（minor，既有缺陷）`EnsurePunctuationReadyAsync` 用长下载前的过期 `EnablePunctuation` 快照**，可把用户已关闭的标点重新启用。修复：加信号量序列化 + 下载后重读当前设置。

3 个发现被独立验证驳回（时序不可达或不属于工作区缺陷）。行为等价性维度确认：三种识别模式取舍、短录音/静音阈值、标点→后处理→写回顺序、尾录语义、通知触发条件等与旧版逐条等价；安全敏感区文件（除上述第 2 项修复外）均为纯移动，启动分支顺序与旧版一致。

所有涉及热键、托盘、UAC、文本写回的行为改动在 Linux 上无法运行验证，合入前需按 `docs/windows-regression-checklist.md` 在真实 Windows 上回归；其中管理员自启创建（上述第 2 项）为本轮必测项。
