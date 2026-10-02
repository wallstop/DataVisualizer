# Getting started

This page takes you from an empty project to an edited asset. It covers the
first session only. The
[README](https://github.com/wallstop/DataVisualizer#readme) documents the full
feature set, including themes, processors, and extension points.

## Requirements

- Unity 2021.3 or newer. `package.json` declares that floor, and the window is
  developed and tested on Unity 6. The 2021.3 editor is not part of the local
  validation yet, which
  [#86](https://github.com/wallstop/DataVisualizer/issues/86) tracks.
- No additional package dependencies.

## Install the package

1. Open **Window → Package Manager**.
2. Select **+**, then **Add package from git URL**.
3. Enter `https://github.com/wallstop/DataVisualizer.git` and select **Add**.

Unity imports the package, and the menu item appears.

## Open the window

Select **Tools → Wallstop Studios → Data Visualizer**, then dock it next to the
Inspector. The window has three panels:

- **Namespaces and types** (left) lists your ScriptableObject types by C#
  namespace. Expand a namespace, then select a type to load its instances.
- **Objects** (center) lists every instance of the selected type. One row is
  selected at a time.
- **Inspector** (right) shows the full inspector for the selected asset,
  including custom editors and Odin Inspector integration.

## Add your types

Three controls above the namespace panel fill the catalog:

- **Search Types** queries the ScriptableObject types Unity knows about. Add
  single types or a whole namespace in one action.
- **Scan Asset Folder** crawls a folder and registers the types and existing
  assets it finds. Use it to adopt a project that already has data.
- **Scan Scripts Folder** registers types from source folders, which is the
  right choice before you create any assets.

Removing a type or namespace only stops Data Visualizer from tracking it. Your
assets stay on disk.

## Edit your first asset

1. Select a type in the left panel.
2. Select an instance in the middle panel.
3. Edit a field in the right panel. The change saves immediately, as in Unity's
   own Inspector.

Use the buttons above the object list to manage the selection: **Clone** writes
a copy with a `Clone` suffix beside the original, **Rename** renames the asset
on disk, **Move** retargets it to another folder, and **Delete** removes it
after you confirm.

## Create an asset

Select **Create** to add a new instance of the active type. The asset lands in
your **Data Folder**, which you set under **Settings**. Clones always stay
beside their original, whatever the Data Folder is.

## Filter and search

- The filter field above the namespace list narrows the type rows by display
  name, without case sensitivity. Namespace headers stay visible.
- The search box above the object list searches the text of all loaded
  instances, so you can jump to an asset by its contents.
- Drag labels from **Available** into the **AND:** or **OR:** rows to show only
  assets that carry them. The **AND &&** and **OR ||** toggle switches between
  matching every dragged label and matching any of them.

## Keep your arrangement

Pane widths, ordering, tracked types, and your selection persist between
sessions. Under **Settings**, choose where that state lives:

- **Persist state in user settings** (default) stores it in your local user
  cache, so each developer keeps a private arrangement and version control
  stays free of layout churn.
- The project settings asset stores the state in the repository, which suits a
  team that wants one shared arrangement.

**Select active object** mirrors your Data Visualizer selection in Unity's
Inspector, and **Data Folder** sets where new assets are created.

## While the editor is playing

Entering Play Mode pauses package-driven work. The window keeps the last loaded
view and shows *Package editing is paused during Play Mode*. While it is paused:

- **Create**, the add-type controls, and the inspector are disabled, and the
  inspector shows read-only content.
- Asset operations are refused: clone, rename, move, delete, and label changes.
- Selecting another type does not load its instances, and the catalog does not
  change.

Everything resumes when you leave Play Mode.

## Next steps

- Themes: pick Classic, Nord, Dracula, Compact, or Minimal under **Settings →
  Theme**, or author your own.
- Extensibility: derive from `BaseDataObject`, implement the lifecycle
  interfaces, and return your own UI Toolkit content from `BuildGUI`.
- The [README](https://github.com/wallstop/DataVisualizer#readme) documents
  every control, the theme tokens, and the runtime extension points.
