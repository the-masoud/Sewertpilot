# Public source build

## Entry point and scope

From a fresh Windows checkout:

```powershell
dotnet build .\SewerPilot.Public.slnf -c Release --nologo -m:1
```

[SewerPilot.Public.slnf](../SewerPilot.Public.slnf) filters the existing solution; it does not introduce a parallel architecture or disable production behavior.

| Included projects | Target |
| --- | --- |
| OpenFlows.Contracts | netstandard2.0 |
| OpenFlows.Compatibility, OpenFlows.Adapter, OpenFlows.RuntimeHost, Desktop, MCP Server | net48 / x64 |
| Contracts.Tests | net8.0 |
| Compatibility.Tests, MCP.Server.Tests | net48 / x64 |

The application uses reflection/runtime capability detection for Bentley integration, so these assemblies compile without Bentley DLLs. Runtime functionality still requires the appropriate commercial product and license. The public build neither opens a model nor invokes a solver.

**Excluded:** `SewerPilot.OpenFlows.Adapter.Tests`, which references the Autodesk plugin and includes private/native/evidence-dependent entry points. Its assertions and project remain intact in the full solution; excluding it from this filter is not a passing test result. The Civil 3D plugin is reached through that test project's reference. Two absent optional probes were removed from the solution, not replaced by fake implementations.

## Prerequisites

- Windows 11 x64; .NET Framework 4.8 targeting assemblies/Developer Pack (available through VS2022's .NET desktop workload).
- A current .NET CLI able to compile C# 12. Verified: SDK **10.0.404**, MSBuild from that SDK. A Visual Studio IDE build was not performed.
- Access to `https://api.nuget.org/v3/index.json`. The root `NuGet.Config` clears inherited package sources and selects this public feed. MCP dependencies are pinned/locked; no corporate feed is needed.
- Executing the contracts harness additionally requires its existing .NET 8 runtime target. Building the executable does not verify the runtime's support lifecycle.

## Repairs and boundaries

1. The existing solution no longer refers to missing `OpenFlowsRuntimeBindingAcceptance` or `OpenFlowsReadOnlyModelAcceptance` projects under `tools/probes/`.
2. The original accepted `watchdog.py` is included at [Runtime/watchdog.py](../src/SewerPilot.OpenFlows.Adapter/Runtime/watchdog.py), and the adapter embeds it under the **same logical resource name**. It was transferred byte-for-byte from the original source; no timeout, Job Object, process-ownership, calculation or acceptance logic changed.
3. The public filter builds useful application components and three test harnesses without the Autodesk-dependent adapter harness.
4. Optional Autodesk references now use `AutoCADManagedDir` and `Civil3DManagedDir`, retain `Private=False`, and produce an explicit missing-SDK error. They do not redistribute vendor assemblies or prove compatibility with new SDK versions.
5. Public examples and retained test paths have personal profile names redacted. The P16 fixture README labels its exported JSON as privacy-redacted replay material, not byte-identical original receipts. Recorded engineering values and inventory hashes were not changed.

The legacy `tools/build.ps1` targets the full solution and disables restore. It remains available for fully provisioned development environments, not as the recommended public-filter entry point. `tools/test.ps1` discovers all test projects and is likewise broader than the public scope. See [CONTRIBUTING.md](../CONTRIBUTING.md) for explicit test commands.

## Recorded verification — 2026-11-14

| Item | Actual observation |
| --- | --- |
| Starting public commit | `db6d86417ac05eb9bc800bdcb494d07372bf9ec6` |
| Host | Windows 11 Pro x64 |
| Build command | `dotnet build .\SewerPilot.Public.slnf -c Release --nologo -m:1` |
| Restore isolation | `NUGET_PACKAGES` set to a new, initially empty directory outside the clone; public NuGet feed |
| Outcome | **PASS**, exit 0, first and only build attempt; all nine selected projects built |
| Diagnostics | 0 errors; existing warnings CS0108 (`EngineeringDesignPanel.Text`) and CS0219 (`P16FarzaneganNativeReadHost.opened`) |
| Elapsed build time | 37.86 seconds, recorded for this host only; not a performance benchmark |
| Tests/native operations | No test harnesses executed, no models opened, no hydraulic solves, no installer built |

The isolated build proves that this public source scope can restore and compile on the recorded developer host with its installed targeting assemblies. It is not an independently provisioned clean-machine, Visual Studio IDE, bit-for-bit reproducible binary, native-runtime or installer qualification. Historical software and engineering acceptance statuses are unchanged.

## Remaining commercial/runtime work

Full adapter-suite builds require licensed Autodesk SDK assemblies and the explicitly documented local configuration. Native OpenFlows behavior depends on the installed runtime, model configuration, external data and authorized evidence; no universal compatibility or new acceptance of Bentley 24.0.2.21 is asserted. The internal unsigned installer is not a public release.
