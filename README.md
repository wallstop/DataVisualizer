# Data Visualizer

> **🤖 AI Assistance Disclosure**
>
> The early versions of Data Visualizer were heavily human-authored. More recent development has been a mix of human effort and AI assistance: human authors lead design, architecture, and review, while AI tools assist with feature development, bug detection, performance optimization, and documentation.

Data Visualizer streamlines working with ScriptableObject-heavy systems by centralizing asset management, inspection, and batch operations in a single window. Instead of hunting through the Project panel and repeatedly switching contexts, you get a namespace-organized view of all your data types with inline editing, batch operations, and workflow automation.

This guide captures the key points from the companion [video walkthrough](https://youtu.be/3oUxUSKNyhw) while keeping the instructions project-agnostic.

## Getting Started

Open **Tools → Wallstop Studios → Data Visualizer** and dock it alongside the Inspector. The tool persists your layout, selection, and tracked types between sessions, so you can jump back into your workflow immediately.

## Window Layout

![Data Visualizer layout with namespace, object, and inspector columns](https://raw.githubusercontent.com/wallstop/DataVisualizer/main/docs/images/data-visualizer-layout.jpg)
*Full window overview at 00:27 in the walkthrough video.*

The window uses a three-panel layout:

**Namespace & Type Panel (left)** organizes ScriptableObject types by C# namespace. Click a namespace to expose its types, then select a type to load all instances. Reorder namespaces and types with the up and down arrow buttons—your ordering persists across sessions.

**Objects Panel (center)** lists every instance of the selected type and keeps one selection at a time. Arrow buttons let you reorder instances without dragging through long lists, and batch edits run through the per-type processors area (scoped to all instances or the filtered set) and per-row actions.

**Inspector Panel (right)** displays the full inspector for the selected asset, including Odin Inspector integrations and custom editors. Changes save immediately, just like Unity's default Inspector.

## Instance Management

![Clone, rename, move, and delete controls highlighted above the object list](https://raw.githubusercontent.com/wallstop/DataVisualizer/main/docs/images/data-visualizer-instance-actions.jpg)
*Instance actions demo at 03:35.*

Asset management controls live above the Objects panel:

**Clone** duplicates the selected asset with a "Clone" suffix in the same folder as the original. Useful for creating variants without leaving the window.

**Rename** opens a draggable prompt that renames the asset on disk. No need to coordinate between multiple panels or windows.

**Move** retargets assets to different folders. The move dialog opens in the moved asset's current folder each time, and a move to the same location is ignored.

**Delete** removes assets permanently after confirmation. Click elsewhere or hit cancel to abort.

Inspector edits save immediately. Your selection persists when switching between types, so you can jump between data categories without losing context.

## Creating Assets

![Create button and data folder selector above object list](https://raw.githubusercontent.com/wallstop/DataVisualizer/main/docs/images/data-visualizer-create.jpg)
*New asset workflow at 06:45.*

The **Create** button spawns a new instance of the active type in your configured **Data Folder** (see Settings below). Clones stay beside their originals regardless of the Data Folder setting. Chain create with rename or move to place new assets exactly where you need them.

## Building Your Type Catalog

![Namespace search dropdown with import controls visible](https://raw.githubusercontent.com/wallstop/DataVisualizer/main/docs/images/data-visualizer-import.jpg)
*Adding ScriptableObject types around 12:45.*

Three controls above the Namespace panel populate your catalog:

**Search Types** queries Unity's known ScriptableObject types. Add individual types or entire namespaces in one operation.

**Scan Asset Folder** crawls a folder recursively, discovering all ScriptableObject types and wiring up existing instances. Ideal for bootstrapping Data Visualizer on established projects.

**Scan Scripts Folder** targets source folders containing ScriptableObject classes. Use this when you've written new types but haven't created any assets yet.

Removing types or namespaces is non-destructive—it only stops Data Visualizer from tracking them. Your assets remain untouched on disk.

Organize the catalog to match your team's mental model. The structure persists across sessions, so everyone can navigate consistently.

## Search & Filtering

The filter field above the Namespace list narrows type rows by display name with case-insensitive matching; namespace headers are not filtered. The global search box above the Objects panel finds text across all loaded instances, letting you jump directly to specific assets without manual scanning.

## Settings

![Settings dropdown showing persistence options and data folder field](https://raw.githubusercontent.com/wallstop/DataVisualizer/main/docs/images/data-visualizer-settings.jpg)
*State management settings at 18:20.*

**Persist state in user settings** stores layout, ordering, and tracked types in your local user cache instead of a shared project asset. Enable this to avoid merge conflicts when multiple developers customize their own workspace.

**Select active object** syncs selection between Data Visualizer and Unity's Inspector window. Useful for cross-referencing assets in other editor windows.

**Data Folder** defines where new assets land. Click to ping the current folder or browse to set a new default.

### Themes

**Classic**, **Nord**, and **Dracula** ship under `Editor/DataVisualizer/Styles` in the package. In **Settings → Theme**, click the current theme to open a searchable dropdown. Search by name or asset path, use Up/Down and Enter to select, or press Escape to cancel. Themes in both Assets and Packages are listed; duplicate names show their paths. Classic uses the original palette, Nord uses blue-gray surfaces and cyan accents, and Dracula uses dark surfaces and purple accents. These editor-only assets and their stylesheets are included in the package, not an optional sample.

The palettes style the window background and text, lists, search results, popovers, label and processor panels, dividers, and standard UI Toolkit inputs, buttons, foldouts, toggles, scrollers, and inspector surfaces. Action colors remain distinct: danger for delete, positive for create/clone/confirm, secondary for rename/script-folder loading, warning for cancel/move, and emphasis for alternate toggle modes. **Reset Theme** keeps the compact Data Folder button sizing, clears the saved selection, and restores Classic. Selecting the Classic asset gives the same appearance but keeps an explicit selection.

Create a theme with **Assets → Create → Wallstop Studios → DataVisualizer → Data Visualizer Theme**. Assign a `.uss` asset to its **Style Sheet** field, then choose the theme in the window's **Settings → Theme** field. Keep your theme and stylesheet under an `Editor` folder; they are editor-only assets.

The selected theme follows the existing project/user persistence setting. Switching that setting copies the current selection. Choose **Classic (Default / Reset)** or use **Reset Theme** to restore the package style. A missing theme falls back to the package style without discarding its saved GUID; reset clears that reference too.

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

The override stylesheet is applied after the package stylesheet. Standard control rules are scoped to `.dataviz-root` inside the Data Visualizer window. Normal USS selector precedence still applies, and explicit inline styles take priority. Data color swatches and label colors remain data-driven; IMGUI and custom third-party inspector styling are not replaced. Use `Nord.uss` or `Dracula.uss` as palette references; copy them under your project's `Editor` folder before customizing rather than editing installed package files. The `--dataviz-background` and `--dataviz-input` tokens control the window and input surfaces. Semantic `--dataviz-danger`, `--dataviz-positive`, `--dataviz-secondary`, `--dataviz-warning`, and `--dataviz-emphasis` tokens each have a matching `--dataviz-on-*` foreground token for filled states. Theme changes apply to the open window without reselecting or reopening: editing the applied theme's stylesheet, changing its Style Sheet reference, or renaming/moving the assets updates immediately, and deleting the applied theme falls back to the package style while keeping the saved GUID semantics.

## Extensibility

Data Visualizer exposes several extension points for custom workflows:

**Attributes** let you override display namespace or friendly names on ScriptableObject classes. Useful when code organization doesn't match your content taxonomy.

**BaseDataObject** provides a ready-made base class for ScriptableObjects with built-in lifecycle support:
- Stores an asset GUID, title, and description for display and stable identity
- Implements clone, create, and rename lifecycle interfaces with virtual `BeforeClone`, `AfterClone`, `BeforeCreate`, `AfterCreate`, `BeforeRename`, and `AfterRename` hooks
- Supports custom UI Toolkit content through the virtual `BuildGUI` method

Derived classes override only what they need—GUID generation, cache resets, companion asset syncing—without duplicating boilerplate.

**Lifecycle Interfaces** hook into asset events before and after clone, create, and rename operations. Enforce invariants like regenerating IDs or pushing audit logs to telemetry without writing per-asset editor scripts.

**UI Toolkit Extensions** render custom UI alongside the default inspector. Return a `VisualElement` tree—graphs, thumbnails, validation badges, or any UI Toolkit component—and Data Visualizer slots it in automatically. Because the entire window runs on UI Toolkit, this approach scales to complex dashboards without leaving the unified workflow.

## Workflow Tips

**Treat namespace ordering as shared navigation** when persisting state in the project asset. A consistent structure reduces friction when different team members jump into the same data.

**Set the Data Folder to your team's content root.** Predictable asset locations keep version control diffs clean and make batch operations safer.

**Use clone + move for cross-folder duplication.** Faster and more controlled than dragging through the Project window, especially when working with deep folder hierarchies.

The [video walkthrough](https://youtu.be/3oUxUSKNyhw) provides a step-by-step visual tour and explains the rationale behind UI choices.


# Overview
This is currently *ALPHA* software, intended for wallstop studios internal use only. Feel free to use. Currently there is no support and there may be breaking changes. Once stabilized, I will ready this repo for production use.

## Contributing

This project uses [CSharpier](https://csharpier.com/) with the default configuration to enable an enforced, consistent style. If you would like to contribute, recommendation is to ensure that changed files are ran through CSharpier prior to merge. This can be done automatically through editor plugins, or, minimally, by installing a [pre-commit hook](https://pre-commit.com/#3-install-the-git-hook-scripts).