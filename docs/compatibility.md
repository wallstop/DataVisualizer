# Compatibility matrix

The package declares Unity `2021.3` as its minimum version and keeps editor-only
code behind the editor assembly. The table distinguishes verified evidence from
environments that are not available in the current local session.

| Environment | EditMode | PlayMode | Player smoke | Status |
| --- | --- | --- | --- | --- |
| Unity 6000.4.6f1, macOS host | 64/64 prior facade slices | 5/5 runtime lifecycle | Not run | Verified for prior slices |
| Unity 2022.3 | Not run | Not run | Not run | Unverified |
| Unity 2021.3 | Not run | Not run | Not run | Unverified |
| Windows editor | Harness lint only | Not run | Not run | Unverified |
| Linux editor | Not run | Not run | Not run | Unverified |
| Optional Odin Inspector | Not available | Not available | N/A | Unverified |

The shared local Unity host currently times out on editor-status requests after a
large benchmark attempt. No new facade or JSON-adapter compilation claim is made
until the host is responsive. Missing OS/editor combinations remain unverified.

## Runtime boundary

Player projects reference the minimal `WallstopStudios.DataVisualizer` runtime
assembly. The editor window, `UnityEditor` APIs, settings, capture helpers, and
editor tests are excluded through folders and assembly definitions. `BaseDataObject`
and its public lifecycle/display interfaces remain available to player code.

## Local verification checklist

When the required editors are installed, run both Unity test platforms directly and
record the exact patch version, OS, graphics device, DPI/theme, window dimensions,
reload settings, and player compile/build result. Exercise light/dark themes,
100%/150%/200% DPI, compact layouts, docking, mixed-monitor movement, and both
default and optional inspector paths. Do not convert an unavailable environment
into a passing result.
