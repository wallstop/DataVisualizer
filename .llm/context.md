# LLM Agent Instructions

Procedural skills are in the [skills/](./skills/) directory. Frontend entrypoints
(`AGENTS.md`, `CLAUDE.md`, `.cursorrules`, `.windsurfrules`,
`.github/copilot-instructions.md`) are thin pointers to this file. Skills are
standard [Agent Skills](https://agentskills.io) (`SKILL.md` + frontmatter).

## Repository Overview

- **Package**: com.wallstop-studios.data-visualizer
- **Version**: 0.0.37
- **Unity**: 2021.3+
- **Root namespace**: `WallstopStudios.DataVisualizer` (editor: `WallstopStudios.DataVisualizer.Editor`, tests: `WallstopStudios.DataVisualizer.Tests.Editor`)
- **License**: MIT
- **Repository**: https://github.com/wallstop/DataVisualizer
- A Unity UPM package providing a UI Toolkit editor window for managing, inspecting,
  and batch-editing ScriptableObject assets, plus a small runtime API
  (`BaseDataObject`, lifecycle interfaces, display attributes).
- This folder is the git repo root (nested under the Unity project's `Packages/`).
  Open agent frontends with this folder as the workspace root.

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

See the generated [Skills Index](./skills/index.md). Regenerate it after adding or
editing any skill with `pwsh -NoProfile -File scripts/generate-skills-index.ps1`
(validated by `scripts/lint-llm-instructions.ps1`).

## Critical Rules

### C# and Unity

1. Target C# 10 with 4-space indentation and block-scoped namespaces; place `using`
   directives inside the namespace, matching existing files.
2. Format every modified `.cs` file with CSharpier 1.1.2
   (`dotnet tool run csharpier -- format <paths>`); treat formatting as a gate.
3. Resolve analyzer warnings before review.
4. Prefer explicit namespaces so the window's namespace/type tree stays predictable.
5. Avoid runtime reflection and stringly-typed lookups; wire menu items, property
   paths, and analytics IDs with `nameof`.
6. ScriptableObjects end in `Data`, `Settings`, or `Profile`; editor windows end in
   `Window`.
7. Private serialized fields are camelCase with `[SerializeField]`; add
   `[FormerlySerializedAs]` when renaming anything already saved to disk.
8. Do not rename serialized fields, asset paths, or EditorPrefs keys without a
   persistence story (user state JSON and settings assets carry saved data).
9. `InternalsVisibleTo` is not honored for the editor->tests assembly pair in
   Unity's compilation; helpers that tests must exercise are `public` (see
   `ObjectIdExtensions`, `LabelFilterEvaluator`).
10. Guard editor-only APIs under `Runtime/` with `#if UNITY_EDITOR`; the runtime
    assembly must compile without editor references.
11. Unity 6000.5+ compatibility: prefer `EntityId` over the retired
    `GetInstanceID` flow where the migration applies.
12. Custom `Try*` methods return `bool` and expose successful values through `out`
    parameters. Use names such as `*OrNull`, `*OrDefault`, or `*IfAvailable` when a
    method instead returns a sentinel or performs a best-effort action.
13. Assign every custom `out` parameter just in time, directly before each return;
    do not initialize outputs at method entry and rely on later branches to replace
    them.
14. Collection mutation helpers return a changed flag or affected-item count when
    callers can use it for persistence decisions, diagnostics, or observability.
15. Before replacing `RemoveAt` with swap-back or `RemoveAll`, check multiplicity
    and order semantics. A single stable removal is linear, not quadratic; swap-back
    is valid only for unordered collections, and `RemoveAll` is for predicate-based
    bulk removal.
16. Use read-only collection interfaces for consumers. Mutation helpers that resize
    a collection should accept the concrete supported mutable type when a broader
    interface permits invalid inputs or adds measured dispatch cost; specialize only
    for collection shapes used by production callers.
17. Unity generates `.meta` files for every non-dot path; commit them alongside new
    files. Dot-folders (`.llm`, `.github`) never get `.meta` files.
18. Keep tests and agent/dev files out of the npm tarball: `package.json` `files`
    whitelists only `Editor`, `Runtime`, `docs`, and their `.meta` companions.
19. Write explicit ordered comparisons with `<` or `<=`, reversing operands instead
    of using `>` or `>=`. Relational patterns retain the operator required by C#
    syntax. Keep `!=` when expressing genuine inequality or null checks; do not wrap
    `==` in a negation merely to avoid it. Before reversing user-defined operators,
    verify the swapped operands and paired operator preserve behavior.
20. Prefer one declared class, struct, interface, or enum per `.cs` file. Unity object
    types must use a matching filename so Unity can associate the script reliably.
    Keep any deliberately nonconforming regression fixture isolated and document why
    it must model an external project's unsupported layout.
21. Use `foreach` for arrays and concrete collections with value-type enumerators when
    the loop does not need an index. For variables typed as `IReadOnlyList<T>` or
    `IList<T>`, use a counted loop to avoid interface-enumerator allocation. Retain
    `foreach` when the input is a stream or only exposes `IEnumerable<T>`.
22. Never use bitwise `|`, `&`, `|=`, or `&=` to aggregate Boolean results. Evaluate
    every side-effectful operation into its own named Boolean, then combine results
    with `||` or `&&`. Bitwise operators remain correct for flags and numeric values.
23. Give mutable lifecycle/state machines one transition owner. Helpers may gather or
    process data, but they must not scatter direct state writes or scheduler ownership
    across methods; route transitions through one named runner/transition method.
24. Every enum member has an explicit numeric value. Reserve zero for an invalid
    `Unknown`, `None`, or compatibility sentinel marked `[Obsolete("Please use a valid
    value")]`; valid values must be nonzero. Preserve or explicitly migrate existing
    serialized ordinals rather than renumbering them accidentally.
25. Mutable services are instantiable sealed classes. Put state, events, and lifecycle
    callbacks on the instance, and expose only one explicit static shared instance when
    production needs global coordination. Static classes remain appropriate for pure
    functions, extension methods, constants, and platform interop.
26. Within each C# type, order members as: constants, events, delegates, static
    properties, static fields, properties, fields, constructors, static methods,
    methods, then nested types. Within every tier use public, protected, internal,
    private. Do not reorder across conditional-compilation boundaries or static
    initialization dependencies. Run `npm run lint:csharp-member-order`; use its
    `:fix` variant for safe source-slice permutations, then resolve reported barriers
    manually.
27. Name classes, structs, enums, records, and delegates in PascalCase; interfaces
    use PascalCase with an `I` prefix. Do not carry all-caps native typedef spellings
    into managed type names. Use a descriptive prefix such as `Native` when the
    idiomatic name would collide with a Unity or framework type. `.editorconfig`
    enforces these declarations as warnings.
28. Name every C# method in PascalCase without underscores, including test methods.
    The C# source lint enforces this convention independently of IDE diagnostics.

### Skills Discipline

- Skills live at `.llm/skills/<kebab-name>/SKILL.md` (standard Agent Skills format);
  see [manage-skills](./skills/manage-skills/SKILL.md) before creating or editing one.
- Every `.md` file under `.llm/` must stay within the 300-line hard limit
  (enforced by `scripts/lint-file-lengths.ps1`); keep SKILL.md bodies lean and move
  bulk material to `.llm/code-samples/` or `.llm/references/`.
- After editing any skill: regenerate the index, then run `npm run lint:llm`.

### Validation Ladder (Run After Each Change)

```bash
dotnet tool restore
dotnet tool run csharpier -- format Editor Runtime Tests
dotnet tool run csharpier -- check Editor Runtime Tests
npm run lint:csharp-member-order
pwsh -NoProfile -File scripts/generate-skills-index.ps1
pwsh -NoProfile -File scripts/lint-llm-instructions.ps1 -VerboseOutput
pwsh -NoProfile -File scripts/lint-file-lengths.ps1 -VerboseOutput
pwsh -NoProfile -File scripts/tests/run-all.ps1
npm run lint:llm
npm run lint:llm:fix
npm pack
unity -projectPath <path-to-host-project> -batchmode -quit -runTests -testPlatform editmode
unity -projectPath <path-to-host-project> -batchmode -quit -runTests -testPlatform playmode
```

Layers: the pre-commit framework hook runs CSharpier, C# member-order, and the fast
`.llm` checks on staged files;
`npm run lint:llm` is the local gate; CI (`.github/workflows/llm-lint.yml`) runs
everything on ubuntu and windows. CSharpier and member ordering run via the
pre-commit config on staged `.cs` files.

### Testing

- Unity Test Framework; EditMode specs live in `Tests/Editor`, grouped by feature
  (`NamespaceOrderingTests`, `SelectionPersistenceTests`); name methods
  `ShouldExpectationWhenCondition`.
- Gate pull requests on both EditMode and PlayMode suites; aim for coverage on
  ordering, filtering, and cloning paths before tagging a release.
- Add PlayMode coverage for `BaseDataObject` lifecycle callbacks and asset-state
  persistence in a sibling `Tests/Runtime` folder mirroring Unity's layout.

### Commit & Pull Requests

- Short imperative subjects with the subsystem up front (for example
  "Fix settings persistence dirty state"); reference related issue IDs in the body.
- PRs must include: behavior summary, reproduction/validation steps, screenshots or
  GIFs for UI tweaks, and a risk callout plus rollback plan.
- Confirm CSharpier formatting, `npm pack`, and both Unity test suites before
  requesting review.
- Version bumps: change `package.json`, then run `npm run lint:llm:fix` so the
  version line above is synced, and commit both together
  (see [bump-version-release](./skills/bump-version-release/SKILL.md)).

### Shared References

- [forbidden patterns](./references/forbidden-patterns.md) - patterns that must not
  appear in this codebase, with the compliant alternative for each.

### Dev Container, MCP, and Agent Tooling

- The devcontainer (`.devcontainer/`) installs the four coding CLIs as the
  non-root user and refreshes them on create/start; **never** suggest `sudo
  npm` — rerun `bash .devcontainer/install-npm-tools.sh` or rebuild instead.
- Gateway launchers (`codex-zai`, `claude-zai`, `codex-openrouter`,
  `claude-openrouter`) come from `.devcontainer/ai-backends.sh`. They
  self-load the gitignored `.env.local` for keys not already exported
  (preexisting environment variables win), so they work in non-interactive
  shells too; contract tests live in `.devcontainer/tests/`.
- `npm run mcp:sync` regenerates machine-local MCP configs for Claude Code,
  Codex, OpenCode, Nanocoder, VS Code, and Cursor from
  `.llm/mcp/configure.mjs`. Those outputs are gitignored and must never be
  committed.
- Secrets live in gitignored `.env.local` (template: `.env.local.example`);
  config files reference env vars instead of embedding credentials.
- Unity MCP: the **official Unity CLI** MCP server (`unity mcp`, ~140 tools).
  Container agents reach it through the host HTTP bridge
  (`npm run unity:mcp:host` → `host.docker.internal:9020`); host GUI clients
  use `unity mcp configure <client>` directly. See
  [mcp/README.md](./mcp/README.md).
