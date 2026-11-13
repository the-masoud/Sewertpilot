# P2 — Vendor-neutral OpenFlows contracts

The production boundary is `SewerPilot.sln`, containing only
`src/SewerPilot.OpenFlows.Contracts` and its executable verification project
`tests/SewerPilot.OpenFlows.Contracts.Tests`. Future Core, StormFlow, GravityFlow
and UI code can consume SewerPilot-owned identities, interfaces and evidence
without referencing Haestad, Bentley or WaterObjects concrete types.

## Framework bridge

Contracts target **netstandard2.0**, with conservative C# 7.3 syntax and only the
standard library. This is a deliberate compatibility bridge for a future .NET
Framework 4.8 adapter and modern .NET consumers, not a Bentley API requirement.
`Directory.Build.props` retains only its existing deterministic/CI settings.
No product-wide target framework or UI framework is selected here.

P1 established .NET Framework 4.8/x64 as the direction for the future primary
Bentley-facing adapter: the inspected runtime metadata and two actual core
assembly loads support that direction. It remains the starting point unless
later runtime evidence changes it. Other layouts, licensing, native engine
initialization and engineering operations remain unproven. Historical P1
evidence is unchanged.

The package-free test executable targets **net8.0**, supported by the installed
SDK/toolchain and runtime. It is a consumer of the same contracts; it does not
load the vendor runtime. The SDK's implicit NETStandard.Library reference package
is a framework prerequisite, not a vendor or test framework dependency. P2
restores from its existing local NuGet cache before the build; it adds no explicit
PackageReference or JSON/test framework package.

## Capabilities and evidence

`CapabilityState` is four-valued. Report values are explicit through
`CapabilityStateReport.ToReportValue`; persistence/JSON serialization belongs to
later phases.

| C# state | Report meaning |
|---|---|
| Supported | SUPPORTED: evidence supports this capability within its stated scope |
| NotSupported | NOT_SUPPORTED: explicitly unsupported |
| NotTested | NOT_TESTED: operation has not been exercised |
| NotEstablished | NOT_ESTABLISHED: evidence is insufficient or absent |

The enum's default is `NotEstablished`. A missing capability query produces an
explicit `NotEstablished` evidence entry rather than false support or an
unsupported verdict. Invalid enum values and contradictory negative evidence
kinds are rejected. A boolean could not preserve these distinctions.

`CapabilityIdentifier` is an immutable, ordinal, case-sensitive value with eleven
predefined identifiers and an open constructor for future additions. Consumers
can retain/query unfamiliar identifiers without requiring a closed enum update.
`WaterObjectsPresent` is the required historical capability label; it is not a
namespace, assembly reference or concrete type dependency.

Each `CapabilityEvidence` carries capability, state, evidence kind, reason and
provenance. Evidence kinds include StaticMetadata, AssemblyLoad,
RuntimeInitialization, ModelRead, RuntimeOperation, SolverExecution,
ExplicitUnsupported, NotTested and NotEstablished. An optional DateTimeOffset
records an observation when useful. Timestamp, host paths and runtime versions
are not part of capability identifier equality. Reasons/provenance are short
diagnostics; callers must supply nonsecret text.

`OpenFlowsCompatibilityProfile` defensively snapshots capability evidence and
limitations into read-only collections. Duplicate evidence for the same
identifier is rejected, avoiding ambiguous state selection. It carries optional
display name, version, build and architecture metadata. ProductDetected exposes
the four-valued state of its corresponding capability. Classification is supplied
explicitly as Established, Partial, InsufficientData or Unsupported; it is never
inferred from metadata or presence alone. An Established classification should
be interpreted within the caller's evidenced scope, not as all capabilities
being supported.

No code parses product versions, compares builds, selects adapters or blocks
unknown versions. Version-tolerance tests change inert version/build strings
while preserving the entire P1 capability matrix and classification.

## P1 evidence represented by the fake

| Capability | State | Evidence kind |
|---|---|---|
| ProductDetected | Supported | StaticMetadata |
| WaterObjectsPresent | Supported | StaticMetadata |
| ManagedAssembliesLoad | Supported | AssemblyLoad |
| ModelRead | NotTested | NotTested |
| ScenarioRead | NotTested | NotTested |
| ScenarioCreate | NotTested | NotTested |
| AlternativeRead | NotTested | NotTested |
| AlternativeCreate | NotTested | NotTested |
| SolverRun | NotTested | NotTested |
| ResultRead | NotTested | NotTested |
| ModelWrite | NotTested | NotTested |

