<p align="center">
  <img src="https://raw.githubusercontent.com/wallstop/DataVisualizer/main/docs/images/brand/dxvisualizer-banner.png" alt="DxVisualizer. Your game data, one window." width="800">
</p>

# DxVisualizer

**Your game data, one window.** DxVisualizer is a Unity editor window for ScriptableObject-heavy projects: a catalog of your types by namespace, every instance of the type you
pick, and the inspector for the asset you selected. You edit, reorder, and batch-process data without moving between the Project panel and the Inspector.

**AI assistance:** early versions were written by hand; current work pairs human-led design, architecture, and review with AI help on features, bug finding, performance, and docs.

**Alpha:** built for Wallstop Studios' internal use. There is no support commitment yet, and any release can break compatibility.

Documentation: <https://wallstop.github.io/DataVisualizer/>

![DxVisualizer layout with namespace, object, and inspector columns](https://raw.githubusercontent.com/wallstop/DataVisualizer/main/docs/images/data-visualizer-layout.png)

_Captured from the live window by the docs capture pipeline._

DxVisualizer was called Data Visualizer until 0.2.0. Namespaces, the package name, saved state, and theme tokens are unchanged; see
[Migrating from Data Visualizer](https://wallstop.github.io/DataVisualizer/migration/).

## Install

Unity 2021.3 or newer. No other package dependencies.

1. Open **Window → Package Manager**.
2. Select **+**, then **Add package from git URL**.
3. Enter `https://github.com/wallstop/DataVisualizer.git` and select **Add**.

Each [GitHub Release](https://github.com/wallstop/DataVisualizer/releases) also carries a `.unitypackage`.

Open the window with **Tools → Wallstop Studios → DxVisualizer** and dock it next to the Inspector.

## What it does

| Task     | What the window gives you                                                                                                                                                                         |
| -------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Find     | Search your ScriptableObject types by name, add a whole namespace at once, or scan a folder for assets you already have. Global search matches asset names, type names, GUIDs, and string fields. |
| Inspect  | Edit the selected asset in the inspector you already know, including custom editors and Odin Inspector integration.                                                                               |
| Organize | Reorder namespaces, types, and instances, filter by asset label with AND / OR clauses, and keep the arrangement across sessions.                                                                  |
| Edit     | Create, clone, rename, move, and delete assets in place, then run your own processors over all or the filtered instances of a type.                                                               |

## Documentation

- [Getting started](https://wallstop.github.io/DataVisualizer/getting-started/): install, open the window, and edit your first asset.
- [Search and filtering](https://wallstop.github.io/DataVisualizer/search-and-filter/): global search, the type filter, and label filtering.
- [Managing assets](https://wallstop.github.io/DataVisualizer/managing-assets/): create, clone, rename, move, delete, and processors.
- [Organizing your data](https://wallstop.github.io/DataVisualizer/organizing/): ordering, themes, persistence, and Play Mode.
- [Extending the window](https://wallstop.github.io/DataVisualizer/extending/): attributes, `BaseDataObject`, processors, and custom inspector content.
- [Troubleshooting](https://wallstop.github.io/DataVisualizer/troubleshooting/): missing types, hidden rows, processors, themes.
- [Video walkthrough](https://youtu.be/3oUxUSKNyhw): a tour of the window, recorded before the rename.

## Free forever

No subscriptions, no paid upgrades, no feature-gated tiers. The full source is MIT-licensed, and every documented capability ships in the free package.

## Contributing

C# is formatted with [CSharpier](https://csharpier.com/) using the default configuration. Run it on changed files before you open a pull request, either through an editor plugin or
the [pre-commit hook](https://pre-commit.com/#3-install-the-git-hook-scripts).

## License

[MIT](LICENSE).

---

Part of the Dx family with DxKit and DxMessaging. Built by Wallstop Studios, published by Ambiguous Interactive.
