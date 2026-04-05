# HsAsrDictation 通用后处理规则系统落地指导文档

## 1. 目标

在现有听写主链路中新增一个**通用后处理规则系统**，作用于最终写回文本。第一期目标：

* 内置若干默认规则，默认启用
* 支持用户通过 GUI 修改规则
* 以“英语缩写去空格”为首个 happy path
* 规则失败不影响主流程，最差回退原始文本

第一期不做：

* LLM 后处理
* 场景化规则切换
* 实时流式预览后处理
* WFST / 大规模 ITN 引擎

---

## 2. 接入位置

在现有链路中新增一步：

```text id="01"
ASR 最终文本
  -> 可选离线标点
  -> 通用后处理规则系统
  -> TextInsertionService
```

在 `DictationCoordinator` 中接入：

```csharp id="02"
var finalText = recognizedText;

if (settings.EnablePunctuation)
{
    finalText = punctuationService.TryAddPunctuation(finalText);
}

if (settings.EnablePostProcessingRules)
{
    finalText = postProcessingService.TryProcess(finalText, context);
}

await textInsertionService.InsertAsync(finalText, context);
```

`context` 第一版先保留最小结构，后续便于扩展场景化：

```csharp id="03"
public sealed class RuleExecutionContext
{
    public string? ProcessName { get; init; }
    public string? WindowTitle { get; init; }
    public bool IsPasswordField { get; init; }
}
```

---

## 3. 目录结构

新增一个 `PostProcessing` 模块：

```text id="04"
src/HsAsrDictation/
  PostProcessing/
    Abstractions/
      IPostProcessingRule.cs
      IPostProcessingService.cs
      IPostProcessingRuleRepository.cs
    Engine/
      PostProcessingService.cs
      RuleExecutionContext.cs
      RuleApplyResult.cs
    Models/
      PostProcessingConfig.cs
      RuleDefinition.cs
    Resources/
      PostProcessing/
        default-rules.json
    Rules/
      ExactReplaceRule.cs
      RegexReplaceRule.cs
      EnglishAcronymJoinRule.cs
      TrimWhitespaceRule.cs
    Validation/
      RuleValidator.cs
      RegexSafetyValidator.cs
```

---

## 4. 规则类型

第一期只支持三类规则。

### 4.1 ExactReplaceRule

固定文本替换。

适合：

* 热词纠正
* 固定术语替换
* 品牌名修正

示例：

* `chat gpt` -> `ChatGPT`
* `vs code` -> `VS Code`

### 4.2 RegexReplaceRule

正则替换。

适合：

* 多空格压缩
* 固定格式修正
* 简单结构化改写

### 4.3 BuiltInTransformRule

内建代码规则。

适合：

* 逻辑比普通正则更复杂
* 需要额外保护逻辑
* 需要在 GUI 里以“高层语义”暴露，而不是直接暴露复杂正则

首个 happy path 放在这里：

* `EnglishAcronymJoinRule`

---

## 5. 默认内置规则

当前实现内置 4 条规则，默认启用，来自嵌入资源 `Resources/PostProcessing/default-rules.json`。

### 5.1 trim-whitespace

* 类型：built_in_transform
* 作用：去掉首尾空白

### 5.2 normalize-fullwidth-space

* 类型：exact_replace
* 作用：全角空格替换为半角空格
* 说明：当前实现里它是一个内置配置规则，不单独建 `NormalizeFullwidthSpaceRule.cs`

### 5.3 collapse-multiple-spaces

* 类型：regex_replace
* 作用：连续多个半角空格压成一个
* 说明：当前实现里它是一个内置配置规则，不单独建 `CollapseRepeatedWhitespaceRule.cs`

参数：

```json id="05"
{
  "pattern": "[ \\t]{2,}",
  "replacement": " "
}
```

### 5.4 english-acronym-join

* 类型：built_in_transform
* 作用：将 `G P T` 合并为 `GPT`

---

## 6. 规则执行顺序

当前实现按照 `Order` 升序执行，数值越小越早执行。

默认规则的 `Order` 依次为：

1. `builtin.trim-whitespace` -> `100`
2. `builtin.normalize-fullwidth-space` -> `200`
3. `builtin.collapse-multiple-spaces` -> `300`
4. `builtin.english-acronym-join` -> `400`