The fake P1 profile uses Partial classification to describe the limited scope of
the evidence. This does not revise P1's successful compatibility-direction
acceptance. Product detection, API presence and two core loads provide no
authority to promote an unperformed operation to Supported or NotSupported.

## Small service boundaries

| Contract | Minimum responsibility / required capability |
|---|---|
| IOpenFlowsRuntime | Disposable runtime lifetime and capability provider |
| IOpenFlowsCapabilityProvider | Profile and capability evidence query |
| IOpenFlowsModel | Disposable model handle with typed identity |
| IOpenFlowsModelReader | Read a source into a handle / ModelRead |
| IOpenFlowsScenarioService | Read identities / ScenarioRead; create working-state identity / ScenarioCreate |
| IOpenFlowsAlternativeService | Read identities / AlternativeRead; create working-state identity / AlternativeCreate |
| IOpenFlowsCalculationRunner | Run a scenario and return run identity / SolverRun |
| IOpenFlowsResultReader | Read identities of elements with results / ResultRead |
| IOpenFlowsModelWriter | Write a controlled copy to an explicit destination / ModelWrite |

Model, scenario, alternative, element and calculation-run identities are small
immutable values. Equality requires both the identity kind and ordinal value;
identical text across kinds cannot alias. These values are adapter-assigned
opaque handles. Persistent identity, safe paths, hashes and baseline protection
remain P5 work. No hydraulic properties, design criteria, engineering units,
calculation options or result schema are invented in P2.

Read methods do not authorize mutation. Create methods identify working-state
mutation explicitly; they do not authorize persistence. The model/reader
interfaces expose no save method. Persistence is exclusively behind
IOpenFlowsModelWriter.WriteCopy with an explicit destination. Before real writes,
a later protection layer must validate a controlled copy, prevent baseline
overwrite and capture before/after hashes. Naming a destination is not itself
proof of safety, and P2 implements no actual writes.

Applications query evidence and invoke an operation only for Supported.
Implementations must also reject other states when called directly. C# interfaces
establish this obligation but cannot prove arbitrary implementations truthful;
future adapters require their own deterministic runtime acceptance. Policy such
as adapter selection or handling an unknown version belongs to later layers.

## Executable verification

`FakeOpenFlowsRuntime` and `FakeModel` exist only inside the test project. The fake
implements all service boundaries without discovery, licensing, file I/O or
assembly loading. It refuses all eight untested P1 operations. Separate in-memory
fixtures demonstrate each of the four states: only explicit Supported grants an
operation. A contract-only caller can inspect provenance/limitations and avoid
invoking an untested operation.

The harness verifies the exact P1 matrix, version/build nonauthority, future and
missing capabilities, evidence validation, immutable snapshots, duplicate
rejection, typed identities, operation gating and read/write/dependency
boundaries. It reports the number of test cases, executed assertions and failures,
and returns nonzero on failure. No external test framework is needed.

`tools/test.ps1` continues generic discovery under tests/. A discovered project
with `SewerPilotTestHarness=true` runs as a prebuilt executable; other test
projects retain the existing dotnet test path. Output and failure exit codes are
propagated. Build and test entry points remain restore-free; see build/README.md
for the explicit restore prerequisite.

`tools/verify.ps1` keeps the historical checks and adds configured solution,
project-reference, contracts framework, vendor-reference, tracked binary,
secret-like material and obvious version/build branching checks. These static
checks supplement executable assertions; they do not establish runtime support.

## Deliberate exclusions and P3 boundary

P2 implements no compatibility/adapter production project, runtime discovery,
registry inspection, vendor assembly loading/binding, reflection adapter,
licensing, native initialization, model opening, solver execution or model
mutation. No Core, StormFlow, GravityFlow, Desktop, Reporting or Standards project
is created. There is no persistence/serialization implementation.

P3 is ready to consume this boundary and must separately establish runtime
discovery + assembly binding + adapter selection, **without model mutation**.
P1's NotTested capabilities remain explicit until their own later evidence
supports a change. P3 is not started by P2 acceptance.
