# SewerPilot

SewerPilot is a Windows engineering workflow assistant for Bentley OpenFlows Sewer / SewerGEMS, providing design assistance, quality checks, auditing and reporting. SewerGEMS/OpenFlows remains the hydraulic solver.

- **StormFlow:** stormwater and surface-water collection workflows.
- **GravityFlow:** gravity sanitary and industrial sewer workflows.

These modules keep engineering criteria independent. Criteria come from explicit project requirements, applicable standards and engineer decisions.

## Public source snapshot

Current source, tests, build scripts and architecture documentation exported on 2026-11-13. Private engineering models, client archives, operational reports, installers, vendor binaries, local configuration and prior Git history are excluded. Optional live tests require local models and evidence not distributed here. Retained P16 replay fixtures contain vendor tutorial observations described in their fixture README.

Publishing source does not grant engineering design approval or qualify a public installer release. No installer is distributed here.

## Development

Open `SewerPilot.sln` on Windows. Projects include .NET Framework 4.8 and .NET 8 targets. Bentley integration requires a separately installed and licensed compatible Bentley runtime; Civil 3D integration requires its own prerequisites. Vendor dependencies are not redistributed.

After installing/restoring the required dependencies, run from PowerShell:

```powershell
.\tools\build.ps1
.\tools\test.ps1
```

Scripts use local dependencies and disable automatic restore during build/test. See `build/README.md` and individual project files for configuration. Full live acceptance requires local models and evidence excluded from this snapshot.

Architecture: `docs/architecture/`. MCP examples: `docs/mcp/`.
