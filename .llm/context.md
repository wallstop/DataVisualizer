# LLM Agent Instructions

Procedural skills are in the [skills/](./skills/) directory. Frontend entrypoints (`AGENTS.md`, `CLAUDE.md`, `.cursorrules`, `.windsurfrules`, `.github/copilot-instructions.md`)
are thin pointers to this file. Skills are standard [Agent Skills](https://agentskills.io) (`SKILL.md` + frontmatter).

## Repository Overview

- **Package**: com.wallstop-studios.data-visualizer
- **Version**: 0.1.0
- **Unity**: 2021.3+
- **Root namespace**: `WallstopStudios.DataVisualizer` (editor: `WallstopStudios.DataVisualizer.Editor`, tests: `WallstopStudios.DataVisualizer.Tests.Editor`)
- **License**: MIT
- **Repository**: https://github.com/wallstop/DataVisualizer
- A Unity UPM package providing a UI Toolkit editor window for managing, inspecting, and batch-editing ScriptableObject assets, plus a small runtime API (`BaseDataObject`,
  lifecycle interfaces, display attributes).
- This folder is the git repo root (nested under the Unity project's `Packages/`). Open agent frontends with this folder as the workspace root.

## Project Structure

```text
Editor/                          Editor-only assembly (WallstopStudios.DataVisualizer.Editor.asmdef)
  DataVisualizer/                The editor window and collaborators
    DataVisualizer.cs            Main window (~9.3k lines; search before editing)
    NamespaceController.cs       Namespace/type catalog controller
    Data/                        Persisted state models (user state, ordering, filters)
    Search/                      Global search matching types
    Styles/                      USS stylesheet + style constants
    UI/                          Reusable UI Toolkit controls
    Unity/                       Asset postprocessor
    Utilities/                   ReadOnly attribute + drawer, monitor utility
    Extensions/                  ObjectId, Color, ListView compat, UI extensions
  Fonts/                         Bundled UI font
Runtime/                         Runtime assembly (WallstopStudios.DataVisualizer.asmdef)
  DataVisualizer/                BaseDataObject, lifecycle interfaces, attributes
  Extensions/                    Shared extension methods
  Helper/                        Directory/Path/Reflection helpers
Tests/Editor/                    EditMode tests (WallstopStudios.DataVisualizer.Tests.Editor.asmdef)
docs/                            Screenshots and README assets
scripts/                         PowerShell automation for this .llm harness
.llm/                            This harness (context, skills, code samples, references)
.llm/mcp/                        MCP servers + multi-frontend configurator (see mcp/README.md)
.devcontainer/                   VS Code devcontainer (agent CLIs, Z.ai backends, MCP sync)
```

## Skills Reference

See the generated [Skills Index](./skills/index.md). Regenerate it after adding or editing any skill with `pwsh -NoProfile -File scripts/generate-skills-index.ps1` (validated by
`scripts/lint-llm-instructions.ps1`).

## Critical Rules

### C# and Unity

1. Target C# 10 with 4-space indentation and block-scoped namespaces; place `using` directives inside the namespace, matching existing files. `npm run lint:csharp-usings` enforces
   this; file-level usings are sanctioned only for namespaceless assembly-attribute files and `[assembly: ...]` preambles such as `InternalsVisibleTo` (#124).
2. Format every modified `.cs` file with CSharpier 1.1.2 (print width 180 in `.csharpierrc`; `dotnet tool run csharpier -- format <paths>`) and every modified `.md` file with
   Prettier (print width 180 in `.prettierrc.json`; `npm run format:md`); treat both as gates. Generated and skill Markdown is exempt (`.prettierignore`), and table cells align to
   their widest cell, so table lines may exceed the width.
3. Resolve analyzer warnings before review.
4. Prefer explicit namespaces so the window's namespace/type tree stays predictable.
5. Avoid runtime reflection and stringly-typed lookups; wire menu items, property paths, and analytics IDs with `nameof`.
6. ScriptableObjects end in `Data`, `Settings`, or `Profile`; editor windows end in `Window`.
7. Private serialized fields are camelCase with `[SerializeField]`; add `[FormerlySerializedAs]` when renaming anything already saved to disk.
8. Do not rename serialized fields, asset paths, or EditorPrefs keys without a persistence story (user state JSON and settings assets carry saved data).
9. `InternalsVisibleTo` is not honored for the editor->tests assembly pair in Unity's compilation; helpers that tests must exercise are `public` (see `ObjectIdExtensions`,
   `LabelFilterEvaluator`).
10. Guard editor-only APIs under `Runtime/` with `#if UNITY_EDITOR`; the runtime assembly must compile without editor references.
11. Unity 6000.5+ compatibility: prefer `EntityId` over the retired `GetInstanceID` flow where the migration applies.
12. Custom `Try*` methods return `bool` and expose successful values through `out` parameters. Use names such as `*OrNull`, `*OrDefault`, or `*IfAvailable` when a method instead
    returns a sentinel or performs a best-effort action.
13. Assign every custom `out` parameter just in time, directly before each return; do not initialize outputs at method entry and rely on later branches to replace them.
14. Collection mutation helpers return a changed flag or affected-item count when callers can use it for persistence decisions, diagnostics, or observability.
15. Before replacing `RemoveAt` with swap-back or `RemoveAll`, check multiplicity and order semantics. A single stable removal is linear, not quadratic; swap-back is valid only for
    unordered collections, and `RemoveAll` is for predicate-based bulk removal.
16. Use read-only collection interfaces for consumers. Mutation helpers that resize a collection should accept the concrete supported mutable type when a broader interface permits
    invalid inputs or adds measured dispatch cost; specialize only for collection shapes used by production callers.
17. Unity generates `.meta` files for every non-dot path; commit them alongside new files. Dot-folders (`.llm`, `.github`) never get `.meta` files.
18. Keep tests, docs imagery, and agent/dev files out of the npm tarball: `package.json` `files` whitelists only `Editor`, `Runtime`, and their `.meta` companions (docs imagery
    ships via GitHub, not npm). Static analyzer binaries, Unity metadata/labels, notices, and `-analyzer` response-file arguments must not exist in this package repository; install
    development-only analyzers in the host Unity project instead. Install or verify the pinned analyzer sets with
    `scripts/install-host-errorprone-analyzers.ps1 -HostProject <path>` (`-AnalyzerSet SonarAnalyzer|All` selects sets; add `-VerifyOnly` for an offline integrity check).
19. Write explicit ordered comparisons with `<` or `<=`, reversing operands instead of using `>` or `>=`. Relational patterns retain the operator required by C# syntax. Keep `!=`
    when expressing genuine inequality or null checks; do not wrap `==` in a negation merely to avoid it. Before reversing user-defined operators, verify the swapped operands and
    paired operator preserve behavior.
20. Prefer one declared class, struct, interface, or enum per `.cs` file. Unity object types must use a matching filename so Unity can associate the script reliably. Keep any
    deliberately nonconforming regression fixture isolated and document why it must model an external project's unsupported layout.
21. Use `foreach` for arrays and concrete collections with value-type enumerators when the loop does not need an index. For variables typed as `IReadOnlyList<T>` or `IList<T>`, use
    a counted loop to avoid interface-enumerator allocation. Retain `foreach` when the input is a stream or only exposes `IEnumerable<T>`. The C# source lint flags
    `for (int i = 0; i < x.Length; ...)` loops whose index is used only to read `x[i]`; `.Count` loops stay review-enforced because the declared type is not visible to a source
    check. Owner-enforced in review (PR #98): sweep new and touched loops for this rule before requesting review.
22. Never use bitwise `|`, `&`, `|=`, or `&=` to aggregate Boolean results. Evaluate every side-effectful operation into its own named Boolean, then combine results with `||` or
    `&&`. Bitwise operators remain correct for flags and numeric values.
23. Give mutable lifecycle/state machines one transition owner. Helpers may gather or process data, but they must not scatter direct state writes or scheduler ownership across
    methods; route transitions through one named runner/transition method.
24. Every enum member has an explicit numeric value. Reserve zero for an invalid `Unknown`, `None`, or compatibility sentinel marked `[Obsolete("Please use a valid value")]`; valid
    values must be nonzero. Preserve or explicitly migrate existing serialized ordinals rather than renumbering them accidentally.
25. Mutable services are instantiable sealed classes. Put state, events, and lifecycle callbacks on the instance, and expose only one explicit static shared instance when
    production needs global coordination. Static classes remain appropriate for pure functions, extension methods, constants, and platform interop. Static shared state must
    document why it is static (cross-instance coordination), key by lifecycle owner with weak references where the owner is disposable, unregister on every removal path, and be
    reset by the owning window's cleanup (`AssetGuidTypeIndex.Shared` and the theme-selection ownership registry are the precedents).
26. Within each C# type, order members as: constants, events, delegates, static properties, static fields, properties, fields, constructors, static methods, methods, then nested
    types. Within every tier use public, protected, internal, private. Do not reorder across conditional-compilation boundaries or static initialization dependencies. Run
    `npm run lint:csharp-member-order`; use its `:fix` variant for safe source-slice permutations, then resolve reported barriers manually.
27. Name classes, structs, enums, records, and delegates in PascalCase; interfaces use PascalCase with an `I` prefix. Do not carry all-caps native typedef spellings into managed
    type names. Use a descriptive prefix such as `Native` when the idiomatic name would collide with a Unity or framework type. `.editorconfig` enforces these declarations as
    warnings.
28. Name every C# method in PascalCase without underscores, including test methods. The C# source lint enforces this convention independently of IDE diagnostics.
29. Use `//` for standalone single-line comments and tool control directives only. Write ordinary multi-line explanations as one indented `/* ... */` block. Keep `///` XML
    documentation line-oriented. The C# source lint enforces this rule.
30. Prefer public Unity Editor geometry and display APIs before adding platform-native bindings. If a native boundary remains necessary, verify its ABI with primary platform
    documentation, represent pointer-sized native values with pointer-sized managed types, and document every supported platform branch that was not executed during validation.
31. Represent reversible state, resource cleanup, and mandatory exit work with a named `IDisposable` scope consumed directly by `using`, not a hand-written `try/finally`. Register
    cleanup before the first side effect. Production hot paths prefer copy-safe readonly struct leases backed by reusable owner storage; leases are default-safe and idempotent, and
    `Dispose()` never throws. Retain `try/catch` where the purpose is exception isolation or translation rather than lifetime management.
32. Keep the shipped `Runtime/` assembly, persisted editor-state models under `Editor/DataVisualizer/Data/`, global-search models under `Editor/DataVisualizer/Search/`, asset
    postprocessing under `Editor/DataVisualizer/Unity/`, and the namespace/type catalog controller `Editor/DataVisualizer/NamespaceController.cs` free of `System.Linq`. Use direct
    collection operations and streaming loops so player-facing helpers, state transfers, per-result search aggregation, per-import asset-change handling, and namespace/type catalog
    construction do not introduce avoidable iterator/delegate allocations. Other editor and test code may retain LINQ when clarity outweighs measured cost.
33. Before caching or pooling a temporary collection, first try eliminating the scratch state. Introduce pooling only after measured repeated reuse, with a defined lifetime,
    invalidation owner, retention bound, and overlapping-call safety. Do not create a type-specific pool speculatively; generalize only when multiple proven call sites share the
    same comparer, reset, and retention semantics. Prefer direct scans for genuinely bounded small inputs and per-operation sets for unbounded membership work.
34. Compare and sort package-owned identifiers (asset labels, namespace keys, type names, paths, GUIDs) with `StringComparison.Ordinal` or `StringComparer.Ordinal`; use
    `OrdinalIgnoreCase` when casing is intentionally ignored. Reserve culture-sensitive comparisons for user-authored prose. Bare `Sort()`, `string.Compare(a, b)`,
    `StartsWith(prefix)`, and `OrderBy(key)` on identifiers sort or match with the current culture and reorder across machines. Prefer static
    `string.Equals(a, b, StringComparison)` over instance `a.Equals(...)` so a null operand compares as a value instead of throwing.
35. Never use `Assert.IsNull`/`Assert.IsNotNull`, the `Is.Null`/`Is.Not.Null` constraints, `?.`, `??`, or an implicit bool test on a `UnityEngine.Object`. Unity overloads the
    equality operators, so a destroyed-object wrapper compares null only through `== null` / `!= null`; the other forms use reference equality and miss that lifetime. Write
    `Assert.That(value == null)` / `Assert.That(value != null)`. The C# source lint enforces the assertion and constraint forms; the `?.`/`??`/bool shapes are review-enforced
    (#117).

### Skills Discipline

- Skills live at `.llm/skills/<kebab-name>/SKILL.md` (standard Agent Skills format); see [manage-skills](./skills/manage-skills/SKILL.md) before creating or editing one.
- Every `.md` file under `.llm/` must stay within the 300-line hard limit (enforced by `scripts/lint-file-lengths.ps1`); keep SKILL.md bodies lean and move bulk material to
  `.llm/code-samples/` or `.llm/references/`.
- After editing any skill: regenerate the index, then run `npm run lint:llm`.

### Validation Ladder (Run After Each Change)

Per-iteration speed: run `npm run check:fast` first (formats and lints only the files changed since `origin/main`, and runs only the surface-selected harness self-tests: each changed tooling path maps to the test files that verify it in `harness-scope.psm1`, with unmapped or shared paths failing safe to the full suite; measured single-subject walls are 2.8-4.0s instead of the 32-42s full suite, which CI still runs on every surface change and skips harness self-tests when their surface is unchanged).
When a Unity MCP host is connected (host bridge: `npm run unity:mcp:host`), run Unity suites through it instead of launching batchmode: `tools.unity.run_tests` with a fixture-name
`filter` returns per-test results in seconds (measured 7s for 65 `MonitorUtilityTests` on the 6000.5 host), and `tools.unity.eval` can invoke `Tests/Editor/DocsImageCapture` entry
points for docs captures. Keep eval return values simple strings; the eval wrapper mis-handles heavier reflective results. The full ladder below gates review.

```bash
dotnet tool restore
dotnet tool run csharpier -- format Editor Runtime Tests
dotnet tool run csharpier -- check Editor Runtime Tests
npm run format:md
npm run format:md:check
npm run lint:csharp-member-order
npm run lint:csharp-null-assertions
npm run lint:csharp-usings
npm run lint:changelog-length
pwsh -NoProfile -File scripts/lint-assembly-warnings.ps1
pwsh -NoProfile -File scripts/generate-skills-index.ps1
pwsh -NoProfile -File scripts/lint-llm-instructions.ps1 -VerboseOutput
pwsh -NoProfile -File scripts/lint-file-lengths.ps1 -VerboseOutput
pwsh -NoProfile -File scripts/tests/run-all.ps1
npm run lint:llm
npm pack
unity -projectPath <path-to-host-project> -batchmode -quit -runTests -testPlatform editmode
unity -projectPath <path-to-host-project> -batchmode -quit -runTests -testPlatform playmode
```

Layers: the pre-commit framework hook runs CSharpier, C# member-order, null-assertions, and the fast `.llm` checks on staged files; `npm run lint:llm` is the local gate; CI
(`.github/workflows/llm-lint.yml`) runs everything, including the Prettier Markdown check, on ubuntu and windows. CSharpier and member ordering run via the pre-commit config on
staged `.cs` files.

### Testing

- Unity Test Framework; EditMode specs live in `Tests/Editor`, grouped by feature (`NamespaceOrderingTests`, `SelectionPersistenceTests`); name methods
  `ShouldExpectationWhenCondition`.
- Gate pull requests on both EditMode and PlayMode suites; aim for coverage on ordering, filtering, and cloning paths before tagging a release.
- Add PlayMode coverage for `BaseDataObject` lifecycle callbacks and asset-state persistence in a sibling `Tests/Runtime` folder mirroring Unity's layout.

### Commit & Pull Requests

- Write all agent-to-user copy (PR titles and bodies, commit messages, code comments, handoffs, CHANGELOG entries) in Simplified Technical English: short, plain, active sentences
  answering why, how, and what, in that order.
- Agent-written PR and issue bodies start with `DISCLOSURE: LLM-GENERATED TEXT`. CHANGELOG entries: one or two sentences, 300 rendered characters max (issue links do not count),
  verb-first, user effect first, no mechanism or narration; never modify released sections. CHANGELOG.md stays user-facing only (tests, CI, tooling, and release mechanics belong
  elsewhere).
- PR titles: 50 characters max, imperative, the user-visible effect. PR bodies: `**Why:**` one sentence naming the problem, `**What:**` two to five one-line bullets of at most 12
  words each, then `Fixes`/`Refs`. Validation evidence lives in commits, tests, and issues, never in the body.
- Commit subjects: imperative, 72 characters max, subsystem up front ("Fix settings persistence dirty state"); the body explains why first and references related issue IDs. Confirm
  CSharpier, `npm pack`, and both Unity test suites before requesting review.
- Version bumps: change `package.json`, then run `npm run lint:llm:fix` so the version line above is synced, and commit both together (see
  [bump-version-release](./skills/bump-version-release/SKILL.md)).

### Shared References

- [forbidden patterns](./references/forbidden-patterns.md) - patterns that must not appear in this codebase, with the compliant alternative for each.

### Dev Container, MCP, and Agent Tooling

- The devcontainer (`.devcontainer/`) installs the four coding CLIs as the non-root user and refreshes them on create/start; **never** suggest `sudo npm` — rerun
  `bash .devcontainer/install-npm-tools.sh` or rebuild instead.
- Gateway launchers (`codex-zai`, `claude-zai`, `codex-openrouter`, `claude-openrouter`) come from `.devcontainer/ai-backends.sh`. They self-load the gitignored `.env.local` for
  keys not already exported (preexisting environment variables win), so they work in non-interactive shells too; contract tests live in `.devcontainer/tests/`.
- `npm run mcp:sync` regenerates machine-local MCP configs for Claude Code, Codex, OpenCode, Nanocoder, VS Code, and Cursor from `.llm/mcp/configure.mjs`. Those outputs are
  gitignored and must never be committed.
- Secrets live in gitignored `.env.local` (template: `.env.local.example`); config files reference env vars instead of embedding credentials.
- Unity MCP: the **official Unity CLI** MCP server (`unity mcp`, ~140 tools). Container agents reach it through the host HTTP bridge (`npm run unity:mcp:host` →
  `host.docker.internal:<port>`, default 9020 via `UNITY_MCP_HTTP_PORT`); host GUI clients use `unity mcp configure <client>` directly. See [mcp/README.md](./mcp/README.md).
