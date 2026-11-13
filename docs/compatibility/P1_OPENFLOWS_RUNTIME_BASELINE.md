# P1 OpenFlows Runtime Compatibility Baseline

**Phase result:** P1_PASS for the bounded primary-runtime assembly-load baseline.
**Compatibility direction:** COMPATIBILITY_DIRECTION_ESTABLISHED.
**P2 readiness:** P2_READY for vendor-neutral contracts and capability statuses.

Detected product version/build is metadata only and is not a SewerPilot
compatibility switch. This report establishes one observed runtime boundary;
it does not certify every installed assembly, model API, license or solver operation.

## Repository State

- Branch: `main`.
- BASE HEAD: `aa70160110fd631276219c4d2a6e80e4111ee319`.
- Starting worktree: clean; P0-RN and P0-B closed/PASS.
- Product discovery timestamp and exact source coverage: `runtime_discovery.json`.
- Historical P0 reports remain historical. Their recorded runtime absence is
  preserved; the current installation is new P1 evidence, not a rewrite of P0.
- No production solution, `src/` project, UI framework or P2 implementation created.

## Host Foundation Reuse

The starting `env.ps1` and `verify.ps1` checks passed. Discovery mode is `auto`;
no local override was present and no `.env.local` was created. The existing
build configuration and common build properties remain unchanged. The disposable
probe uses locally discovered MSBuild and installed framework reference assemblies,
without NuGet dependencies, restore, downloads or machine-wide environment changes.
Final foundation, path, hash and scope results are in `verification.json`.

## Detected Bentley Products

Uninstall metadata in both machine registry views reports **OpenFlows Sewer 2024**,
publisher **Bentley Systems, Incorporated**, display version **24.0.2.21**.
The 64-bit uninstall entry and Bentley Sewer registry installation field agree on
`C:\Program Files\Bentley\OpenFlows Sewer`.

Registry reads covered HKLM/HKCU, both 32/64-bit views, uninstall metadata and
Bentley/Bentley Systems/Haestad keys. Only selected product/installation fields
were read; licensing/credential keys were skipped. Standard vendor/shared folders
and registry-derived locations were surveyed with explicit depth/file/key bounds.
Discovery reported no access errors or coverage limits. No full-disk crawl occurred.

| Candidate | Executable identity | File/product version | PE architecture |
|---|---|---|---|
| RT-001 | Bentley SewerCAD / `SewerCAD.exe` | 24.0.2.21 | AMD64 |
| RT-002 | Bentley SewerGEMS / `SewerGEMS.exe` | 24.0.2.21 | AMD64 |

These are two executable identities in one installation, not two competing
installations. Missing prospective vendor roots in discovery evidence are explicitly
marked `Exists=false`; they are not claimed as detected paths.

## Selected Runtime Candidate

Selected **RT-002**, `C:\Program Files\Bentley\OpenFlows Sewer\SewerGEMS.exe`,
because it is the requested SewerGEMS product identity. The precise product root
is its executable directory; the broader `InstallRoot` in discovery records is
the original search root, not an override configuration. Selection did not compare
product years or choose the newest version.

The primary layout, `x86/` and `net6/` variants were inspected separately.
Only the primary .NET Framework/x64 core load boundary was runtime-tested.

## WaterObjects Assembly Inventory

`runtime_metadata.json` records eight relevant Haestad assemblies in each of three
layouts, plus both product executable identities and the installed managed helper
`WaterObjects.Net.PostCalculationProcessor.exe`. The helper's actual assembly
identity is `WaterObjects.Net.PostCalculationProcessor`, not merely a filename match.

Each inventory item records path, size, SHA-256, assembly identity/version,
file/product version, metadata CLR version, TargetFrameworkAttribute, PE machine,
derived processor architecture, CorFlags and public-key token. Metadata inspection
uses PEReader without loading/executing vendor code. Assembly roles are **INFERRED**
from identity and static evidence; functional capabilities are classified separately.

The two explicitly referenced and runtime-loaded assemblies are:

