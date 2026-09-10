# User guide

## Find and inspect

The left navigation groups concrete managed types by namespace. The object list is
virtualized and uses fixed-height rows, so visible work is bounded by the viewport.
Selecting an object opens its Unity inspector in the detail pane.

![Selected object and actions](images/data-visualizer-instance-actions.jpg){ loading=lazy }

Global search matches the established display name, type, labels, and supported
nested data. Search and indexing can be incomplete while work is in flight; a
partial result is not presented as a complete index.

## Labels and ordering

Use labels to narrow a type's objects. Label filters support the configured AND/OR
combination and are evaluated consistently with the persisted filter state. Custom
namespace, type, and object ordering is stored with the configured state store.

![Import and label workflow](images/data-visualizer-import.jpg){ loading=lazy }

![Create asset workflow](images/data-visualizer-create.jpg){ loading=lazy }

## Create, clone, and asset actions

The editor can create and clone supported `ScriptableObject` assets, then rename,
move, label, or delete them. Lifecycle interfaces run around the corresponding
operation where implemented:

- `ICreatable.BeforeCreate` and `AfterCreate`
- `IDuplicable.BeforeClone` and `AfterClone`
- `IRenamable.BeforeRename` and `AfterRename`

Asset moves preserve Unity GUID identity. Operations are not filesystem
transactions: a batch can partially complete and reports the failing item. The
package rejects package assets and package-driven mutations during Play Mode.

## State persistence

The **Persist State in Settings Asset** option selects between project settings
state and per-user JSON state. Switching modes migrates the current layout,
selection, ordering, and filter state instead of silently resetting the workspace.
The settings asset is project-shared; per-user JSON is stored below Unity's
`Application.persistentDataPath`.

## Processors

`IDataProcessor` implementations are optional extension points for batch changes.
Processor discovery is deferred until the processor pane is requested. Processors
run only in Edit Mode and should keep their work deterministic and safe to rerun.
