---
name: create-editmode-test
description: Write EditMode tests in Tests/Editor for the Data Visualizer package following repo conventions - assembly references, Should_When_ naming, TestCaseData-driven cases, and public-helper access rules. Use when adding or modifying Unity tests.
metadata:
  category: Feature
---

# Skill: Create EditMode Tests

## Assembly Facts

- Tests live in `Tests/Editor` against
  `WallstopStudios.DataVisualizer.Tests.Editor.asmdef`: references
  `UnityEditor.TestRunner`, `UnityEngine.TestRunner`,
  `WallstopStudios.DataVisualizer.Editor`; `includePlatforms: ["Editor"]`;
  `precompiledReferences: ["nunit.framework.dll"]`;
  `defineConstraints: ["UNITY_INCLUDE_TESTS"]`.
- Namespace is `WallstopStudios.DataVisualizer.Tests.Editor`; `using` directives go
  inside the block-scoped namespace.
- Run headlessly: `unity -projectPath <host-project> -batchmode -quit -runTests
  -testPlatform editmode`.

## Naming

Methods are `Should_<Expectation>_When_<Condition>`; classes end in `Tests` and are
grouped by feature (`NamespaceOrderingTests`, `SelectionPersistenceTests`,
`LabelFilterEvaluatorTests`).

## Structure Pattern

- Build inputs through small private static helpers instead of constructors inline in
  assertions.
- Prefer data-driven cases with `TestCaseData` + `SetName` so failures read as
  scenario names (see `LabelFilterEvaluatorTests`).
- One assertion cluster per behavior; cover null/empty inputs explicitly.

See the full template: [EditModeTestTemplate.cs](../../code-samples/EditModeTestTemplate.cs).

## Accessing Editor Internals

`InternalsVisibleTo` is NOT honored for the editor->tests assembly pair in Unity's
compilation. If a test needs an internal helper, make the helper `public` in the
editor assembly (precedent: `ObjectIdExtensions`, `LabelFilterEvaluator`). Never
use reflection in tests to reach internals.

## Coverage Priorities

Ordering, filtering, and cloning paths must have coverage before a release; add
PlayMode coverage in a sibling `Tests/Runtime` folder for `BaseDataObject` lifecycle
callbacks and asset-state persistence.

## Related Skills

- [extend-runtime-api](./extend-runtime-api/SKILL.md) - what runtime behavior needs
  lifecycle tests
- [modify-data-visualizer](./modify-data-visualizer/SKILL.md) - editor behavior under
  test
