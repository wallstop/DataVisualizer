# DxVisualizer

> **🤖 AI Assistance Disclosure**
>
> The early versions of Data Visualizer were heavily human-authored. More recent development has been a mix of human effort and AI assistance: human authors lead design,
> architecture, and review, while AI tools assist with feature development, bug detection, performance optimization, and documentation.

DxVisualizer streamlines working with ScriptableObject-heavy systems by centralizing asset management, inspection, and batch operations in a single window. Instead of hunting
through the Project panel and repeatedly switching contexts, you get a namespace-organized view of all your data types with inline editing, batch operations, and workflow
automation.

DxVisualizer is free forever: no subscriptions, no paid upgrades, no feature-gated tiers. The full source is MIT-licensed, and every capability documented here ships in the free
package.

This guide captures the key points from the companion [video walkthrough](https://youtu.be/3oUxUSKNyhw) while keeping the instructions project-agnostic. A browsable version of this
documentation, with per-topic pages, is published at <https://wallstop.github.io/DataVisualizer/>.

## Getting Started

Open **Tools → Wallstop Studios → DxVisualizer** and dock it alongside the Inspector. The tool persists your layout, selection, and tracked types between sessions, so you can jump
back into your workflow immediately.

## Window Layout

![Data Visualizer layout with namespace, object, and inspector columns](https://raw.githubusercontent.com/wallstop/DataVisualizer/main/docs/images/data-visualizer-layout.png) _Full
window overview. Regenerated from the live window by the docs capture pipeline._

The window uses a three-panel layout:

**Namespace & Type Panel (left)** organizes ScriptableObject types by C# namespace. Click a namespace to expose its types, then select a type to load all instances. Reorder
namespaces and types by dragging them, or with the up and down arrow buttons, which move a row to the top or bottom of its list. Your ordering persists across sessions.

**Objects Panel (center)** lists every instance of the selected type and keeps one selection at a time. Drag a row to place an instance precisely, or use its arrow buttons to move
it to the top or bottom. Batch edits run through the per-type processors area, scoped to all instances or to the filtered set, and through the per-row actions.

**Inspector Panel (right)** displays the full inspector for the selected asset, including Odin Inspector integrations and custom editors. Changes save immediately, just like
Unity's default Inspector.

## Instance Management

![Clone, rename, move, and delete controls on the object rows](https://raw.githubusercontent.com/wallstop/DataVisualizer/main/docs/images/data-visualizer-instance-actions.png)
_Per-row asset management controls. Regenerated from the live window by the docs capture pipeline._

Asset management controls sit on the right of each object row:

**Clone** (`++`) duplicates that row's asset in the same folder as the original, directly after it in the list. Any existing `(Clone)` or `(Clone n)` suffix is stripped from the
source name first, then reapplied, so repeated clones read `(Clone)`, `(Clone 1)`, `(Clone 2)`, and so on. Useful for creating variants without leaving the window.

**Rename** opens a draggable prompt that renames the asset on disk. No need to coordinate between multiple panels or windows.

**Move** retargets assets to different folders. The move dialog opens in the moved asset's current folder each time, and a move to the same location is ignored.

**Delete** removes assets permanently after confirmation. Click elsewhere or hit cancel to abort.

Inspector edits save immediately. Your selection persists when switching between types, so you can jump between data categories without losing context.

## Creating Assets

![Create popover asking for a new asset name](https://raw.githubusercontent.com/wallstop/DataVisualizer/main/docs/images/data-visualizer-create.png) _Create popover over the object
list. Regenerated from the live window by the docs capture pipeline._

The **Create** button asks for a name and spawns a new instance of the active type under your configured **Data Folder** (see Settings below), in a per-type folder named after the
type's full namespace, such as `Assets/Data/MyGame/Items/WeaponData/`. Those folders are created for you. Clones stay beside their originals regardless of the Data Folder setting.
Chain create with rename or move to place new assets exactly where you need them.

## Building Your Type Catalog

![Manage-visible-types popover listing the project's ScriptableObject types](https://raw.githubusercontent.com/wallstop/DataVisualizer/main/docs/images/data-visualizer-import.png)
_Type search popover over the namespace panel. Regenerated from the live window by the docs capture pipeline._

Three controls above the Namespace panel populate your catalog:

**Search Types** queries Unity's known ScriptableObject types. Add individual types or entire namespaces in one operation.

**Scan Asset Folder** crawls a folder recursively, discovering all ScriptableObject types and wiring up existing instances. Ideal for bootstrapping DxVisualizer on established
projects.

**Scan Scripts Folder** targets source folders containing ScriptableObject classes. Use this when you've written new types but haven't created any assets yet.

Removing types or namespaces is non-destructive—it only stops DxVisualizer from tracking them. Your assets remain untouched on disk.

Organize the catalog to match your team's mental model. The structure persists across sessions, so everyone can navigate consistently.

## Search & Filtering

Three finders narrow different things.

The **Search** box in the window header row, next to the Settings button, searches every tracked type rather than only the selected one. A space-separated query matches an asset
when any one of its terms matches an asset name, a type name, an exact asset GUID, or a string field on the asset or a nested plain object; field matching is case-insensitive and
skips primitives, vectors, colors, and references to other Unity objects. Results are ordered by asset name then full type name, list up to two matched fields for context,
highlight the matched terms, and cap at 25 results. Use Up and Down to move, Enter to open the highlighted asset, Escape to dismiss.

The filter field above the Namespace list narrows type rows by display name with case-insensitive matching; namespace headers are not filtered.

Dragging labels from **Available** into the **AND:** or **OR:** rows filters the selected type's instances by Unity asset labels. The **AND &&** and **OR ||** switch chooses
between requiring every dragged label and requiring any one of them; in OR mode an empty clause never counts as a match. The Advanced row refuses to collapse while OR mode or any
OR label is active, so a filter that hides rows is never left concealed. A line under the filter reports how many objects are hidden. The selected asset's inspector panel adds and
removes its labels, and the filter re-applies immediately.

## Settings

![Settings popover with persistence toggles and data folder field](https://raw.githubusercontent.com/wallstop/DataVisualizer/main/docs/images/data-visualizer-settings.png)
_Settings popover. Regenerated from the live window by the docs capture pipeline._

With **Persist State in Settings Asset** off (the default), the window stores the selected namespace and type, the selected object per type, namespace, type, and object ordering,
collapse state, tracked types, per-type label filters, and the per-type processor scope in a per-user JSON file instead of a shared project asset. Each developer keeps a private
arrangement and version control stays free of layout churn.

Turning **Persist State in Settings Asset** on stores the same state inside a `DataVisualizerSettings` asset in the project, which suits a team that wants one shared arrangement.
DxVisualizer creates one at `Assets/Editor/DataVisualizerSettings.asset` on first use if no such asset exists. Switching between the two copies the current state across.

**Select Active Object** syncs selection between DxVisualizer and Unity's Inspector window. Useful for cross-referencing assets in other editor windows.

**Data Folder** defines where new assets land, each in a per-type folder named after its full namespace. Click the path to ping the current folder, or browse to set a new default.

### Themes

**Classic**, **Nord**, **Dracula**, **Compact**, and **Minimal** ship under `Editor/DataVisualizer/Styles` in the package. In **Settings → Theme**, click the current theme to open
a searchable dropdown. Search by name or asset path, use Up/Down and Enter to select, or press Escape to cancel. Themes in both Assets and Packages are listed; duplicate names show
their paths. Classic uses the original palette, Nord uses blue-gray surfaces and cyan accents, and Dracula uses dark surfaces and purple accents. Compact and Minimal keep the
Classic palette and shrink the density: window typography, action buttons, and control chrome. Compact uses 13px type with 20px action buttons and 4px control radii; Minimal uses
12px type with 16px action buttons and square control corners. These editor-only assets and their stylesheets are included in the package, not an optional sample.

The palettes style the window background and text, lists, search results, popovers, label and processor panels, dividers, and standard UI Toolkit inputs, buttons, foldouts,
toggles, scrollers, and inspector surfaces. Action colors remain distinct: danger for delete, positive for create/clone/confirm, secondary for rename/script-folder loading, warning
for cancel/move, and emphasis for alternate toggle modes. **Reset Theme** keeps the compact Data Folder button sizing, clears the saved selection, and restores Classic. Selecting
the Classic asset gives the same appearance but keeps an explicit selection.

Create a theme with **Assets → Create → Wallstop Studios → DxVisualizer → DxVisualizer Theme**. Assign a `.uss` asset to its **Style Sheet** field, then choose the theme in the
window's **Settings → Theme** field. Keep your theme and stylesheet under an `Editor` folder; they are editor-only assets.

The selected theme follows the existing project/user persistence setting. Switching that setting copies the current selection. Choose **Classic (Default / Reset)** or use **Reset
Theme** to restore the package style. A missing theme falls back to the package style without discarding its saved GUID; reset clears that reference too.

For example, this stylesheet changes the accent, standard button states, window font size, and action-button size:

```css
:root {
    --dataviz-accent: #88c0d0;
    --dataviz-control-hover: #88c0d0;
    --dataviz-control-pressed: #81a1c1;
    --dataviz-on-accent: #2e3440;
    --dataviz-font-size: 15px;
    --dataviz-circle-size: 32px;
}
```

The override stylesheet is applied after the package stylesheet. Standard control rules are scoped to `.dataviz-root` inside the DxVisualizer window. Normal USS selector precedence
still applies, and explicit inline styles take priority. Data color swatches and label colors remain data-driven; IMGUI and custom third-party inspector styling are not replaced.
Use `Nord.uss` or `Dracula.uss` as palette references; copy them under your project's `Editor` folder before customizing rather than editing installed package files. The
`--dataviz-background` and `--dataviz-input` tokens control the window and input surfaces. Semantic `--dataviz-danger`, `--dataviz-positive`, `--dataviz-secondary`,
`--dataviz-warning`, and `--dataviz-emphasis` tokens each have a matching `--dataviz-on-*` foreground token for filled states. Theme changes apply to the open window without
reselecting or reopening: editing the applied theme's stylesheet, changing its Style Sheet reference, or renaming/moving the assets updates immediately, and deleting the applied
theme falls back to the package style while keeping the saved GUID semantics.

## Extensibility

DxVisualizer exposes several extension points for custom workflows:

**Attributes** let you override display namespace or friendly names on ScriptableObject classes. `[CustomDataVisualization(Namespace = "...", TypeName = "...")]` replaces the
namespace group and the display name the window shows. Without it the window files a type under the last segment of its C# namespace. Useful when code organization doesn't match
your content taxonomy.

**BaseDataObject** provides a ready-made base class for ScriptableObjects with built-in lifecycle support:

- Stores an asset GUID, title, and description for display and stable identity
- Implements clone, create, and rename lifecycle interfaces with virtual `BeforeClone`, `AfterClone`, `BeforeCreate`, `AfterCreate`, `BeforeRename`, and `AfterRename` hooks
- Supports custom UI Toolkit content through the virtual `BuildGUI` method

Derived classes override only what they need—GUID generation, cache resets, companion asset syncing—without duplicating boilerplate.

**Lifecycle Interfaces** hook into asset events before and after clone, create, and rename operations. Enforce invariants like regenerating IDs or pushing audit logs to telemetry
without writing per-asset editor scripts.

**UI Toolkit Extensions** render custom UI alongside the default inspector. Return a `VisualElement` tree—graphs, thumbnails, validation badges, or any UI Toolkit component—from
`IGUIProvider.BuildGUI` (or `BaseDataObject.BuildGUI`) and DxVisualizer slots it in below the inspector. The `DataVisualizerGUIContext` argument carries the selected asset's
`SerializedObject` for writing changes back. Because the entire window runs on UI Toolkit, this approach scales to complex dashboards without leaving the unified workflow.

**Processors** are plain `IDataProcessor` classes that the window discovers with no registration. `Name` labels the button, `Description` is its tooltip, `Accepts` lists the types
it applies to, and `Process(Type, IEnumerable<ScriptableObject>)` receives the **ALL** or **FILTERED** object set you chose. The window instantiates processors through their public
parameterless constructor, skips any that lack one, and surfaces a thrown exception in a dialog without stopping other processors.

## Workflow Tips

**Treat namespace ordering as shared navigation** when persisting state in the project asset. A consistent structure reduces friction when different team members jump into the same
data.

**Set the Data Folder to your team's content root.** Predictable asset locations keep version control diffs clean and make batch operations safer.

**Use clone + move for cross-folder duplication.** Faster and more controlled than dragging through the Project window, especially when working with deep folder hierarchies.

The [video walkthrough](https://youtu.be/3oUxUSKNyhw) provides a step-by-step visual tour and explains the rationale behind UI choices.

# Overview

This is currently _ALPHA_ software, intended for wallstop studios internal use only. Feel free to use. Currently there is no support and there may be breaking changes. Once
stabilized, I will ready this repo for production use.

## Contributing

This project uses [CSharpier](https://csharpier.com/) with the default configuration to enable an enforced, consistent style. If you would like to contribute, recommendation is to
ensure that changed files are ran through CSharpier prior to merge. This can be done automatically through editor plugins, or, minimally, by installing a
[pre-commit hook](https://pre-commit.com/#3-install-the-git-hook-scripts).
