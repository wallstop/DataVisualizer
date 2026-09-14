using System.Diagnostics.CodeAnalysis;

/*
    Sanctioned SonarWay suppressions for the runtime assembly, recorded by the owner on
    issue #85 (criteria 2-3). Every entry targets one specific site; no assembly-wide rule
    weakening. Enforcement contract: with the pinned SonarAnalyzer.CSharp set installed on
    the Unity host, compiling this assembly must produce zero S-diagnostics.
*/

[assembly: SuppressMessage(
    "Sonar Code Smell",
    "S101",
    Justification = "Name frozen by the runtime compatibility contract (RuntimeSignatureContractTests); see issue #85 criterion 3.",
    Scope = "type",
    Target = "T:WallstopStudios.DataVisualizer.DataVisualizerGUIContext"
)]
[assembly: SuppressMessage(
    "Sonar Code Smell",
    "S101",
    Justification = "Name frozen by the runtime compatibility contract (RuntimeSignatureContractTests); see issue #85 criterion 3.",
    Scope = "type",
    Target = "T:WallstopStudios.DataVisualizer.IGUIProvider"
)]
