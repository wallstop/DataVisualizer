# Extending the window

The runtime assembly is part of the package and carries the extension surface, so
your types compile against it in both the editor and a player build. Everything
on this page is public API.

## Display attributes

`[CustomDataVisualization]` overrides how a type appears in the catalog.

```csharp
using WallstopStudios.DataVisualizer;

[CustomDataVisualization(Namespace = "Weapons", TypeName = "Weapon")]
public sealed class WeaponData : ScriptableObject
{
}
```

`Namespace` replaces the namespace group the type is filed under. Without it the
window uses the last segment of your C# namespace, so
`MyGame.Items.Weapons` appears under `Weapons`, and a type in no namespace appears
under `No Namespace`.

`TypeName` replaces the type's display name in the list, in the type filter, and
in the dialogs that name the type. Without it the C# type name is used.

With the Odin Inspector available, the attribute also carries `UseOdinInspector`,
which defaults to `true`. See [Odin Inspector](#odin-inspector).

## BaseDataObject

`BaseDataObject` is an abstract `ScriptableObject` that carries identity and the
lifecycle hooks. Derive from it instead of `ScriptableObject` to get them.

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

- `Id`, the asset's GUID, kept in sync with the AssetDatabase.
- `Title`, the display name. A blank title falls back to the asset name, so you
  only set it when the asset name is not what you want to show.
- `Description`.
- Ordering by title, then asset name, then Id, then description.

The serialized fields `_assetGuid`, `_title`, and `_description` are shown in the
inspector under a **Base Data** header, read-only.

## Lifecycle hooks

Each hook is a `virtual` method on `BaseDataObject` and also a standalone
interface, so you can implement one without deriving.

| Interface | Methods | When |
| --- | --- | --- |
| `IDuplicable` | `BeforeClone(ScriptableObject previous)`, `AfterClone(ScriptableObject previous)` | Around [Clone](managing-assets.md#clone) |
| `ICreatable` | `BeforeCreate()`, `AfterCreate()` | Around [Create](managing-assets.md#create) |
| `IRenamable` | `BeforeRename(string newName)`, `AfterRename(string newName)` | Around [Rename](managing-assets.md#rename) |

Override only what you need. The default implementations already do the work you
would otherwise repeat: `BeforeClone` clears the inherited asset GUID so the copy
does not share the original's identity, and `AfterClone` refreshes the GUID from
the new asset path and applies the `(Clone)`, `(Clone 1)`, `(Clone 2)`, … suffix
to the title.

```csharp
public sealed class ItemData : BaseDataObject
{
    public override void BeforeRename(string newName)
    {
        // Audit before the file on disk changes.
    }

    public override void AfterRename(string newName)
    {
        EditorUtility.SetDirty(this);
    }
}
```

## Custom inspector content

Implement `IGUIProvider` and return a `VisualElement` from `BuildGUI`. The window
adds it below the standard inspector.

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

Return `null` to add nothing. `BaseDataObject` implements `IGUIProvider` and
returns `null` by default, so a derived type only overrides the method when it
has something to show.

The `DataVisualizerGUIContext` carries the `SerializedObject` for the selected
asset in the editor, which is what you need to write back through. The context
type has no editor-only members in a player build, so a `BuildGUI` override
compiles in both.

## Display name

Implement `IDisplayable` to control the name shown for an object. `BaseDataObject`
implements it through `Title`. For a plain `ScriptableObject`, implement it
yourself:

```csharp
public sealed class ItemData : ScriptableObject, IDisplayable
{
    [SerializeField]
    private string displayName;

    public string Title => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
}
```

The row label, the search result name, and the drag ghost all read `Title` when
the type provides it.

## Processors

A processor is a plain class implementing `IDataProcessor`. The window discovers
every implementation in your project, so no registration is needed.

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;
using WallstopStudios.DataVisualizer;

public sealed class RecalculateRarity : IDataProcessor
{
    public string Name => "Recalculate rarity";

    public string Description => "Recomputes each item's rarity from its level.";

    public IEnumerable<Type> Accepts => new[] { typeof(ItemData) };

    public void Process(Type type, IEnumerable<ScriptableObject> objects)
    {
        foreach (ScriptableObject obj in objects)
        {
            if (obj is ItemData item)
            {
                item.Rarity = Rarity.FromLevel(item.Level);
                EditorUtility.SetDirty(item);
            }
        }
    }
}
```

- `Name` is the button label and sorts the list.
- `Description` is the button tooltip.
- `Accepts` limits the processor to the types you list, and the processor is
  only offered for a selected type that appears in that list. Return `null` or an
  empty list and the processor is never offered.
- `Process` receives the type and the objects, which is the **ALL** or
  **FILTERED** set chosen in the window. See
  [Processors](managing-assets.md#processors) for scope, the confirmation, and
  the load-in-progress refusal.
- `WillEffect(Type, IEnumerable<ScriptableObject>)` is a default interface method
  that returns the number of objects. Override it when a processor will skip some.

The window instantiates each processor with its public parameterless constructor.
A processor that is abstract, generic, or lacks that constructor is skipped and
logged. Exceptions from `Process` are caught, logged, and surfaced in a dialog;
one failing processor does not stop the others.

The window saves assets and refreshes after a processor runs, so call
`EditorUtility.SetDirty` on anything you change.

## Odin Inspector

When the Odin Inspector is installed, the window renders assets through Odin
instead of the standard inspector in two cases: the type carries
`[CustomDataVisualization]` with `UseOdinInspector` set, and the type derives
from `SerializedScriptableObject` (which `BaseDataObject` does when Odin is
present). A type that opts out shows the standard inspector.

Odin integration is optional. The package has no dependency on Odin and compiles
and runs without it.

## Removing a type from the catalog

Types deriving from `BaseDataObject` and types carrying
`[CustomDataVisualization]` are managed for you and have no remove button. Other
tracked types can be removed with the **X** on their row or their namespace
header. Removing a type stops Data Visualizer from tracking it; the assets stay on
disk.

## Next steps

- [Managing assets](managing-assets.md) — the operations your hooks run inside.
- [Search and filtering](search-and-filter.md) — the label filter your processors
  can be scoped to.
- [Organizing your data](organizing.md) — themes, layout, and persistence.