说明：

* 不再做“按类型分组再执行”的二次排序。
* 不再额外做一次末尾 trim。

---

## 7. 统一规则模型

使用统一 JSON 模型存储规则：

```csharp id="06"
public sealed class RuleDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Kind { get; set; } = ""; // exact_replace | regex_replace | built_in_transform
    public bool IsEnabled { get; set; } = true;
    public bool IsBuiltIn { get; set; } = false;
    public int Order { get; set; } = 1000;
    public JsonObject Parameters { get; set; } = new();
}
```

---

## 8. 配置文件设计

采用“双层配置”。

### 8.1 内置默认规则

放资源文件：

```text id="07"
Resources/PostProcessing/default-rules.json
```

### 8.2 用户覆盖文件

放本地目录：

```text id="08"
%LOCALAPPDATA%/HsAsrDictation/postprocessing-rules.user.json
```

---

## 9. 配置合并策略

启动时执行：

1. 读取嵌入资源 `default-rules.json`
2. 读取 `postprocessing-rules.user.json`
3. 按规则 `Id` 合并
4. 生成最终运行规则集

规则 ID 必须固定，例如：

* `builtin.trim-whitespace`
* `builtin.normalize-fullwidth-space`
* `builtin.collapse-multiple-spaces`
* `builtin.english-acronym-join`

合并规则：

* 用户层没有该 ID：使用默认规则
* 用户层有同 ID：使用用户覆盖值
* 用户新增规则：追加到结果中

---

## 10. 服务接口

### 10.1 核心接口

```csharp id="09"
public interface IPostProcessingService
{
    string TryProcess(string input, RuleExecutionContext context);
}

public interface IPostProcessingRule
{
    string Id { get; }
    string Name { get; }
    int Order { get; }
    bool IsEnabled { get; }

    bool CanApply(RuleExecutionContext context);
    RuleApplyResult Apply(string input, RuleExecutionContext context);
}

public sealed class RuleApplyResult
{
    public string Output { get; init; } = "";
    public bool Changed { get; init; }
    public string? TraceMessage { get; init; }
}
```

### 10.2 配置仓储接口

```csharp id="10"
public interface IPostProcessingRuleRepository
{
    PostProcessingConfig Load();
    void Save(PostProcessingConfig config);
    void ResetBuiltInOverride(string ruleId);
}
```

---

## 11. PostProcessingService 实现要求

`PostProcessingService` 负责：

* 读取最终规则集
* 按顺序执行
* 收集 trace
* 单条规则失败时跳过
* 整个后处理失败时回退原文

参考实现：

实际代码里还包含 `TestProcess(...)` 和内部的 `ExecutePipeline(...)`，这里仅保留关键骨架，说明它依赖仓储加载配置并通过工厂实例化规则。

```csharp id="11"
public sealed class PostProcessingService : IPostProcessingService
{
    private readonly IPostProcessingRuleFactory _factory;
    private readonly ILogger _logger;
    private readonly IPostProcessingRuleRepository _repository;

    public PostProcessingService(
        IPostProcessingRuleRepository repository,
        IPostProcessingRuleFactory factory,
        ILogger logger)
    {
        _repository = repository;
        _factory = factory;
        _logger = logger;
    }

    public string TryProcess(string input, RuleExecutionContext context)
    {
        return ExecutePipeline(input, context).Output;
    }
}
```

---

## 12. Happy Path：EnglishAcronymJoinRule

## 12.1 目标效果

输入：

* `G P T` -> `GPT`
* `C P U 温度` -> `CPU 温度`
* `A P I 文档` -> `API 文档`
* `G P T 4` -> `GPT 4`

保持不变：

* `hello world`
* `abc`
* `A`
* `A 1 B`
* `example@gmail.com`
* `https://a.b.com`

## 12.2 默认参数

```json id="12"
{
  "transformName": "english_acronym_join",
  "minLetters": 2,
  "maxLetters": 8,
  "preserveCase": true,
  "onlyAsciiLetters": true
}
```

## 12.3 判定条件

只在以下条件同时满足时合并：

1. 候选由 2 到 8 个 token 组成
2. 每个 token 都是单个英文字母
3. token 之间只有空格或制表符
4. 前后边界不是连续英文单词内部
5. 不在 email / URL 内部

