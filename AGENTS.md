# Repository Guidelines

## Project Structure & Module Organization
- Organize runtime code under `Runtime/` with subfolders such as `DataVisualizer/` for core interfaces like `IDataProcessor` and context types.
- Place supporting utilities in `Runtime/Helper/` and extensions in `Runtime/Extensions/`.
- Keep editor-only UI and tooling under `Editor/DataVisualizer/` with the `.asmdef` files `WallstopStudios.DataVisualizer.asmdef` and `WallstopStudios.DataVisualizer.Editor.asmdef` managing dependencies.
- Treat `Editor/DataVisualizer/DataVisualizer.cs` as a composition root only. Add behaviour to:
  - `State/` for session data (selection, diagnostics, popovers)
  - `Services/` for persistence, AssetDatabase interaction, processor execution, etc.
  - `Controllers/` for UI Toolkit orchestration (namespace, object list, labels, search, drag and drop)
  - `Events/` for new `DataVisualizerEventHub` contracts when controllers need to communicate
- Store shared fonts in `Editor/Fonts/` and USS styles in `Editor/DataVisualizer/Styles/`.
- Update `package.json` whenever the assembly public API changes for Unity 2021.3 projects.

## Build, Test, and Development Commands
- Use the Unity Editor or `unity -projectPath <your-project> -importPackage <path-to-repo>` to consume the package locally.
- Run `csharpier .` before committing to maintain consistent formatting.
- Execute `dotnet tool restore` followed by `dotnet tool run csharpier .` in CI-like environments to match repository expectations.

## Coding Style & Naming Conventions
- Follow standard C# conventions: PascalCase for types, camelCase for locals and private fields, and `[SerializeField] private` for serialized private fields.
- Keep indentation at four spaces and prefer expression-bodied members only when they improve readability.
- Ensure file names mirror the primary class (for example, `DataVisualizerGUIContext.cs` contains `DataVisualizerGUIContext`).
- Run CSharpier after every change and avoid manual styling tweaks that conflict with the generated layout.
- Do not use underscores in function names, especially test function names.
- Do not use regions, anywhere, ever.
- Avoid `var` wherever possible; use expressive types instead.
- Controllers and services should communicate by publishing/subscribing to events. Do not introduce new cross-controller method calls or reach into window internals.

## Testing Guidelines
- Adopt the Unity Test Framework with runtime tests under `Tests/Runtime` and editor tests under `Tests/Editor`.
- Use `[TestFixture]` classes with descriptive names such as `DataVisualizerGUIContextTests`, and name test methods with the `MethodUnderTest_State_ExpectedResult` pattern.
- Run edit mode suites via `unity -projectPath <project> -runTests -testPlatform EditMode` and include the generated `TestResults.xml` when discussing failures.
- Target meaningful coverage for core processors, serializers, and state objects.
- Do not use Description annotations for tests.
- Do not create `async Task` test methods; rely on `IEnumerator`-based Unity test methods instead.
- Do not use `Assert.ThrowsAsync` because it is unavailable.
- When checking UnityEngine.Objects for null, compare directly (`thing != null`, `thing == null`) to respect Unity's object existence rules.
- Do not use underscores in function names, especially test function names.
- Do not use regions, anywhere, ever.
- Avoid `var` wherever possible, use expressive types.
- When tests need to tweak persistence/services, prefer `DataVisualizer.OverrideUserStateRepositoryForTesting` or dedicated hooks instead of editing internal fields directly.

## Commit & Pull Request Guidelines
- Keep commit messages short, present-tense, and imperative (for example, `Add namespace collapse state cache`).
- Group related changes per commit and avoid mixing formatting-only edits with functional updates.
- Provide concise pull request summaries, link issues when applicable, and include before/after screenshots for UI changes.
- Document test coverage or manual verification steps in pull requests.
- Request review from another maintainer before merging into `main`.