| Assembly | SHA-256 |
|---|---|
| Haestad.Support.dll | `770FFEF9F69598B391AF2E89EFBA72BC233A28FE5087033D931C00995B0C545D` |
| Haestad.Domain.dll | `3C3DC211F04E1B96EF2777A5EB1489E58AA4A52196F174B1C53A1614706B741C` |

Both have assembly-version metadata `24.0.2.0` and public-key token
`b4db808388021f6e`. Neither value is pinned in the probe's references or used
as a compatibility decision. No Bentley binaries were copied into or committed with
the repository.

## Runtime / Target Framework Evidence

- Primary Haestad inventory and executable attributes: `.NETFramework,Version=v4.8`.
- Primary executable configuration: CLR `v4.0`, framework SKU `.NETFramework,Version=v4.8`.
- Primary assembly metadata CLR: `v4.0.30319`.
- Probe process: CLR `4.0.30319.42000`, loading the primary assemblies successfully.
- `x86/` inspected assemblies: .NET Framework 4.8, statically inspected only.
- `net6/` inspected assemblies: `.NETCoreApp,Version=v6.0`, statically inspected only.
  The COM runtime configuration declares Microsoft.NETCore.App and
  Microsoft.WindowsDesktop.App; their presence in configuration is not runtime proof.

The minimum **proven core load/probe direction** is .NET Framework 4.8/x64.
This applies only to the Bentley-facing probe/compatibility boundary. It selects
no framework for vendor-neutral domain projects or the UI and does not prohibit
later independently validated adapters.

## Architecture Evidence

Primary executables have AMD64 PE headers. Pure core assemblies are PE I386/MSIL,
ILOnly, without Requires32Bit; this permits an x64 host. Primary
`Haestad.Support.Mixed.dll` is AMD64 mixed/native code. Its x86 counterpart is I386
mixed/native code, so mixing layouts is unsafe.

Actual Haestad.Domain reference metadata includes Haestad.Support, Haestad.SQLite,
Haestad.LicensingFacade and framework/BCL dependencies. The latter two vendor files
are present and AMD64 mixed/native assemblies, recorded in `dependency_metadata.json`.
They were metadata-inspected, not loaded by the probe. A missing local BCL DLL in
the inventory means framework resolution is required, not that the dependency is
absent; successful compilation/core load verifies only the exercised resolution.
Native engine loading, database access and licensing initialization remain unproven.

## SDK / Documentation Evidence

Installed `SewerGEMS.chm` and `SewerCAD.chm` product help files exist in the product
root. No C#/VB sample projects/source or matching XML documentation for the selected
core assemblies were found within the inspected installation. A complete developer
SDK, supported model-open recipe and API licensing requirements are **NOT_ESTABLISHED**.
Product help presence is not treated as complete WaterObjects API documentation.

## Safe Sample Model

A Bentley-installed tutorial bundle was selected by sorted filename under
`Lessons\SewerGEMS`: `Lesson_1.stsw`, size **77,088 bytes**, plus its `.dwh` and
`.sqlite` companions. Its installed tutorial location supplies vendor-sample
provenance; no user/production engineering model was selected.

- Path: `C:\Program Files\Bentley\OpenFlows Sewer\Lessons\SewerGEMS\Lesson_1.stsw`.
- Initial SHA-256: `032665DC5733359BCECC699E9EC91C66515A78BCFC59B731E3B1C40F67778493`.
- The model was **not opened through Bentley or a model API**; files were hashed only.
- `runtime_metadata.json` contains initial hashes for all three bundle files;
  `verification.json` contains the post-probe comparisons. No copies/binaries enter Git.

## Compatibility Probe

Location: `tools/probes/WaterObjectsCompatibilityProbe/`.
Target: .NET Framework 4.8, x64, directly derived from the observed primary layout.
Vendor references use the runtime-resolved `OpenFlowsHome` MSBuild property, no
absolute HintPath or exact assembly-version binding, and `Private=False`.

