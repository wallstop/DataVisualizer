# Roadmap

DxVisualizer works with ScriptableObject assets today. This page lists the data sources planned beyond them. Nothing here has a release date; the
[issue tracker](https://github.com/wallstop/DataVisualizer/issues) has the current state of each item.

## Current scope

- Main ScriptableObject assets, matched by exact type.
- Subassets are out of scope. A ScriptableObject stored inside another asset is not listed.

## Planned data sources

| Source               | Status  | What it would add                                                                                              |
| -------------------- | ------- | -------------------------------------------------------------------------------------------------------------- |
| Prefab components    | Planned | Serialized component data on prefab assets, listed and edited in the same catalog, object list, and inspector. |
| Addressables entries | Planned | Assets listed by Addressables group and address, next to their type.                                           |

Each source would join the catalog alongside ScriptableObject types, with the same search, ordering, label filtering, and persistence rules described in the guides. The existing
public API, saved state, and theme tokens stay compatible.

## Suggest a source

Open a [feature request](https://github.com/wallstop/DataVisualizer/issues/new/choose) with the data you edit today, where it lives, and how you find it.
