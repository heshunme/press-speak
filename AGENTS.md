# Repository Guidelines

## Project Structure & Module Organization
`src/HsAsrDictation/` contains the Windows WPF application targeting `.NET 8`. Keep code grouped by feature area: `Audio/`, `Asr/`, `Hotkeys/`, `Insertion/`, `Models/`, `Settings/`, `Tray/`, and `Views/`. Core orchestration lives in `Services/DictationCoordinator.cs`. Tests live in `tests/HsAsrDictation.Tests/`; current unit coverage focuses on reusable logic such as `AudioSilenceTrimmer` and `ModelManifest`. Supporting docs are in [`design.md`](/root/proj/hs-asr/design.md) and [`implementation-status-report.md`](/root/proj/hs-asr/implementation-status-report.md). Release scripts are under `scripts/`.

## Build, Test, and Development Commands
Use project-file commands because the repo does not include a `.sln`.

- `dotnet build src/HsAsrDictation/HsAsrDictation.csproj`: build the WPF app.
- `dotnet test tests/HsAsrDictation.Tests/HsAsrDictation.Tests.csproj`: run xUnit tests.
- `bash scripts/publish-win-x64.sh Release`: create a self-contained `win-x64` publish in `artifacts/publish/win-x64/`.
- `pwsh ./scripts/publish-win-x64.ps1 -Configuration Release`: PowerShell equivalent for Windows.

## Coding Style & Naming Conventions
Follow existing C# style: 4-space indentation, file-scoped namespaces, `PascalCase` for types and methods, `_camelCase` for private readonly fields, and clear interface names with an `I` prefix. Keep feature code inside the matching folder/namespace, for example `HsAsrDictation.Audio` or `HsAsrDictation.Models`. Nullable reference types and implicit usings are enabled; write code that stays warning-free. No dedicated formatter or linter config is checked in, so match the surrounding style and default SDK analyzers.

## Testing Guidelines
Add xUnit tests in `tests/HsAsrDictation.Tests/` with names like `FeatureNameTests.cs`. Prefer focused method names such as `Trim_RemovesLeadingAndTrailingSilence`. For logic reused from the app project, either reference the source directly as current tests do or add a project reference when test scope expands. There is no coverage gate today, but new behavior should ship with unit tests or a short note explaining why only manual Windows validation is possible.

## Commit & Pull Request Guidelines
Recent history uses Conventional Commit prefixes with short Chinese summaries, for example `feat: 实现听写应用主体功能` and `test: 添加静音裁剪和模型清单测试`. Keep commits small and typed (`feat`, `fix`, `test`, `docs`, `chore`). PRs should describe the user-visible change, list verification steps, and call out Windows-only areas that were not exercised locally, especially hotkeys, tray behavior, model download, and text insertion.