**Attempt 1:** build exit 0; runtime exit 0; `PROBE_STATUS: PASS`.
**Attempt 2:** not used. No framework guessing or retry loop.

The probe loaded only Haestad.Support and Haestad.Domain, read identity/framework
metadata and bound the public types Haestad.Support.Cad.CADEntityType and
Haestad.Domain.AlternativeType. It did not invoke vendor API methods or explicitly
initialize licensing, a model, scenarios, alternatives, calculations or writes.
Vendor-internal assembly-loader effects are not independently excluded, and load
success is not proof of a usable license or full application initialization.

## Capability Matrix

| Capability | Classification | Evidence boundary |
|---|---|---|
| ProductDetected | SUPPORTED | Registry agreement and actual SewerGEMS executable metadata |
| WaterObjectsPresent | SUPPORTED | Actual WaterObjects helper identity and installed Haestad core API assemblies; presence only |
| ManagedAssembliesLoad | SUPPORTED | Primary Haestad.Support/Domain only; runtime load and public type binding |
| ModelReadEvidence | NOT_TESTED | No model API invocation |
| ScenarioReadEvidence | NOT_TESTED | No model/scenario API invocation |
| ScenarioCreateEvidence | NOT_TESTED | Write-capable operation outside this probe |
| AlternativeReadEvidence | NOT_TESTED | No model/alternative API invocation |
| AlternativeCreateEvidence | NOT_TESTED | Write-capable operation outside this probe |
| SolverRunEvidence | NOT_TESTED | No solver/API execution |
| ResultReadEvidence | NOT_TESTED | No calculation results accessed |
| ModelWriteEvidence | NOT_TESTED | No writes or save API invocation |

Static public type evidence is not proof of engineering operation support.
Untested capabilities are not asserted NOT_SUPPORTED.

## Version-Tolerance Assessment

Executable/assembly versions and paths are observations in evidence, not product
configuration or business logic. Discovery is registry/environment based; the
probe accepts a resolved root, references simple assembly names and uses no
year/build/version comparison. Runtime differences remain isolated for future
compatibility adapters. Canonical product build/test behavior remains independent
of the disposable probe.

## Remaining Unknowns

- Supported/licensed initialization and documented read-only model-open workflow.
- Native/mixed-mode engine dependency loading and full WaterObjects initialization.
- Model/scenario/alternative/results APIs and solver/write behavior.
- Runtime behavior of the x86 and net6 layouts and other installed product releases.
- Any SDK/material outside the inspected relevant vendor roots.

## P2 Readiness

**P2_READY:** the required product, core API metadata, framework/platform direction
and actual core load evidence exist. P2 may define vendor-neutral OpenFlows
contracts and a capability model with explicit NOT_TESTED/NOT_ESTABLISHED states.

P2 must not leak Haestad/WaterObjects types into domain modules, choose all-domain
frameworks from this probe, introduce version switches, or claim unproven model
or solver capabilities. A later Bentley-facing adapter may start from the proven
.NET Framework 4.8/x64 load direction, while native/licensing/model workflows need
their own bounded acceptance evidence. P2 is not implemented here.

## Evidence Index

All evidence paths below are repository-relative under `docs/evidence/p1/`:

- `runtime_discovery.json`: filtered registry/product records, bounded search roots,
  deterministic candidate IDs, coverage and error observations.
- `runtime_metadata.json`: 24 core layout assembly records, executable metadata,
  runtime configuration, local help/source findings and initial vendor sample hashes.
- `dependency_metadata.json`: two primary native/mixed Domain dependencies.
- `probe_attempt_1.json`: exact build/runtime output and zero-retry record.
- `verification.json`: final path/hash/integrity/foundation/scope checks.
- `scripts/discover_runtime.ps1`: reproducible bounded product discovery.
- `scripts/inspect_runtime.ps1`: reproducible metadata-only static inspection.

The separate probe README/source/project/runner records its build and execution
boundary. Atria reviews the bounded final diff/evidence once, read-only and advisory;
its outcome is recorded in the phase completion report, without another code/test loop.
