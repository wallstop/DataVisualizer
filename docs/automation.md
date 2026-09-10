# C# and JSON automation

The editor assembly exposes the window-independent
`WallstopStudios.DataVisualizer.Editor.Automation.DataVisualizerAutomation` facade.
It does not require a `DataVisualizer` window to exist.

## Typed C# calls

```csharp
using WallstopStudios.DataVisualizer.Editor.Automation;

DataVisualizerConfiguration configuration =
    DataVisualizerAutomation.ReadConfiguration();

DataVisualizerAssetMetadataPage page =
    DataVisualizerAutomation.QueryAssetMetadata(
        assemblyQualifiedTypeName: null,
        page: 0,
        pageSize: 100
    );

DataVisualizerAssetOperationResult preview =
    DataVisualizerAutomation.PreviewAssetOperation(
        new DataVisualizerAssetOperationRequest
        {
            operation = DataVisualizerAssetOperationKind.Rename,
            guids = new[] { "0123456789abcdef0123456789abcdef" },
            value = "RenamedData",
        }
    );
```

Metadata is ordered by path and GUID and uses assembly-qualified type names. Asset
operation results include the request GUID, original/resulting paths, resulting GUIDs
for Create/Clone, per-item diagnostics, and `complete`/`succeeded` status. Preview validates targets and move
destinations without running lifecycle hooks; apply revalidates before mutating.
Create and clone use the same operation surface: Create takes an assembly-qualified
type, asset name, and optional destination folder; Clone takes one source GUID and
an optional clone name. Create/clone run `ICreatable`/`IDuplicable` hooks in the
same before-create/before-clone, save, after-create/after-clone order as the UI.
Serialized-property operations take a property path and an explicit value kind
(`String`, `Boolean`, numeric, enum, Unity value, collection size, or asset-reference
GUID). Unsupported or mismatched property kinds fail before mutation; preview never
authors serialized data.

## JSON schema version 1

JSON requests contain `schemaVersion: 1`, a nonempty caller `requestId`, a numeric
`operation` value from `DataVisualizerAutomationRequestKind`, and typed arguments.
The shipped schemas and examples are available here:

- [Request schema](automation/data-visualizer-automation-request.schema.json)
- [Result schema](automation/data-visualizer-automation-result.schema.json)
- [Metadata request example](automation/data-visualizer-automation-request.example.json)
- [Asset operation example](automation/data-visualizer-automation-operation.example.json)
- [Create asset example](automation/data-visualizer-automation-create.example.json)

In-process code can call `DispatchRequest` or `ExecuteRequestJson`. A direct Unity
run uses:

```text
-executeMethod WallstopStudios.DataVisualizer.Editor.Automation.DataVisualizerAutomation.ExecuteRequestFile
-dataVisualizerRequest /absolute/path/request.json
-dataVisualizerResult /absolute/path/result.json
```

The file entrypoint reads one request, writes one final structured result, and exits
with code `0` for success, `1` for a failed request, or `2` for invalid runner
arguments. Request and result paths must be absolute, different files outside
the project's `Assets` folder. The result is written through a temporary file
replacement so a failed write cannot overwrite the previous complete result.
It does not start a watcher, daemon, HTTP listener, or custom MCP server.

## Boundaries

Configuration changes and asset mutations reject Play Mode. The current bounded
surface does not expose arbitrary C# evaluation, reflective method invocation,
serialized-property mutation, cancellation/progress for long-running indexing, or
processor execution through JSON. Callers must handle partial completion and must
not assume transaction rollback across filesystem operations or extension hooks.
