# Extending the window

The runtime assembly is part of the package and carries the extension surface, so your types compile against it in both the editor and a player build. Everything on this page is
public API. Code identifiers keep the `DataVisualizer` name — namespaces, types, and assemblies are unchanged by the rebrand, so your code needs no edits.

Each code block is a separate example. Put each ScriptableObject class in a file with the same name. Place processors, editor queries and automation examples in an Editor folder;
runtime examples compile in editor and player assemblies.

## Display attributes

`[CustomDataVisualization]` overrides how a type appears in the catalog.

```csharp
using UnityEngine;
using WallstopStudios.DataVisualizer;

[CustomDataVisualization(Namespace = "Weapons", TypeName = "Weapon")]
public sealed class WeaponData : ScriptableObject
{
}
```

`Namespace` replaces the namespace group the type is filed under. Without it the window uses the last segment of your C# namespace, so `MyGame.Items.Weapons` appears under
`Weapons`, and a type in no namespace appears under `No Namespace`.

`TypeName` replaces the type's display name in the list, in the type filter, and in the dialogs that name the type. Without it the C# type name is used.

With the Odin Inspector available, the attribute also carries `UseOdinInspector`, which defaults to `true`. See [Odin Inspector](#odin-inspector).

## BaseDataObject

`BaseDataObject` is an abstract `ScriptableObject` that carries identity and the lifecycle hooks. Derive from it instead of `ScriptableObject` to get them.

```csharp
using UnityEngine;
using WallstopStudios.DataVisualizer;

public sealed class ItemData : BaseDataObject
{
    [SerializeField]
    private int value;
}
```

It provides:

- `Id`, a persisted identifier. In the editor, an empty or whitespace ID is initialized from the asset GUID once an asset path is available. Existing nonempty IDs are preserved,
  including authored IDs that differ from the asset GUID. Validation, rename, and move do not synchronize them. Player builds read the serialized ID without AssetDatabase access.
- `Title`, the display name. A blank title falls back to the asset name, so you only set it when the asset name is not what you want to show.
- `Description`.
- Ordering by title, then asset name, then Id, then description.

The serialized fields `_assetGuid`, `_title`, and `_description` appear under a **Base Data** header. Only `_assetGuid` is read-only; title and description remain editable.

Use the asset GUID when identifying a file through editor APIs. Do not assume an authored `Id` equals that GUID or is unique across independently authored assets.

## Lifecycle hooks

Each hook is a `virtual` method on `BaseDataObject` and also a standalone interface, so you can implement one without deriving.

| Interface     | Methods                                                                           | When                                       |
| ------------- | --------------------------------------------------------------------------------- | ------------------------------------------ |
| `IDuplicable` | `BeforeClone(ScriptableObject previous)`, `AfterClone(ScriptableObject previous)` | Around [Clone](managing-assets.md#clone)   |
| `ICreatable`  | `BeforeCreate()`, `AfterCreate()`                                                 | Around [Create](managing-assets.md#create) |
| `IRenamable`  | `BeforeRename(string newName)`, `AfterRename(string newName)`                     | Around [Rename](managing-assets.md#rename) |

Override only what you need, and call the base implementation when retaining its identity and title behavior. The default implementations already do the work you would otherwise
repeat: `BeforeClone` clears the inherited ID so the copy does not share the original's identity, and `AfterClone` initializes the empty ID from the new asset path and applies the
`(Clone)`, `(Clone 1)`, `(Clone 2)`, … suffix to the title.

The rename example guards its editor-only call so it also compiles in a player assembly:

```csharp
using WallstopStudios.DataVisualizer;
#if UNITY_EDITOR
using UnityEditor;
#endif

public sealed class ItemData : BaseDataObject
{
    public override void BeforeRename(string newName)
    {
        // Audit before the file on disk changes.
    }

    public override void AfterRename(string newName)
    {
#if UNITY_EDITOR
        EditorUtility.SetDirty(this);
#endif
    }
}
```

## Custom inspector content

Implement `IGUIProvider` and return a `VisualElement` from `BuildGUI`. The window adds it below the standard inspector.

```csharp
using UnityEngine.UIElements;
using WallstopStudios.DataVisualizer;

public sealed class ItemData : BaseDataObject
{
    public override VisualElement BuildGUI(DataVisualizerGUIContext context)
    {
        return new Label("Weight: ") { name = "weight-label" };
    }
}
```

Return `null` to add nothing. `BaseDataObject` implements `IGUIProvider` and returns `null` by default, so a derived type only overrides the method when it has something to show.

The `DataVisualizerGUIContext` carries the `SerializedObject` for the selected asset in the editor, which is what you need to write back through. The context type has no
editor-only members in a player build, so a `BuildGUI` override compiles in both.

## Display name

Implement `IDisplayable` to control the name shown for an object. `BaseDataObject` implements it through `Title`. For a plain `ScriptableObject`, implement it yourself:

```csharp
using UnityEngine;
using WallstopStudios.DataVisualizer;

public sealed class ItemData : ScriptableObject, IDisplayable
{
    [SerializeField]
    private string displayName;

    public string Title => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
}
```

The row label, the search result name, and the drag ghost all read `Title` when the type provides it.

## Processors

A processor is a plain class implementing `IDataProcessor`. The window discovers every implementation in your project, so no registration is needed. Place this example in an
**Editor** folder; its `UnityEditor` calls are editor-only. Use the `ItemData` example from [BaseDataObject](#basedataobject), whose public `Description` property is available
here.

```csharp
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using WallstopStudios.DataVisualizer;

public sealed class TrimDescriptions : IDataProcessor
{
    public string Name => "Trim descriptions";

    public string Description => "Removes surrounding whitespace from item descriptions.";

    public IEnumerable<Type> Accepts => new[] { typeof(ItemData) };

    public void Process(Type type, IEnumerable<ScriptableObject> objects)
    {
        foreach (ScriptableObject obj in objects)
        {
            if (obj is ItemData item)
            {
                item.Description = item.Description?.Trim();
                EditorUtility.SetDirty(item);
            }
        }
    }
}
```

- `Name` is the button label and sorts the list.
- `Description` is the button tooltip.
- `Accepts` limits the processor to the types you list, and the processor is only offered for a selected type that appears in that list. Return `null` or an empty list and the
  processor is never offered.
- `Process` receives the type and the objects, which is the **ALL**, **FILTERED** or **SELECTED** set chosen in the window. See [Processors](managing-assets.md#processors) for
  scope, the confirmation, and the load-in-progress refusal.
- `WillEffect(Type, IEnumerable<ScriptableObject>)` is a default interface method that returns the number of objects. Override it when a processor will skip some.

The window instantiates each processor with its public parameterless constructor. A processor that is abstract, generic, or lacks that constructor is skipped and logged. Exceptions
from `Process` are caught and shown as uncertain target outcomes in the preview workspace; inspect partial changes before retrying.

The window saves assets and refreshes after a processor runs, so call `EditorUtility.SetDirty` on anything you change.

## Read-only editor queries

Use `EditorAssetQuery` on Unity's editor main thread without opening a window. It uses the same text matcher (multi-term OR) and label evaluator as the window. Scope contains exact
ScriptableObject types: derived instances and subassets are excluded. An empty scope is rejected instead of searching everything. Results use asset GUIDs and assembly-qualified
type identities; authored `BaseDataObject.Id` values are separate.

Place this example under an `Editor` folder. It uses [QuestData from Getting started](getting-started.md#create-your-first-asset); replace the type and namespace for your project:

```csharp
namespace MyGame.Editor
{
    using MyGame.Data;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.DataVisualizer.Editor.Utilities;

    public static class QuestQueries
    {
        [MenuItem("Tools/List Quest Assets")]
        public static void ListQuests()
        {
            using (EditorAssetQuery query = new(new[] { typeof(QuestData) }, "quest"))
            {
                while (!query.IsComplete && !query.IsWaitingForEditor)
                {
                    query.Advance();
                }
                foreach (EditorAssetRecord record in query.GetPage(0, 100))
                {
                    Debug.Log(record.Guid + " " + record.Path + " " + record.Error);
                }
                Debug.Log("Complete: " + query.IsComplete);
            }
        }
    }
}
```

For interactive tools, keep the query alive and call `Advance(4)` once per editor update, then read pages (up to 1,000 items). Dispose it on cancellation, reload, or owner cleanup.
`IsComplete` means discovery and extraction finished; currently available pages and counts can be incomplete. Play transitions and AssetDatabase activity defer advancement.
`LongestSliceMilliseconds` and `SliceOverrunCount` include indivisible Unity discovery/loading calls; the budget is cooperative, not a hard latency guarantee.

The service copies result values and retains no asset wrappers. It loads each discovered candidate once for labels and matching, without saving, dirtying, or invoking package
mutation hooks. User-defined Unity loading callbacks still run. Item errors appear in `Error`; callers must inspect them even after completion. A query is a one-pass observation,
not a transaction: restart after project changes, and do not persist page offsets across queries. Discovery scans project `.asset` main assets through the existing type index.
Configuration, selection, asset mutations, and persistent metadata caching are outside this initial read-only API.

## Explicit processor preview and apply

`EditorProcessorOperation` runs on the editor main thread. Construct it with an `IDataProcessor` instance, one exact concrete ScriptableObject type and a nonempty list of asset
GUIDs. Preview lists exact paths, rejects duplicates, subassets, wrong types and non-writable paths outside `Assets`. It does not load assets, call processor methods, dirty or save
assets. It previews targets, not changes that arbitrary processor code might make.

```csharp
using (EditorProcessorOperation operation = new(processor, typeof(QuestData), assetGuids))
{
    foreach (EditorProcessorTarget target in operation.Targets)
    {
        Debug.Log(target.Guid + " " + target.Path);
    }
    // Ask the user to confirm these exact targets before calling Apply.
    operation.Apply();
    // Interactive tools call Advance once per editor update instead of looping.
    for (int step = 0; step <= assetGuids.Length && !operation.IsFinished; step++)
    {
        operation.Advance(0);
    }
    if (!operation.IsFinished)
    {
        operation.Cancel();
    }
    Debug.Log(operation.Status + " " + operation.Code + " " + operation.Detail);
}
```

The snippet belongs inside an editor method, with the Utilities namespace imported; supply your processor and explicit GUID array. Keep the operation alive across confirmation and
editor updates. `PreparedCount` reports loading progress. Dispose or cancel before invocation to stop without calling `Process`. Play transitions reject mutation; Unity
AssetDatabase activity pauses preparation. A moved, deleted, wrong-type or non-writable target invalidates the entire preview before invocation, including targets loaded on an
earlier update. Create and confirm a new preview after such a change. Asset content edits are not transactional snapshots and loading can invoke user Unity callbacks.

`Process` receives the entire batch once, preserving existing processor semantics. Its execution, saving and refresh cannot yield or be cancelled. Successful return reports
`applied`; this means the batch returned and saving succeeded, not that each asset changed. Exceptions report `outcome_unknown` for every target because arbitrary code may have
changed any subset, including assets outside the scope. No automatic Undo, rollback, retry or recovery is promised. Inspect the assets and use version control to recover.
Configuration and individual create/clone/rename/move/delete services remain outside this processor API.

## Configure editor preferences from C#

`EditorConfigurationService` operates on an explicit `DataVisualizerSettings` instance without opening a window. The browser uses the same service for its data-folder and active
selection controls. For example, inside an editor method:

```csharp
var settings = UnityEditor.AssetDatabase.LoadAssetAtPath<WallstopStudios.DataVisualizer.Editor.Data.DataVisualizerSettings>(
    "Assets/Editor/DataVisualizerSettings.asset");
var configuration = new WallstopStudios.DataVisualizer.Editor.Utilities.EditorConfigurationService(settings);
// Read and validate without dirtying, saving, creating folders, or opening dialogs.
UnityEngine.Debug.Log(configuration.DataFolderPath);
WallstopStudios.DataVisualizer.Editor.Utilities.EditorConfigurationService.ValidateDataFolder("Assets");
bool changed = configuration.Apply("Assets", selectActiveObject: true);
```

Apply requires an existing folder with its canonical, case-exact path under `Assets`. It validates before changing either preference, rejects Play Mode/transition and busy editor
states, and saves only the explicit writable settings asset. Repeating the same request returns `false` and does not save other pending edits. Transient settings can also be
configured; they become dirty but have no file to save. A save exception can leave changed preferences in memory; inspect the settings and resolve the write failure before
retrying.

This contract covers `DataFolderPath` and `selectActiveObject`. It preserves saved arrangements, persistence mode, and theme selection; their migration contracts and individual
asset operations remain outside this service. Read preferences again after external edits rather than retaining an earlier snapshot.

## JSON editor automation

Schema version 1 exposes `query`, `validate`, processor preview/apply, and configuration read/preview/apply through `EditorAutomationSession`, using the same services as the C#
APIs and browser. See the [request schema](automation-request.schema.json) and [response schema](automation-response.schema.json). Unknown JSON members are ignored; use the schema
in client tooling. Other individual asset-operation requests return `unsupported_operation`.

Configuration uses `readConfiguration`, `previewConfiguration` and `applyConfiguration`. Supply an explicit `settingsGuid` for a `DataVisualizerSettings` main asset under `Assets`.
Preview and apply also require `dataFolderPath` and `selectionMode`: `keep` leaves Unity's active selection alone when inspecting assets; `select` selects inspected assets. Preview
returns the proposed preferences without changing or saving settings. Read returns current preferences. Apply requires `confirmPaths` containing the exact settings path from
preview.

For example, inside an editor method, replace the settings path if your project uses another asset:

```csharp
var request = new WallstopStudios.DataVisualizer.Editor.Utilities.EditorAutomationRequest
{
    schemaVersion = 1,
    operation = "previewConfiguration",
    settingsGuid = UnityEditor.AssetDatabase.AssetPathToGUID("Assets/Editor/DataVisualizerSettings.asset"),
    dataFolderPath = "Assets",
    selectionMode = "select",
};
using (var preview = WallstopStudios.DataVisualizer.Editor.Utilities.EditorAutomationSession.Start(UnityEngine.JsonUtility.ToJson(request)))
{
    var response = UnityEngine.JsonUtility.FromJson<WallstopStudios.DataVisualizer.Editor.Utilities.EditorAutomationResponse>(preview.GetResponseJson());
    if (response.code != "preview")
    {
        UnityEngine.Debug.LogError(response.detail);
        return;
    }
    UnityEngine.Debug.Log(response.items[0].path); // Review this settings target before applying.
    // After reviewing the target, submit the same preferences with:
    request.operation = "applyConfiguration";
    request.confirmPaths = new[] { response.items[0].path };
    using (var apply = WallstopStudios.DataVisualizer.Editor.Utilities.EditorAutomationSession.Start(UnityEngine.JsonUtility.ToJson(request)))
    {
        UnityEngine.Debug.Log(apply.GetResponseJson());
    }
}
```

Configuration is one synchronous unit executed by `Start`, including apply. It completes before the session returns; `Cancel` cannot interrupt it or undo a completed write. Use
`previewConfiguration` first and start apply only after reviewing its settings target. `applied` and `unchanged` distinguish writes from idempotent requests. Settings changes do
not alter the editor's current selection immediately. Apply rejects Play Mode/transitions, busy editor state, read-only settings and invalid folders without dialogs. Requests
require offset zero; later response paging does not repeat the operation. `configuration_failed` with `outcome_unknown` means a write failed after mutation may have started: read
current preferences and inspect disk before retrying. This contract covers only the two preferences documented above; persistence migration, theme and individual asset operations
remain outside it. File execution validates its output path before starting configuration.

Inside an editor method, generate a query request using an exact type from your project, then keep the session alive across editor updates:

```csharp
var request = new WallstopStudios.DataVisualizer.Editor.Utilities.EditorAutomationRequest
{
    schemaVersion = 1,
    operation = "query",
    typeIdentities = new[] { typeof(MyGame.Data.QuestData).AssemblyQualifiedName },
    text = "intro quest",
    pageSize = 100,
};
using (var session = WallstopStudios.DataVisualizer.Editor.Utilities.EditorAutomationSession.Start(UnityEngine.JsonUtility.ToJson(request)))
{
    session.Advance(4);
    UnityEngine.Debug.Log(session.GetResponseJson());
}
```

The snippet performs one slice, then cancels unfinished work on disposal. A longer-lived tool calls `Advance(4)` on each update, reads `GetResponseJson(offset, count)` pages, and
disposes the session on owner cleanup. `Cancel()` preserves partial results. Queries keep multi-term OR matching, exact type scopes, and label-expression semantics. Pages follow
discovery order; restart after edits and never reuse offsets across sessions. `availableCount` is a partial count until `complete` is true. `scopeCount` is -1 while query discovery
is incomplete. Validation findings can arrive for an earlier asset when a later duplicate ID appears.

Processor requests use one `typeIdentities` entry, explicit `assetGuids`, and `processorIdentity` containing the processor's assembly-qualified identity. No text or label filters
are accepted. Use `operation: "previewProcessor"` first; it neither constructs the processor nor loads assets. Read **all** pages of targets, inspect their paths and obtain
confirmation. For apply, resend those identities with `operation: "applyProcessor"` and `confirmPaths` containing every preview path in GUID order. Missing or changed paths reject
the whole request. `Advance` prepares targets and invokes the same `EditorProcessorOperation` batch used by C# callers. Never automatically retry `failed` responses: inspect
`outcome_unknown` targets and recover through version control. `processedCount` counts prepared targets, not proven individual mutations. Cancellation stops preparation only.

For example, after supplying your own exact identities and asset GUID:

```json
{
    "schemaVersion": 1,
    "operation": "previewProcessor",
    "typeIdentities": ["MyGame.Data.QuestData, Assembly-CSharp"],
    "processorIdentity": "MyGame.Editor.TrimDescriptions, Assembly-CSharp-Editor",
    "assetGuids": ["0123456789abcdef0123456789abcdef"]
}
```

Use actual `typeof(QuestData).AssemblyQualifiedName` and `typeof(TrimDescriptions).AssemblyQualifiedName` values, including assembly version information when present. After
reviewing the response, change the operation to `applyProcessor` and add `"confirmPaths": ["Assets/Data/Quest.asset"]` using the exact returned path. The existing
[processor example](#processors) supplies the implementation; these placeholder identities and GUID are not executable fixtures.

For a runnable preview/apply example, first add `ItemData` and `TrimDescriptions` from [Processors](#processors). Put this class in an `Editor` folder, select one or more
`ItemData` main assets in the Project view, and choose **Tools → Preview selected item processor**. The menu rejects mixed selections and shows every path before apply. It keeps
one JSON session for all response pages; it does not repeat apply to fetch outcomes.

```csharp
namespace MyGame.Editor
{
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.DataVisualizer.Editor.Utilities;

    public static class SelectedItemProcessor
    {
        [MenuItem("Tools/Preview selected item processor")]
        public static void Run()
        {
            List<string> guids = new();
            foreach (UnityEngine.Object selected in Selection.objects)
            {
                if (!(selected is ItemData) || !AssetDatabase.IsMainAsset(selected))
                {
                    throw new InvalidOperationException("Select only ItemData main assets.");
                }
                guids.Add(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(selected)));
            }
            if (guids.Count == 0 || 1000 < guids.Count)
            {
                throw new InvalidOperationException("Select 1 to 1000 ItemData assets.");
            }
            EditorAutomationRequest request = new()
            {
                schemaVersion = 1,
                operation = "previewProcessor",
                typeIdentities = new[] { typeof(ItemData).AssemblyQualifiedName },
                processorIdentity = typeof(TrimDescriptions).AssemblyQualifiedName,
                assetGuids = guids.ToArray(),
                pageSize = 1000,
            };
            using EditorAutomationSession preview = EditorAutomationSession.Start(JsonUtility.ToJson(request));
            EditorAutomationResponse targets = JsonUtility.FromJson<EditorAutomationResponse>(preview.GetResponseJson());
            if (!targets.complete || targets.code != "preview")
            {
                throw new InvalidOperationException(targets.code + ": " + targets.detail);
            }
            string[] paths = new string[targets.items.Length];
            for (int i = 0; i < paths.Length; i++)
            {
                paths[i] = targets.items[i].path;
            }
            if (!EditorUtility.DisplayDialog("Run Trim descriptions?", string.Join("\n", paths), "Run", "Cancel"))
            {
                return;
            }
            request.operation = "applyProcessor";
            request.confirmPaths = paths;
            using EditorAutomationSession apply = EditorAutomationSession.Start(JsonUtility.ToJson(request));
            for (int step = 0; step <= guids.Count && !apply.IsFinished && !apply.IsWaitingForEditor; step++)
            {
                apply.Advance(0);
            }
            if (!apply.IsFinished)
            {
                apply.Cancel("editor_busy");
            }
            Debug.Log(apply.GetResponseJson());
        }
    }
}
```

This small-selection example advances synchronously and disposes unfinished work if the editor is busy. For interactive bulk tools, keep the same session across editor updates and
dispose it on owner cleanup or reload. Neither variant can interrupt arbitrary `Process` code; inspect all failed outcomes before retrying.

For file automation, serialize the request above to a file. Each apply file runs a new mutation; never rerun apply to fetch another page. File apply requires offset zero and a page
large enough for every target (at most 1,000). Use an in-process session for larger batches and page that same session. A validation request instead uses `operation: "validate"`
and a nonempty `assetGuids` array, without query filters. Replace the example GUID with one from your project:

```json
{ "schemaVersion": 1, "operation": "validate", "assetGuids": ["0123456789abcdef0123456789abcdef"], "pageSize": 100 }
```

Run in a Unity project that is available to batch mode:

```sh
Unity -batchmode -projectPath /path/to/project \
  -executeMethod WallstopStudios.DataVisualizer.Editor.Utilities.EditorAutomation.RunFromCommandLine \
  -dxRequest /path/to/request.json -dxResponse /path/to/response.json
```

`EditorAutomation.RunFiles(requestPath, responsePath, timeoutMilliseconds)` is the equivalent in-process file entrypoint. It advances synchronously for up to 30 seconds by default.
Use a session for responsive interactive tools. File execution returns 0 for completed work, 1 for rejected requests, or 2 for partial/cancelled work. **Completion does not mean
zero defects**: inspect every result page's item codes. The batch entrypoint uses those exit codes and exits the editor; in-process calls do not exit it.

Responses distinguish `running`, `waiting`, `cancelled`, `complete`, and `rejected`. Busy databases and Play transitions return partial work without dialogs. Unsupported schema
versions, malformed requests, unavailable/ambiguous types, invalid GUIDs, invalid pagination, and unsupported operations return a top-level code and detail. Per-item codes are
`ok`, `asset_unavailable`, `read_failed`, `missing_reference`, and `duplicate_authored_id`. A completed scan can contain unreadable assets; coverage limits remain in the response.

Responses must be separate `.json` files outside `Assets`, `Packages`, and `ProjectSettings`; parent directories must already exist. Path protection ignores case on every platform.
Missing input files produce a `request_read_failed` response. The command writes only its requested output and does not change settings or assets. Files contain project paths and
authored IDs, so store them where your project permits. The dispatcher uses Unity JSON field types; full schema validation belongs in client tooling.

## Odin Inspector

When the Odin Inspector is installed, the window renders assets through Odin instead of the standard inspector in two cases: the type carries `[CustomDataVisualization]` with
`UseOdinInspector` set, and the type derives from `SerializedScriptableObject` (which `BaseDataObject` does when Odin is present). A type that opts out shows the standard
inspector.

Odin integration is optional. The package has no dependency on Odin and compiles and runs without it.

## Removing a type from the catalog

Types deriving from `BaseDataObject` and types carrying `[CustomDataVisualization]` are managed for you and have no remove button. Other tracked types can be removed with the **X**
on their row or their namespace header. Removing a type stops DxVisualizer from tracking it; the assets stay on disk.

## Next steps

- [Managing assets](managing-assets.md) — the operations your hooks run inside.
- [Search and filtering](search-and-filter.md) — the label filter your processors can be scoped to.
- [Organizing your data](organizing.md) — themes, layout, and persistence.
