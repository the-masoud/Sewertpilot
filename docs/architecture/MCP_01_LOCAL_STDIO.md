# MCP-01 — Local read-only stdio foundation

The dedicated SewerPilot.Mcp.Server executable targets .NET Framework 4.8, x64. It uses the official C# SDK ModelContextProtocol 2.2.0 and Microsoft.Extensions.Hosting 10.0.10; both direct requirements are exact NuGet ranges and resolved dependencies are locked. C# 12 is confined to the new projects; existing C# 7.3 projects and Bentley boundaries remain unchanged.

Codex starts the executable locally and exchanges MCP JSON-RPC through stdin/stdout. No HTTP listener, paid API, cloud service, runtime package download, bridge process or GUI helper is used. Diagnostics are classification-only stderr. The existing .NET Framework 4.8 installation remains a prerequisite.

Exactly one tool is registered: sewerpilot_get_project_info, with no arguments. The server captures SEWERPILOT_PROJECT_DIR from its startup environment. Calls cannot supply or override a directory. Unknown tools and additional arguments are explicitly refused.

The result is labelled STORED_PROJECT_METADATA and contains only project ID/name, schema/revision, selected module, stored model fingerprint and separate GravityFlow/StormFlow Design Basis reference identifiers/versions. These are stored values, not proof of live model identity, verified engineering artifacts, hydraulic results, readiness or approval.

The wrapper calls the public ProjectStore.LoadProject directly. It does not call ProjectWorkspace.Open, append audit, follow model references or load an engineering workspace. No RuntimeHost executable is part of the distribution. No Bentley runtime discovery, model open/save, solver or engineering authorization is registered.

Input must be a fully qualified local drive directory. UNC/device/alternate-data-stream/relative/traversal paths, trailing ambiguous characters and network drives are rejected. Windows handles are opened without following reparse points; every existing ancestor is pinned and inspected by handle. Existing project.json and optional audit.jsonl inputs are held with read sharing only, preventing replacement/writes while ProjectStore reads. Each input is limited to 16 MiB. A previously absent audit that appears during the read is rejected. Legacy projects without audit remain readable without creating an audit file.

Storage classifications are preserved; service exception messages, paths, audit contents, runtime credentials and stack traces are not returned. Read-only tool annotations are descriptive; enforcement comes from the one explicit handler and the guarded service call.

## Build and operate

The accepted Release Adapter, Compatibility and Contracts assemblies must already exist. Build the new projects directly with BuildProjectReferences=false to preserve those accepted outputs. Initial dependency resolution generates the two lock files; subsequent restore uses the project-scoped RestoreLockedMode=true settings.

1. dotnet restore tests/SewerPilot.Mcp.Server.Tests/SewerPilot.Mcp.Server.Tests.csproj
2. dotnet build src/SewerPilot.Mcp.Server/SewerPilot.Mcp.Server.csproj -c Release --no-restore -p:BuildProjectReferences=false
3. dotnet build tests/SewerPilot.Mcp.Server.Tests/SewerPilot.Mcp.Server.Tests.csproj -c Release --no-restore -p:BuildProjectReferences=false
4. Run the focused harness with the server executable path and an output JSON path.

The complete bin/Release/net48 directory is the offline application distribution. Include its DLLs, executable and generated binding-redirect .config. NuGet retrieval is a build-time operation only. The focused harness executes a copied bundle without HOME, package-cache or useful PATH environment entries. Deploy onto Windows with .NET Framework 4.8; no SDK is required to operate the server.

Set SEWERPILOT_PROJECT_DIR in the launching process to the chosen existing project. Run the executable with no command-line arguments under a stdio MCP client. On client disconnect/EOF, the server exits. No project path is accepted from tool calls.

The Codex example is docs/mcp/codex-config.example.toml. Update its executable location and project environment value, then merge only that server entry into the intended local configuration. MCP-01 does not edit user configuration. Codex integration acceptance is reported separately from official SDK protocol acceptance.

## Verification boundary

The focused harness uses the official SDK client. It verifies initialization, one zero-argument read-only tool, minimal dual-module stored metadata, malformed state/schema/audit, audit identity, security classification, oversize and busy input rejection, unsafe directory and actual reparse traversal rejection, unchanged inputs, JSON-RPC-only stdout, sanitized stderr, offline copied-bundle operation and clean shutdown. An unprivileged Windows Job Object limits the owned server to one process; total-process accounting must remain exactly one, so no vendor/RuntimeHost child can start during requests. This does not monitor unrelated applications. It writes TEST_ONLY fixtures outside the repository; those test-owned fixture edits are not server writes.

No historical regression suite, GUI or Bentley test is part of MCP-01. P15–P18 acceptance, result readback and MCP-02/MCP-03 remain separate.

## Dependencies and licensing

ModelContextProtocol and ModelContextProtocol.Core are official Apache-2.0 SDK packages; Microsoft.Extensions.Hosting is MIT licensed. Dependency license metadata and package identities are retained in the verification evidence. No repository-wide license change is made by this implementation.

Official references:
- https://github.com/modelcontextprotocol/csharp-sdk
- https://www.nuget.org/packages/ModelContextProtocol/2.2.0
- https://www.nuget.org/packages/Microsoft.Extensions.Hosting/10.0.10
- https://learn.chatgpt.com/docs/extend/mcp?surface=cli
