# Forbidden Patterns

Patterns that must not appear in this codebase, with the compliant alternative.

| Forbidden | Use Instead | Why |
| --- | --- | --- |
| `GetInstanceID()` on UnityEngine.Object where Unity 6000.5+ matters | `EntityId`-based flow (see commit "Migrate GetInstanceID to EntityId") | API retirement in newer Unity |
| Hardcoded package id / prefs prefix strings | `PackageId`, `PrefsPrefix` consts | single source of truth |
| Renaming serialized fields without `[FormerlySerializedAs]` | keep old name attribute + new camelCase field | saved assets and user state JSON break silently |
| `UnityEditor` usage in `Runtime/` without `#if UNITY_EDITOR` | wrap in the define | runtime assembly must compile without editor refs |
| Stringly-typed menu items, property paths, analytics IDs | `nameof` expressions | rename-safe lookups (repo rule) |
| Reflection to reach internals from tests | make the helper `public` (`ObjectIdExtensions`, `LabelFilterEvaluator` precedent) | `InternalsVisibleTo` is not honored for editor->tests |
| Reflection in hot paths (per-frame, per-asset loops) | cached delegates, direct calls, or `ReflectionHelper` with caching | window batches over thousands of assets |
| Culture-sensitive sorting in generated files/scripts | ordinal comparer (`StringComparer.Ordinal`) | byte-identical output across OS/CI machines |
| Editor-only API calls in `OnValidate` without `Application.isPlaying` guard | `#if UNITY_EDITOR` + play-mode check (see `BaseDataObject.TrySetAssetPath`) | build/player crashes |
| Growing `DataVisualizer.cs` with new subsystems | new files under `Data/`, `Search/`, `UI/`, `Utilities/` | the main file is already ~9.3k lines |
| Adding `.llm/`, `scripts/`, or pointer files to `package.json` `files` | keep the whitelist Editor/Runtime/Tests/docs only | tarball hygiene for the UPM registry |
| Committing without `.meta` files for new Unity-visible paths | let Unity generate them, then `git add` | missing metas churn guids per machine |
| Editing `.llm/skills/index.md` by hand | `pwsh -NoProfile -File scripts/generate-skills-index.ps1` | file is generated; hand edits drift |
| Markdown files under `.llm/` exceeding 300 lines | split into skills/code-samples/references | context minimalism (hard limit, enforced) |
| Smart quotes / em-dashes in `.llm/skills/*/SKILL.md` frontmatter | ASCII-only frontmatter values | cross-OS index determinism |
| `foreach` over an `IReadOnlyList<T>` / `IList<T>` interface | counted `for`; use `foreach` for arrays/concrete value enumerators | avoid boxed/interface enumerator allocation in hot loops |
| Bitwise Boolean aggregation or mutation inside `|=` / `&=` | named operation results combined afterward with `||` / `&&` | preserve evaluation while making side effects and intent auditable |
| Direct lifecycle-state writes and scheduler changes across helpers | one transition owner and state-machine runner | prevent contradictory transitions and orphaned recurring work |
| Implicit enum values or a valid zero-valued enum member | explicit ordinals plus an obsolete zero sentinel | Unity serializes enum ordinals; reordering implicit members silently changes data |
| Static classes that own mutable service state, events, or lifecycle work | sealed instance service plus one explicit shared instance if needed | instances isolate tests and make ownership visible |
| C# members outside the repository tier/access order, or nested types interspersed with members | `npm run lint:csharp-member-order:fix`, followed by manual barrier fixes | consistent layout keeps APIs scannable and nested declarations from obscuring behavior |
| All-caps native typedef names used as managed type names | descriptive PascalCase names such as `NativeRect` | follows .NET naming conventions without shadowing framework or Unity types |