## 12.4 内部匹配模式

```regex id="13"
(?<![A-Za-z])(?:[A-Za-z](?:[ \t]+[A-Za-z]){1,7})(?![A-Za-z])
```

## 12.5 规则实现

```csharp id="14"
using System.Text.RegularExpressions;

public sealed class EnglishAcronymJoinRule : IPostProcessingRule
{
    private readonly int _order;
    private readonly bool _isEnabled;
    private readonly int _minLetters;
    private readonly int _maxLetters;

    public EnglishAcronymJoinRule(int order, bool isEnabled, int minLetters = 2, int maxLetters = 8)
    {
        _order = order;
        _isEnabled = isEnabled;
        _minLetters = minLetters;
        _maxLetters = maxLetters;
    }

    public string Id => "builtin.english-acronym-join";
    public string Name => "英语缩写去空格";
    public int Order => _order;
    public bool IsEnabled => _isEnabled;

    public bool CanApply(RuleExecutionContext context) => true;

    public RuleApplyResult Apply(string input, RuleExecutionContext context)
    {
        if (string.IsNullOrEmpty(input))
        {
            return new RuleApplyResult { Output = input, Changed = false };
        }

        var changed = false;

        var output = AcronymRegex().Replace(input, match =>
        {
            var letters = 0;
            foreach (var ch in match.Value)
            {
                if (!char.IsWhiteSpace(ch))
                {
                    if (!IsAsciiLetter(ch))
                        return match.Value;
                    letters++;
                }
            }

            if (letters < _minLetters || letters > _maxLetters)
                return match.Value;

            if (LooksLikeUrlOrEmailContext(input, match.Index, match.Length))
                return match.Value;

            changed = true;
            return RemoveWhitespace(match.Value);
        });

        return new RuleApplyResult
        {
            Output = output,
            Changed = changed,
            TraceMessage = changed ? "Joined spaced English acronym." : null
        };
    }

    private static bool IsAsciiLetter(char ch)
        => (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z');

    private static string RemoveWhitespace(string value)
    {
        Span<char> buffer = stackalloc char[value.Length];
        var count = 0;

        foreach (var ch in value)
        {
            if (!char.IsWhiteSpace(ch))
                buffer[count++] = ch;
        }

        return new string(buffer[..count]);
    }

    private static bool LooksLikeUrlOrEmailContext(string input, int index, int length)
    {
        var start = Math.Max(0, index - 16);
        var end = Math.Min(input.Length, index + length + 16);
        var slice = input[start..end];

        return slice.Contains("://", StringComparison.Ordinal) ||
               slice.Contains("@", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"(?<![A-Za-z])(?:[A-Za-z](?:[ \t]+[A-Za-z]){1,7})(?![A-Za-z])",
        RegexOptions.CultureInvariant)]
    private static partial Regex AcronymRegex();
}
```

---

## 13. ExactReplaceRule 实现

```csharp id="15"
public sealed class ExactReplaceRule : IPostProcessingRule
{
    private readonly string _id;
    private readonly string _name;
    private readonly int _order;
    private readonly bool _isEnabled;
    private readonly string _find;
    private readonly string _replace;
    private readonly StringComparison _comparison;

    public ExactReplaceRule(
        string id,
        string name,
        int order,
        bool isEnabled,
        string find,
        string replace,
        bool ignoreCase)
    {
        _id = id;
        _name = name;
        _order = order;
        _isEnabled = isEnabled;
        _find = find;
        _replace = replace;
        _comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }

    public string Id => _id;
    public string Name => _name;
    public int Order => _order;
    public bool IsEnabled => _isEnabled;

    public bool CanApply(RuleExecutionContext context) => true;

    public RuleApplyResult Apply(string input, RuleExecutionContext context)
    {
        if (string.IsNullOrEmpty(_find))
            return new RuleApplyResult { Output = input, Changed = false };

        var output = input.Replace(_find, _replace, _comparison);

        return new RuleApplyResult
        {
            Output = output,
            Changed = !string.Equals(input, output, StringComparison.Ordinal)
        };
    }
}
```

---

## 14. RegexReplaceRule 实现

