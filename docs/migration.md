# Migrating from Data Visualizer

Data Visualizer is now DxVisualizer. The rename is user-facing: the product name, the menu paths, the window title, and this site changed. Code identifiers did not, so existing
projects upgrade without edits.

## What changed

|                  | Before                                                                          | Now                                                                        |
| ---------------- | ------------------------------------------------------------------------------- | -------------------------------------------------------------------------- |
| Name             | Data Visualizer                                                                 | DxVisualizer                                                               |
| Window menu      | **Tools → Wallstop Studios → Data Visualizer**                                  | **Tools → Wallstop Studios → DxVisualizer**                                |
| Theme asset menu | **Assets → Create → Wallstop Studios → DataVisualizer → Data Visualizer Theme** | **Assets → Create → Wallstop Studios → DxVisualizer → DxVisualizer Theme** |
| Default theme    | Classic                                                                         | Dx                                                                         |

## What stayed the same

- The package name, `com.wallstop-studios.data-visualizer`, and the install URL, `https://github.com/wallstop/DataVisualizer.git`. The dependency key in `Packages/manifest.json`
  keeps resolving. Both repoint when the repository moves to Ambiguous-Interactive ([#151](https://github.com/wallstop/DataVisualizer/issues/151)); this page will be updated with
  the move.
- The C# namespaces `WallstopStudios.DataVisualizer` and `WallstopStudios.DataVisualizer.Editor`, and every public type: `BaseDataObject`, `CustomDataVisualizationAttribute`,
  `IDataProcessor`, `IGUIProvider`, `DataVisualizerGUIContext`, and the lifecycle interfaces.
- Saved state: `DataVisualizerUserState.json`, the `DataVisualizerSettings` asset at `Assets/Editor/DataVisualizerSettings.asset`, ordering, selections, label filters, processor
  scope, and the selected theme.
- Theme tokens: every `--dataviz-*` custom property and the `.dataviz-root` scope. Custom themes keep working.
- Asset GUIDs and the `.meta` files behind them, so references from your assets to package types are untouched.

## Update the package

1. Open **Window → Package Manager**.
2. Select the package entry under **Packages: In Project**.
3. Select **Update**, or re-add the package from the git URL `https://github.com/wallstop/DataVisualizer.git`.

## Keep the previous look

Dx is the default theme from this release. If you never picked a theme, the window switches to Dx. A theme you picked before the upgrade, including Classic, stays selected. To
return to the previous appearance, open **Settings → Theme** and choose **Classic**.
