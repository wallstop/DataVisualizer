using System.Diagnostics.CodeAnalysis;

/*
    Sanctioned SonarWay suppressions for the editor test assembly, recorded by the owner on
    issue #85 (criteria 2-3). Every entry targets one specific site; no assembly-wide rule
    weakening. Enforcement contract: with the pinned SonarAnalyzer.CSharp set installed on
    the Unity host, compiling this assembly must produce zero S-diagnostics.
*/

[assembly: SuppressMessage(
    "Sonar Code Smell",
    "S2094",
    Justification = "Deliberately empty ScriptableObject fixture; the empty body is the tested subject.",
    Scope = "type",
    Target = "T:WallstopStudios.DataVisualizer.Tests.Editor.EditorOnlyCreationDataFileMarker"
)]
[assembly: SuppressMessage(
    "Sonar Code Smell",
    "S2094",
    Justification = "Deliberately empty ScriptableObject fixture; the empty body is the tested subject.",
    Scope = "type",
    Target = "T:WallstopStudios.DataVisualizer.Tests.Editor.EditorOnlyCreationData"
)]
[assembly: SuppressMessage(
    "Sonar Code Smell",
    "S2094",
    Justification = "Deliberately empty ScriptableObject fixture; the empty body is the tested subject.",
    Scope = "type",
    Target = "T:WallstopStudios.DataVisualizer.Tests.Editor.EditorOnlyCreationSubasset"
)]
[assembly: SuppressMessage(
    "Sonar Code Smell",
    "S2094",
    Justification = "Deliberately empty ScriptableObject fixture; the empty body is the tested subject.",
    Scope = "type",
    Target = "T:WallstopStudios.DataVisualizer.Tests.Editor.OtherEditorOnlyCreationData"
)]
[assembly: SuppressMessage(
    "Sonar Code Smell",
    "S2094",
    Justification = "Deliberately empty ScriptableObject fixture; the empty body is the tested subject.",
    Scope = "type",
    Target = "T:WallstopStudios.DataVisualizer.Tests.Editor.OtherSelectionPersistenceGuidData"
)]
[assembly: SuppressMessage(
    "Sonar Code Smell",
    "S2094",
    Justification = "Deliberately empty ScriptableObject fixture; the empty body is the tested subject.",
    Scope = "type",
    Target = "T:WallstopStudios.DataVisualizer.Tests.Editor.SelectionPersistenceGuidData"
)]
[assembly: SuppressMessage(
    "Sonar Code Smell",
    "S2094",
    Justification = "Deliberately empty ScriptableObject fixture; the empty body is the tested subject.",
    Scope = "type",
    Target = "T:WallstopStudios.DataVisualizer.Tests.Editor.UnindexedAssetGuidDiscoveryData"
)]
[assembly: SuppressMessage(
    "Sonar Code Smell",
    "S2094",
    Justification = "Deliberately empty ScriptableObject fixture; the empty body is the tested subject.",
    Scope = "type",
    Target = "T:WallstopStudios.DataVisualizer.Tests.Editor.TypeIdentityCollision.First.Data.OrderCollisionData"
)]
[assembly: SuppressMessage(
    "Sonar Code Smell",
    "S2094",
    Justification = "Deliberately empty ScriptableObject fixture; the empty body is the tested subject.",
    Scope = "type",
    Target = "T:WallstopStudios.DataVisualizer.Tests.Editor.TypeIdentityCollision.Second.Data.OrderCollisionData"
)]
[assembly: SuppressMessage(
    "Sonar Code Smell",
    "S2094",
    Justification = "Deliberately empty ScriptableObject fixture; the empty body is the tested subject.",
    Scope = "type",
    Target = "T:WallstopStudios.DataVisualizer.Tests.Editor.DerivedEditorOnlyCreationData"
)]
[assembly: SuppressMessage(
    "Sonar Code Smell",
    "S2094",
    Justification = "Deliberately empty EditorWindow fixture; the empty body is the tested subject.",
    Scope = "type",
    Target = "T:WallstopStudios.DataVisualizer.Tests.Editor.LayoutTestWindow"
)]
[assembly: SuppressMessage(
    "Sonar Code Smell",
    "S3966",
    Justification = "Double disposal is the tested behavior; leases and scopes are idempotent.",
    Scope = "member",
    Target = "M:WallstopStudios.DataVisualizer.Tests.Editor.DisposalScopeTests.ShouldDisposeReusableLeaseOnlyOnceAcrossCopiesAndSlotReuse"
)]
[assembly: SuppressMessage(
    "Sonar Code Smell",
    "S3966",
    Justification = "Double disposal is the tested behavior; leases and scopes are idempotent.",
    Scope = "member",
    Target = "M:WallstopStudios.DataVisualizer.Tests.Editor.DisposalScopeTests.ShouldRunEveryTestCleanupInReverseOrderOnlyOnce"
)]
[assembly: SuppressMessage(
    "Sonar Code Smell",
    "S108",
    Justification = "Empty using block is the tested subject: acquire plus immediate dispose must run cleanup.",
    Scope = "member",
    Target = "M:WallstopStudios.DataVisualizer.Tests.Editor.DisposalScopeTests.ShouldAllowReusableCleanupToAcquireAnotherLease"
)]
