# Contributing to SewerPilot

SewerPilot helps engineers inspect sewer/drainage networks, protect original models and verify Bentley hydraulic results. Contributions to diagnostics, accessibility, documentation, compatibility adapters, offline tests and reproducible builds are welcome. StormFlow and GravityFlow keep their engineering criteria separate.

## Development environment

Use Windows 11 x64 with the .NET Framework 4.8 Developer Pack/targeting assemblies and a current .NET CLI supporting C# 12. The public build was verified with .NET SDK 10.0.404. Visual Studio 2022 with .NET desktop development can provide the targeting assemblies and serve as an editor; this verification used the CLI, not a Visual Studio IDE build. SDK 10 integration inside VS2022 is not claimed.

The contracts test executable still targets `net8.0` and requires a .NET 8 runtime to execute. The desktop, runtime host, adapter, compatibility and MCP components target `net48`; contracts target `netstandard2.0`. Framework modernization is separate work.

## Clean-checkout setup and public build

```powershell
git clone https://github.com/the-masoud/Sewertpilot.git
cd Sewertpilot
dotnet build .\SewerPilot.Public.slnf -c Release --nologo -m:1
```

Restore runs as part of this command using the public source in `NuGet.Config`. MCP projects retain their checked-in lock files and locked restore. Do not add feed credentials to the repository. See [public build scope and evidence](docs/PUBLIC_BUILD.md) for all included/excluded projects, prerequisites and limitations.

This builds the desktop application, runtime host, adapter, MCP server, contracts, compatibility layer and three test harnesses. Bentley and Autodesk are not compile-time requirements for this filter. Building does not run tests or prove native hydraulic behavior.

## Offline tests

After a successful public build, run the small contracts and compatibility harnesses individually:

```powershell
dotnet run --project .\tests\SewerPilot.OpenFlows.Contracts.Tests -c Release --no-build --no-restore
.\tests\SewerPilot.OpenFlows.Compatibility.Tests\bin\x64\Release\net48\SewerPilot.OpenFlows.Compatibility.Tests.exe
```

The MCP harness is a separate Windows process-integration test. It uses synthetic project metadata and an owned child server process; it does not require a Bentley model. Review its [entry point](tests/SewerPilot.Mcp.Server.Tests/Program.cs) before running:

```powershell
New-Item -ItemType Directory -Force .\artifacts | Out-Null
.\tests\SewerPilot.Mcp.Server.Tests\bin\Release\net48\SewerPilot.Mcp.Server.Tests.exe .\src\SewerPilot.Mcp.Server\bin\Release\net48\SewerPilot.Mcp.Server.exe .\artifacts\mcp-public-tests.json
```

These commands are contributor instructions, not claims of execution during the public-build repair. The adapter test project also contains native/live and historical evidence-materialization entry points. Do not run it against customer models or treat the legacy all-project `tools/test.ps1` as a public-filter test command.

## Optional commercial integrations

The broader `SewerPilot.sln` includes adapter tests that reference the Civil 3D plugin. They require separately licensed Autodesk assemblies. Set environment variables `AutoCADManagedDir` (containing `accoremgd.dll`, `acdbmgd.dll`, `acmgd.dll`, `AecBaseMgd.dll`) and `Civil3DManagedDir` (containing `AeccDbMgd.dll`) to your installation's actual locations before building the full solution. No automatic SDK acquisition or version-wide compatibility is implied; this optional build was not verified in this mission.

Bentley model intake/calculation requires a separately installed and licensed compatible OpenFlows Sewer/SewerGEMS runtime. The requested 24.0.2.21 configuration was not newly runtime-qualified here. Native tests also need explicitly authorized models, complete external dependencies and their own bounded execution plan. Do not introduce substitute Bentley interfaces or weaken production acceptance gates.

## Pull requests and documentation

- Open an issue for substantial changes; keep patches focused and describe the user-visible result.
- Include the exact build/test commands, actual outcomes and dependency scope. Distinguish historical evidence from newly executed checks.
- Preserve original engineering models and safety gates. Never invent criteria, solver results, approvals or compatibility claims. Software verification and engineering approval are separate.
- Keep Bentley API differences behind compatibility boundaries. Do not hard-code a developer's installation or personal paths.
- Check relative links, image provenance, technical claims and `git diff --check`. Use genuine application captures and exclude private data.
- Submit only work you have the right to contribute under [Apache-2.0](LICENSE); retain third-party notices and disclose borrowed code/dependencies. See [license boundaries](docs/LICENSING.md).

## Issues, security and privacy

Report the component, operating system, runtime/tool versions, steps and expected/actual behavior. Use minimal synthetic fixtures and reviewed, redacted logs. Never post credentials, corporate addresses, customer models, private acceptance archives or vendor DLLs. Do not publish a vulnerability's exploit details or sensitive diagnostic archive in a public issue; contact the maintainer through an established private channel first. No dedicated security mailbox or response-time commitment is advertised.
