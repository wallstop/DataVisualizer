---
template: home.html
---

# DxVisualizer

DxVisualizer is a Unity editor window for ScriptableObject-heavy projects. It gathers your data in one place: a catalog of your ScriptableObject types organized by namespace, a
list of every instance of the selected type, and the inspector for the asset you picked. You edit, reorder, and batch-process data without moving between the Project panel and the
Inspector.

DxVisualizer is free forever. There are no subscriptions, no paid upgrades, and no feature-gated tiers. The full source is MIT-licensed, and every capability described here ships
in the free package.

## What it does

| Task     | What the window gives you                                                                                                |
| -------- | ------------------------------------------------------------------------------------------------------------------------ |
| Find     | Search your ScriptableObject types by name, add a whole namespace at once, or scan a folder for assets you already have. |
| Inspect  | Edit the selected asset in the inspector you already know, including custom editors and Odin Inspector integration.      |
| Organize | Reorder namespaces, types, and instances, filter by label, and keep the arrangement across sessions.                     |
| Edit     | Clone, rename, move, delete, and create assets in place, then run processors over one type or all of its instances.      |

## Requirements

- Unity 2021.3 or newer, the floor declared in `package.json`. The window is developed and tested on Unity 6.
- No additional package dependencies.

## Install

1. Open **Window → Package Manager**.
2. Select **+**, then **Add package from git URL**.
3. Enter `https://github.com/wallstop/DataVisualizer.git` and select **Add**.

Open the window with **Tools → Wallstop Studios → DxVisualizer**. [Getting started](getting-started.md) walks through the first session.

!!! note

    DxVisualizer is alpha software. It was built for wallstop studios'
    internal use, there is no support commitment yet, and breaking changes can
    arrive in any release. Feel free to use it, and report what breaks.

## Where to go next

- [Getting started](getting-started.md) — install the package, open the window, and edit your first asset.
- [Search and filtering](search-and-filter.md) — global search, the type filter, and label filtering.
- [Managing assets](managing-assets.md) — create, clone, rename, move, delete, and run processors.
- [Organizing your data](organizing.md) — ordering, layout, themes, persistence, and Play Mode.
- [Extending the window](extending.md) — attributes, `BaseDataObject`, processors, and custom inspector content.
- [Troubleshooting](troubleshooting.md) — slow loading, stale views, missing assets, window size limits, and capture regeneration.
- [README](https://github.com/wallstop/DataVisualizer#readme) — the repository README, which links the video walkthrough.
- [Video walkthrough](https://youtu.be/3oUxUSKNyhw) — a visual tour of the window.
- [Issues](https://github.com/wallstop/DataVisualizer/issues) — known problems and feature requests.
