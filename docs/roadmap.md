# Roadmap

DxVisualizer works with ScriptableObject main assets today. This page lists proposed sources. Nothing here has a release date; the
[issue tracker](https://github.com/Ambiguous-Interactive/DxVisualizer/issues) has the current state of each item.

## Current scope

- Main ScriptableObject assets, matched by exact type.
- Subassets are out of scope. A ScriptableObject stored inside another asset is not listed.

## Planned data sources

| Source               | Status                          | What it would add                                                                                                                             |
| -------------------- | ------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------- |
| Prefab components    | Read-only feasibility evaluated | [Proposed read-only discovery and inspection](https://github.com/Ambiguous-Interactive/DxVisualizer/issues/321); editing remains unsupported. |
| Addressables entries | Planned                         | Assets listed by Addressables group and address, next to their type.                                                                          |

Any adopted source needs an explicit identity, persistence and operation contract. Test experiments do not enable new data sources. Existing ScriptableObject behavior and saved
arrangements remain the compatibility boundary.

## Suggest a source

Open a [feature request](https://github.com/Ambiguous-Interactive/DxVisualizer/issues/new/choose) with the data you edit today, where it lives, and how you find it.
