---
title: DxVisualizer
template: home.html
hide:
  - navigation
  - toc
---

## What it does

| Task | What the window gives you |
| --- | --- |
| Find | Search your ScriptableObject types by name, add a whole namespace at once, or scan a folder for assets you already have. |
| Inspect | Edit the selected asset in the inspector you already know, including custom editors and Odin Inspector integration. |
| Organize | Reorder namespaces, types, and instances, filter by label, and keep the arrangement across sessions. |
| Edit | Clone, rename, move, delete, and create assets in place, then run processors over one type or all of its instances. |

DxVisualizer is free forever. There are no subscriptions, no paid upgrades,
and no feature-gated tiers. The full source is MIT-licensed, and every
capability described here ships in the free package.

## Requirements

- Unity 2021.3 or newer, the floor declared in `package.json`. The window is
  developed and tested on Unity 6.
- No additional package dependencies.

## Install

1. Open **Window → Package Manager**.
2. Select **+**, then **Add package from git URL**.
3. Enter `https://github.com/wallstop/DataVisualizer.git` and select **Add**.

Open the window with **Tools → Wallstop Studios → DxVisualizer**.
[Getting started](getting-started.md) walks through the first session.

!!! note "Alpha"

    Built for Wallstop Studios' internal use. There is no support commitment
    yet, and any release can break compatibility. Feel free to use it, and
    report what breaks.

## Where to go next

- [Getting started](getting-started.md): install the package, open the window, and edit your first asset.
- [Search and filtering](search-and-filter.md): global search, the type filter, and label filtering.
- [Managing assets](managing-assets.md): create, clone, rename, move, delete, and run processors.
- [Organizing your data](organizing.md): ordering, layout, themes, persistence, and Play Mode.
- [Extending the window](extending.md): attributes, `BaseDataObject`, processors, and custom inspector content.
- [Troubleshooting](troubleshooting.md): missing types, hidden rows, processors, and themes.
- [Migrating from Data Visualizer](migration.md): what the rename changed, and what it didn't.
- [Roadmap](roadmap.md): data sources planned beyond ScriptableObjects.
- [Issues](https://github.com/wallstop/DataVisualizer/issues): known problems and feature requests.
