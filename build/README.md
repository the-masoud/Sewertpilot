# Public build entry point

Use `dotnet build .\SewerPilot.Public.slnf -c Release --nologo -m:1` from the repository root. See [current public build scope and verification](../docs/PUBLIC_BUILD.md) and [contributor setup](../CONTRIBUTING.md).

The foundation notes below are historical P2 documentation. Their package/framework assumptions do not describe all current projects. The legacy scripts target the full solution; the public filter is the documented build without Autodesk dependencies.

---

# Build and environment foundation

`BuildConfiguration.json` is machine-independent build policy. Schema version 1
requires exactly the seven supplied keys. Configuration names are `Debug` and
`Release`; the default must be one of them. Tool preference is an ordered,
nonduplicated list of `dotnet` and/or `msbuild`. `allowNoSolution` is a Boolean.
`solution` is null or a repository-relative `.sln` path; `artifactsDirectory` is a
repository-relative directory. Rooted paths, parent traversal and links that
redirect configured paths outside the repository are rejected.

P2 configures `solution=SewerPilot.sln` and `allowNoSolution=false`. The solution
contains the netstandard2.0 contracts library and the net8.0 test executable.
These scripts do not create projects or discover/probe a vendor runtime. They
do not choose a product-wide framework or UI framework. The null-solution branch
remains available to the configuration schema but is no longer current policy.

## Explicit restore prerequisite

The build remains restore-free. Before its first invocation on a new checkout,
restore the solution using an approved source. The only package prerequisite is
the SDK's implicit NETStandard.Library 2.0.3 reference package; the contracts have
no explicit package references and the executable tests have no test packages.
P2 used the existing local cache, without downloads or new tool installations:

```powershell
dotnet restore .\SewerPilot.sln --source (Join-Path $env:USERPROFILE '.nuget/packages') -p:NuGetAudit=false
```

This local source must already contain the framework reference package. If it
does not, provision that prerequisite through the developer's approved restore
process. Do not run restore again during canonical test execution. A local SDK
able to target net8.0 and a net8.0 runtime are required for the current harness.

## Entry points

- `tools/env.ps1` overlays built-in defaults, `.env.example`, then optional
  `.env.local`. It accepts only `SEWERPILOT_ENV`, `SEWERPILOT_LOG_LEVEL`,
  `SEWERPILOT_OPENFLOWS_DISCOVERY_MODE`, `SEWERPILOT_OPENFLOWS_HOME`,
  `SEWERPILOT_SAMPLE_MODEL` and `SEWERPILOT_ARTIFACTS_DIR`. Environment and log
  level must be nonempty; discovery mode must be exactly `auto`. Full-line
  comments and blanks are ignored. `KEY=VALUE` permits spaces and additional
  equals signs in values, and optional matching outer single/double quotes.
  Values are literal: no interpolation, command execution or inline comments.
  Unknown/duplicate keys and malformed lines fail with filename/line information
  without printing values. All input is validated before current-process variables
  are changed. Defaults/overrides replace existing values for these six variables;
  unrelated variables and machine/user settings are untouched.
- `tools/build.ps1 [-Configuration Debug|Release]` validates configuration,
  discovers a usable dotnet SDK and MSBuild from PATH or a local `vswhere`, and
  reports tool availability. A configured but missing solution is an error.
  Builds use the first available preferred tool with restore disabled and report
  `BUILD_STATUS: PASS` on success. Assets must already be restored as above.
- `tools/test.ps1 [-Configuration Debug|Release]` finds test `.csproj` files only
  beneath the repository's `tests/` directory, identified by `IsTestProject=true`
  or a `.Tests.csproj` filename. Directory/file links and generated directories
  are skipped. Projects with `SewerPilotTestHarness=true` run through dotnet run;
  other discovered projects use dotnet test. Both paths require prebuilt/restored
  projects and use `--no-build --no-restore`. The canonical P2 harness reports
  exact test/assertion counts and a nonzero exit code on failure. The repository
  entry point propagates output/failures and reports `TEST_STATUS: PASS` on success.
- `tools/verify.ps1` checks required files, build policy, environment semantics,
  Git ignores, active agent/reviewer policy, forbidden bindings, obvious secrets,
  tracked historical evidence presence/integrity, configured solution/project
  references, netstandard2.0 contract boundary and source/test vendor leakage.
  It rejects tracked binaries and obvious version/build compatibility branching.
  It reports
  `VERIFY_STATUS: PASS` or `FAIL` without rewriting files or executing product code.

Failures return exit 1 with concise findings. All paths resolve from script
location rather than the caller's working directory. No tools/packages are
installed, no downloads or network operations are initiated, and no engineering
model is opened. Tool discovery is local only. Dot-sourcing `env.ps1` or
`build.ps1` exposes helper functions without loading variables or invoking a build.

`Directory.Build.props` contains only deterministic build policy and a conditional
CI flag. It does not select language version, target framework, platform, runtime,
global nullable policy or warnings-as-errors policy.

Atria-Dawn-Preview is the read-only second reviewer, advisory/non-blocking only;
the primary runs all deterministic checks before the single advisory review.
