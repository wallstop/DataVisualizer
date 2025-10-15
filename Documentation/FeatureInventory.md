# Data Visualizer Feature Inventory (Baseline)

This document captures the current editor workflows before further refactoring. Use it as a reference for manual validation and to drive future regression coverage. Update the screenshot placeholders with real captures when running inside Unity.

## Feature Catalogue

| Area | Feature | Behaviour Notes |
| --- | --- | --- |
| Namespace Pane | Type discovery | Lists all configured ScriptableObject namespaces and types with cached ordering. |
| Namespace Pane | Namespace reorder | Drag-and-drop reorders persist between sessions. |
| Namespace Pane | Type removal | Prompts before deleting types and warns when dependent assets exist. |
| Object List | Pagination | Displays objects in pages of 100 with previous/next navigation and manual entry. |
| Object List | Drag reorder | Supports in-list reordering and move-to-top/bottom shortcuts. |
| Object List | Context actions | Inline buttons for clone, rename, move, and delete operations. |
| Labels | AND/OR filter | Label filter UI supports switching between AND/OR modes with advanced collapse. |
| Labels | Suggestions | Suggestions surface project labels while excluding ones already applied. |
| Processor Panel | Execution | Processor list toggles execution per selection with persisted enable states. |
| Search | Global search | Popover search filters types and objects with fuzzy matching and keyboard navigation. |
| Drag & Drop | Namespace/type drag | Dragging namespaces or types reorders groups and persists order. |
| Layout | Split view persistence | Outer/inner split widths persist via EditorPrefs. |

## Journey Notes

1. **Type Selection & Creation**  
   - Starting from an empty selection, choose a namespace/type and confirm object and inspector panels refresh.  
   - Create a new object via the `+` button and verify it appears at the top of the list.  
   - Placeholder: `Documentation/Media/type-selection-before.png`

2. **Label Editing Workflow**  
   - Select an object with existing labels, toggle advanced configuration, add AND/OR labels, and confirm list filtering updates.  
   - Placeholder: `Documentation/Media/label-editing-before.png`

3. **Processor Execution Loop**  
   - Enable a processor, execute it on multiple objects, and verify results update without clearing selection.  
   - Placeholder: `Documentation/Media/processor-execution-before.png`

4. **Search Interactions**  
   - Invoke the search popover, type partial matches, navigate with arrow keys, and commit selection.  
   - Placeholder: `Documentation/Media/search-before.png`

5. **Drag Reorder Flow**  
   - Drag objects to new positions and use move-to-top/bottom shortcuts; confirm persistence after window reload.  
   - Placeholder: `Documentation/Media/drag-reorder-before.png`

6. **Window Layout Persistence**  
   - Adjust split view widths, close/reopen the window, and verify layout restores.  
   - Placeholder: `Documentation/Media/layout-before.png`

## Capture Guidance

- Replace placeholders with actual captures once the Unity editor is available.  
- Store any additional recordings alongside the referenced PNGs to minimise future diff noise.  
- Update the table and journeys when new features are introduced or behaviour changes.
