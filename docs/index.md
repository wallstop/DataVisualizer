# Data Visualizer

Data Visualizer is a fast, focused editor for finding, inspecting, organizing,
and editing Unity `ScriptableObject` assets. It keeps navigation, search, labels,
ordering, persistence, and the selected inspector in one window without adding a
runtime dependency to player builds.

## The workflow

1. Install the package through Unity Package Manager.
2. Open **Window → Data Visualizer**.
3. Choose a namespace and type, then inspect or edit the selected asset.
4. Use search, labels, ordering, and asset actions to reach the next object.

![Data Visualizer layout](images/data-visualizer-layout.jpg){ loading=lazy }

The package supports Unity 2021.3 and newer. The editor assembly, tests, settings,
and automation tools are excluded from players; the runtime assembly contains the
small compatibility surface used by `BaseDataObject` and its extension interfaces.

!!! note

    Performance claims in this site are measured results, not promises of instant
    cold loading. See [Performance](performance.md) for fixture sizes, sample
    counts, hardware, and open limitations.

## Continue

- [Getting started](getting-started.md) for installation and first use.
- [User guide](user-guide.md) for search, labels, ordering, persistence, and processors.
- [Automation](automation.md) for typed C# and versioned JSON requests.
- [Compatibility](compatibility.md) for verified and unverified environments.