```csharp id="16"
using System.Text.RegularExpressions;

public sealed class RegexReplaceRule : IPostProcessingRule
{
    private readonly string _id;
    private readonly string _name;
    private readonly int _order;
    private readonly bool _isEnabled;
    private readonly Regex _regex;
    private readonly string _replacement;

    public RegexReplaceRule(
        string id,
        string name,
        int order,
        bool isEnabled,
        string pattern,
        string replacement,
        RegexOptions options = RegexOptions.None)
    {
        _id = id;
        _name = name;
        _order = order;
        _isEnabled = isEnabled;
        _replacement = replacement;
        _regex = new Regex(pattern, options, TimeSpan.FromMilliseconds(100));
    }

    public string Id => _id;
    public string Name => _name;
    public int Order => _order;
    public bool IsEnabled => _isEnabled;

    public bool CanApply(RuleExecutionContext context) => true;

    public RuleApplyResult Apply(string input, RuleExecutionContext context)
    {
        var output = _regex.Replace(input, _replacement);

        return new RuleApplyResult
        {
            Output = output,
            Changed = !string.Equals(input, output, StringComparison.Ordinal)
        };
    }
}
```

---

## 15. 正则安全要求

用户自定义正则必须满足：

1. 保存前先校验语法
2. 运行时必须设置 timeout
3. GUI 必须提供测试输入 / 输出预览
4. 非法表达式禁止保存
5. 执行超时只影响该条规则，不影响主流程

校验器示例：

```csharp id="17"
public static class RegexSafetyValidator
{
    public static (bool ok, string? error) Validate(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return (false, "Pattern 不能为空。");

        try
        {
            _ = new Regex(pattern, RegexOptions.None, TimeSpan.FromMilliseconds(100));
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
```

---

## 16. 默认规则 JSON 示例

`Resources/PostProcessing/default-rules.json`

```json id="18"
{
  "version": 1,
  "isEnabled": true,
  "rules": [
    {
      "id": "builtin.trim-whitespace",
      "name": "清理首尾空白",
      "description": "去掉文本首尾的空白字符",
      "kind": "built_in_transform",
      "isEnabled": true,
      "isBuiltIn": true,
      "order": 100,
      "parameters": {
        "transformName": "trim_whitespace"
      }
    },
    {
      "id": "builtin.normalize-fullwidth-space",
      "name": "全角空格转半角空格",
      "description": "将全角空格替换为普通空格",
      "kind": "exact_replace",
      "isEnabled": true,
      "isBuiltIn": true,
      "order": 200,
      "parameters": {
        "find": "　",
        "replace": " ",
        "ignoreCase": false
      }
    },
    {
      "id": "builtin.collapse-multiple-spaces",
      "name": "压缩连续空格",
      "description": "将多个连续空格或制表符合并为一个空格",
      "kind": "regex_replace",
      "isEnabled": true,
      "isBuiltIn": true,
      "order": 300,
      "parameters": {
        "pattern": "[ \\t]{2,}",
        "replacement": " ",
        "options": "None"
      }
    },
    {
      "id": "builtin.english-acronym-join",
      "name": "英语缩写去空格",
      "description": "将 G P T 这类按字母分开的英语缩写合并为 GPT",
      "kind": "built_in_transform",
      "isEnabled": true,
      "isBuiltIn": true,
      "order": 400,
      "parameters": {
        "transformName": "english_acronym_join",
        "minLetters": 2,
        "maxLetters": 8,
        "preserveCase": true,
        "onlyAsciiLetters": true
      }
    }
  ]
}
```

---

## 17. 规则仓储实现要求

### 17.1 启动加载

* 从资源读取默认规则
* 从本地读取用户覆盖
* 合并后返回

### 17.2 保存

* 只写本地用户层
* 内置规则的修改保存为 override
* 用户新增规则直接写入用户层

### 17.3 恢复默认

* 删除指定规则 `Id` 的 override
* 下次加载时自动回到默认值

---

## 18. GUI 设计

在现有设置页新增一个页签：

* 页签名：`后处理规则`

页面分三块。

### 18.1 顶部

* `启用后处理规则` 总开关
* 说明：`作用于最终写回文本，失败时自动回退原文本`

### 18.2 左侧规则列表

