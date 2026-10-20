# Extending the window

The runtime assembly is part of the package and carries the extension surface, so your types compile against it in both the editor and a player build. Everything on this page is
public API. Code identifiers keep the `DataVisualizer` name — namespaces, types, and assemblies are unchanged by the rebrand, so your code needs no edits.

Each code block is a separate example. Put each ScriptableObject class in a file with the same name. Except for the processor, the examples compile in editor and player assemblies.

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
- `Process` receives the type and the objects, which is the **ALL** or **FILTERED** set chosen in the window. See [Processors](managing-assets.md#processors) for scope, the
  confirmation, and the load-in-progress refusal.
- `WillEffect(Type, IEnumerable<ScriptableObject>)` is a default interface method that returns the number of objects. Override it when a processor will skip some.

The window instantiates each processor with its public parameterless constructor. A processor that is abstract, generic, or lacks that constructor is skipped and logged. Exceptions
from `Process` are caught, logged, and surfaced in a dialog; one failing processor does not stop the others.

The window saves assets and refreshes after a processor runs, so call `EditorUtility.SetDirty` on anything you change.

## Read-only editor queries

Use `EditorAssetQuery` on Unity's editor main thread without opening a window. It uses the same text matcher (multi-term OR) and label evaluator as the window. Scope contains exact
ScriptableObject types: derived instances and subassets are excluded. An empty scope is rejected instead of searching everything. Results use asset GUIDs and assembly-qualified
type identities; authored `BaseDataObject.Id` values are separate.

Place this example under an `Editor` folder and replace `QuestData` with your project type:

```csharp
namespace MyGame.Editor
{
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
