# Getting started

## Install

Add the package through **Window → Package Manager → + → Add package from git
URL**, using the repository URL:

```text
https://github.com/wallstop/DataVisualizer.git
```

The repository root is the UPM package because it contains `package.json`.
For a local package, add this repository folder to the project's `Packages/`
directory or use its local path in the Package Manager.

The minimum declared Unity version is **2021.3**. The package has no runtime
package dependencies. Odin Inspector remains optional; the default inspector path
uses Unity's built-in editor APIs.

## First use

1. Create or import one or more `ScriptableObject` assets.
2. Open **Tools → Wallstop Studios → Data Visualizer**.
3. Select a type in the namespace/type navigation.
4. Select an object to show its inspector, labels, and asset actions.
5. Set **Data Folder** to the team's content root if new assets should be created
   somewhere other than `Assets/Data`.

![Data Visualizer settings](images/data-visualizer-settings.jpg){ loading=lazy }

Data Visualizer discovers concrete managed `ScriptableObject` types by their exact
identity. Two types with the same short name remain distinct when their namespaces
differ. Subassets are not treated as separate main-asset rows.

## Troubleshooting first launch

- If a type is absent, confirm its script compiles, is concrete, and derives from
  `ScriptableObject`.
- If a saved selection no longer exists, the entry is ignored and the remaining
  navigation stays usable.
- If indexing is incomplete, wait for the progress state to settle or use the
  explicit refresh action. The last available view remains visible during Play Mode.
- If the window is too small, widen it or use the compact navigation arrangement;
  essential controls are kept inside the available bounds.