使用 `DataGrid`，列如下：

* 启用
* 顺序
* 名称
* 类型
* 内置
* 描述

### 18.3 右侧规则详情

根据规则类型切换编辑面板。

#### ExactReplaceRule

* 名称
* 描述
* 查找文本
* 替换文本
* 是否忽略大小写
* 启用

#### RegexReplaceRule

* 名称
* 描述
* Pattern
* Replacement
* Options
* 启用

#### BuiltInTransformRule（英语缩写去空格）

* 名称
* 描述
* 最少字母数
* 最多字母数
* 启用

### 18.4 底部测试区

* 输入文本
* 测试按钮
* 输出文本
* 执行 trace

---

## 19. GUI 命令要求

页面至少支持这些操作：

* 新增规则
* 复制规则
* 删除规则
* 上移
* 下移
* 恢复默认
* 测试
* 保存

规则删除规则：

* 内置规则不能真删除，只能禁用
* 用户规则可删除

---

## 20. ViewModel 建议

```csharp id="19"
public sealed class PostProcessingRulesViewModel : ObservableObject
{
    public bool IsRuleSystemEnabled { get; set; }

    public ObservableCollection<RuleItemViewModel> Rules { get; } = new();

    public RuleItemViewModel? SelectedRule { get; set; }

    public string TestInput { get; set; } = "";
    public string TestOutput { get; set; } = "";
    public string TestTrace { get; set; } = "";

    public ICommand AddRuleCommand { get; }
    public ICommand DuplicateRuleCommand { get; }
    public ICommand DeleteRuleCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public ICommand ResetRuleCommand { get; }
    public ICommand TestCommand { get; }
    public ICommand SaveCommand { get; }
}
```

---

## 21. 测试要求

至少补以下单元测试。

### 21.1 EnglishAcronymJoinRule

* `G P T -> GPT`
* `A P I 文档 -> API 文档`
* `G P T 4 -> GPT 4`
* `A -> A`
* `A 1 B` 不改
* email 不改
* URL 不改
* 超长字母链不改

### 21.2 RegexReplaceRule

* 合法 regex 可执行
* 非法 regex 保存失败
* timeout 不拖垮主流程

### 21.3 配置合并

* 默认规则 + override 合并正确
* 恢复默认正确

### 21.4 执行顺序

* 顺序不同结果不同，验证 `Order` 生效

---

## 22. 日志要求

至少记录：

* 规则系统是否启用
* 加载规则总数
* 命中的规则 ID
* 单条规则异常
* regex timeout
* 后处理总前后文本长度
* 整体失败时是否回退原文

---

## 23. 手工验收步骤

1. 启动应用，打开设置页，能看到“后处理规则”
2. 默认规则已加载且默认启用
3. 输入测试文本 `G P T`，测试区输出 `GPT`
4. 实际听写时，说出 `G P T`，最终写回为 `GPT`
5. 禁用“英语缩写去空格”后，写回恢复为 `G P T`
6. 调整 `maxLetters` 后，行为随配置变化
7. 新增一条用户规则后能立即生效
8. 输入非法 regex 时，GUI 阻止保存
9. 单条规则抛异常时，主流程仍正常写回原始文本

---

## 24. 第一阶段实施顺序

按下面顺序做，不要并行摊太开。

### 第一步

搭建 `PostProcessing` 模块和基础接口。

### 第二步

实现规则配置模型、默认规则文件、仓储加载与合并。

### 第三步

实现三类规则：

* `ExactReplaceRule`
* `RegexReplaceRule`
* `EnglishAcronymJoinRule`

### 第四步

实现 `PostProcessingService` 并接入 `DictationCoordinator`。

### 第五步

在设置页增加“后处理规则”页签和规则编辑 GUI。

### 第六步

补齐单元测试和日志。

### 第七步

做真实 Windows 手工回归。

---

## 25. 本期交付完成标准

满足以下条件即算完成：

* 最终写回前能执行规则链
* 默认规则自动加载
* GUI 可查看、编辑、启用、禁用、排序规则
* `G P T -> GPT` 在测试区和真实写回链路都生效
* 非法 regex 不能保存
* 规则失败不影响主流程
* 配置本地持久化
* 单元测试覆盖 happy path 和异常路径
