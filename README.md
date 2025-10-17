# DataVisualizer
Data management tooling for Unity projects built on ScriptableObject workflows.

⚠️ **Status:** *alpha*. Expect sharp edges and breaking changes while we stabilize the new architecture. Feedback and bug reports are welcome, but please do not rely on API stability yet.

## Getting Started

1. Clone this repository next to (or inside) your Unity project.
2. From Unity, open **Package Manager → + → Add package from disk…** and select `package.json` in this folder. The package can also be imported via CLI: `unity -projectPath <your-project> -importPackage <path-to-repo>`.
3. Open the window from **Tools → Wallstop Studios → Data Visualizer**.

## Settings & Preferences

Open the gear icon to configure the tooling:

- Toggle persistence mode (Settings asset vs. per-user JSON), enable/disable shortcut and drag hints, and set the default processor logic.
- Export or import user state as JSON to share presets across machines; the import flow reloads the window automatically.
- Reset user state – useful when onboarding a teammate or clearing stale preferences.

## Architectural Overview

The editor window is intentionally thin. It composes a set of testable building blocks that live under `Editor/DataVisualizer/`:

- **State (`State/`)** – In-memory session data such as selections, pagination, diagnostics, and popover visibility. These types are serializable-friendly and drive UI refreshes.
- **Services (`Services/`)** – Pure(ish) logic that talks to Unity APIs, persistence, and the asset database (`IDataAssetService`, `IUserStateRepository`, `ProcessorExecutionService`, etc.). Services expose small abstractions so they can be mocked in tests.
- **Events (`Events/`)** – The `DataVisualizerEventHub` and strongly typed messages (for example `ObjectReorderRequestedEvent`, `DragStateChangedEvent`). Controllers publish to the hub instead of grabbing other controllers directly.
- **Controllers (`Controllers/`)** – UI Toolkit orchestration for specific panels (`NamespacePanelController`, `ObjectListController`, `LabelPanelController`, `DragAndDropController`, …). Each controller receives only the services and state containers it needs and communicates via the event hub.
- **Infrastructure (`Infrastructure/`)** – Lightweight wiring (`DataVisualizerDependencies`) that resolves repositories, event hub, and shared schedulers.

`DataVisualizer` (the window) now focuses on lifecycle, dependency wiring, and simple helpers (popover IDs, test hooks). When you need to extend behaviour, reach for a controller or service rather than adding new logic to the window itself.

## Extending the Tooling

- **Add a processor** – Implement `IDataProcessor`, register it through `DataProcessorRegistry`, and expose any configuration UI via `ProcessorPanelController` hooks. The controller already listens for registry change events and updates the UI automatically.
- **Add new filtering/UI panels** – Create a controller in `Editor/DataVisualizer/Controllers/` and publish any cross-panel communication through `DataVisualizerEventHub` events. Persist state in `VisualizerSessionState` as needed.
- **Customize drag & drop** – Extend `DragAndDropController` or listen to `DragStateChangedEvent`/`ObjectReorderRequestedEvent` to add behaviour based on modifier keys.
- **Testing new features** – Use the helpers in `DataVisualizerDependencies.OverrideUserStateForTesting` to inject fake repositories or services when writing playmode/editmode tests. Do not poke internal window fields directly.

## Testing & Tooling

- Format all code with [CSharpier](https://csharpier.com/):
  ```bash
  dotnet tool restore
  dotnet tool run csharpier .
  ```
- Run edit-mode tests from Unity or via CLI: `unity -projectPath <project> -runTests -testPlatform EditMode`.
- Tests live under `Tests/Editor` (window/controllers/services) and `Tests/Runtime` (shared runtime contracts).
- `DataVisualizer.OverrideUserStateRepositoryForTesting` and `DataVisualizerDependencies.OverrideUserStateForTesting` help inject fakes without touching internals.

## Contributing

- Follow the architecture guidelines above and prefer adding behaviour to services/controllers over the window.
- Avoid using `var` unless the type is painfully obvious; stick to the repository’s conventions outlined in `AGENTS.md`.
- Keep commits small, focused, and formatted. Include `dotnet tool run csharpier .` before pushing.

## License

MIT – see [LICENSE](LICENSE).
