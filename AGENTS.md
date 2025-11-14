# Repository Guidelines

## Project Structure & Module Organization
This Unity Package Manager module lives at `Packages/com.wallstop-studios.data-visualizer`. Keep runtime-facing APIs, ScriptableObject base classes, and shared attributes inside `Runtime/` so downstream games can include the package without editor baggage. Place editor windows, UI Toolkit layouts, and menu integrations in `Editor/`. Documentation assets (screens, GIFs, and the `README.md`) stay under `docs/`. Tests are not yet checked in; when you add them, mirror Unity’s layout by creating sibling `Tests/EditMode` and `Tests/PlayMode` folders.

## Build, Test, and Development Commands
- `unity -projectPath <path-to-host-project> -batchmode -quit -runTests -testPlatform editmode` runs EditMode coverage and surfaces compilation issues headlessly.
- `unity -projectPath <path-to-host-project> -batchmode -quit -runTests -testPlatform playmode` exercises runtime lifecycle hooks before promoting releases.
- `npm pack` (from this directory) generates the `.tgz` artifact consumed by scoped registries or `manifest.json` file references.
- `dotnet tool restore` installs the pinned .NET toolset, and `dotnet tool run csharpier -- format Editor Runtime` applies the CSharpier 1.1.2 style (use `-- check` in CI to fail fast).

## Coding Style & Naming Conventions
Target C# 10 with 4-space indentation, file-scoped namespaces, and analyzer warnings resolved before review. Follow Unity conventions: ScriptableObjects end in `Data`, `Settings`, or `Profile`, editor windows end in `Window`, and private serialized fields use camelCase names with `[SerializeField]`. Run the repo-pinned formatter (`dotnet tool run csharpier -- format <paths>`) on every modified file after `dotnet tool restore`; avoid manual line wrapping. Prefer explicit namespaces so the Data Visualizer window keeps its namespace/type tree predictable. Avoid runtime reflection and stringly-typed lookups; expose helpers via `internal` APIs with `InternalsVisibleTo` and depend on `nameof` expressions to wire menu items, property paths, and analytics IDs.

## Testing Guidelines
Leverage Unity Test Framework. Group EditMode specs by feature (`NamespaceOrderingTests`, `SelectionPersistenceTests`) and name methods `Should_<Expectation>_When_<Condition>`. Add PlayMode tests for `BaseDataObject` lifecycle callbacks and asset-state persistence. Gate pull requests on both test suites using the commands above, and aim for coverage on ordering, filtering, and cloning paths before tagging a release.

## Commit & Pull Request Guidelines
Existing history favors short, imperative subject lines (e.g., “Fix saved object selection when ordering >100”) with the subsystem up front. Reference any related issue IDs in the body. Pull requests must include: summary of behavior change, reproduction/validation steps, screenshots or GIFs for UI tweaks, and a risk callout plus rollback plan. Confirm CSharpier formatting, `npm pack`, and both Unity test commands before requesting review.
