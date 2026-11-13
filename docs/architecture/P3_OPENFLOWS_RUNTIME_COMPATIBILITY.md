# P3 — OpenFlows runtime compatibility boundary

P3 adds one net48/x64 production project,
`SewerPilot.OpenFlows.Compatibility`, over the netstandard2.0 contracts. The
framework/platform choice follows P1's actual bounded evidence: a net48-compatible
x64 OpenFlows executable and successful loads of the two core managed assemblies.
It does not determine frameworks for the rest of SewerPilot.

## Discovery and candidates

`OpenFlowsRuntimeDiscovery` accepts immutable discovery bounds and injected
location/inspection interfaces. The production Windows source checks:

- a caller supplied `SEWERPILOT_OPENFLOWS_HOME` value passed by the acceptance
  runner as an explicit override;
- product display/install metadata under the current user's and local machine's
  32-bit and 64-bit uninstall registry views;
- bounded Bentley vendor registry locations, reading whitelisted path values
  while skipping keys whose names indicate license, activation, credentials,
  tokens or secrets;
- platform-derived Program Files and Program Files (x86) Bentley vendor roots.

Each root search has caller-visible maximum depth and directory count. Vendor
registry depth and entry count also have bounds. Paths are canonicalized, and
filesystem links are rejected or skipped. There is no drive-wide crawl, user-home
recursion, network access, installer invocation or whole-system product query.
Discovery reads executable identity and static metadata for the product
executable and the two required core assemblies within the product root. It stops
descending after finding that product executable, avoiding its lesson/model trees.
Errors and incomplete coverage are reported as concise diagnostics.

Candidates contain a stable opaque ID derived from the normalized root, product
metadata, architecture, discovery source, bounded assembly inventory and
inspection errors. Candidate IDs contain no observed version. Discovery results
are sorted and roots are deduplicated independently of which source found them.
Only a SewerGEMS product identity with net48/x64 executable metadata and exactly
one net48-compatible core assembly of each required simple name is statically
usable. Static usability is not a successful assembly-load claim.

## Version tolerance and selection

Product version, file version, assembly versions, build, path and framework
metadata are observations for diagnostics. They are not compatibility predicates
and do not rank candidates. Highest version, newest build and newest year are
never automatic selection rules.

With complete discovery, zero statically usable candidates yields `NotFound`, a
single candidate yields `Selected`, and multiple usable candidates yield
`Ambiguous` with no implicit choice. Incomplete automatic discovery reports
`IncompleteDiscovery` rather than claiming uniqueness. An explicit override
limits inspection/selection to its normalized root. Invalid syntax, no matching
usable candidate or incomplete override inspection yields `InvalidOverride`; it
does not silently fall back to another install. An override is a request, not
proof: executable identity, core inventory and successful load are still checked.

`TraditionalWaterObjectsNetFx` is selected only when static requirements and
hash-matched loads of both core assemblies are evidenced. All other outcomes
select `None`. There are no year-specific adapter classes or version/build
switches.

## Static metadata and dynamic assembly binding

Metadata inspection runs in a temporary .NET Framework AppDomain and uses
reflection-only assembly loading. It records simple name, absolute discovered
path, SHA-256, assembly/file/product versions, CLR image runtime, target framework,
processor architecture and CorFlags without running a vendor module initializer
or API method. The temporary metadata domain is unloaded on success and failure.

The binder requires one selected candidate, an x64 CLR 4 process, statically usable
metadata, direct files under the selected product root, intact paths and hashes,
and assembly simple names matching the required identities. It opens a temporary
AppDomain, loads only `Haestad.Support` and `Haestad.Domain` from their inspected
paths, checks their actual loaded identities, paths and hashes, and rejects any
additional loaded assembly in those vendor families. Its temporary
`AssemblyResolve` handler can resolve only those two prevalidated core identities
from the chosen root. It returns no fallback path and is detached in `finally`;
the binding AppDomain is unloaded after evidence is returned. No binaries are
copied to application output.

This boundary proves a managed core load in an isolated CLR domain. Vendor-internal
native loader effects and licensed application initialization are not independently
excluded. Runtime success does not imply a usable license or full product session.
No license API, model/session API, scenario, alternative, solver, result or write
API is invoked.

## Capability evidence

The profile factory maps executable product identity to `ProductDetected` with
StaticMetadata evidence and the required core assembly inventory to
`WaterObjectsPresent` with StaticMetadata evidence. Only a verified two-assembly
load maps `ManagedAssembliesLoad` to Supported with AssemblyLoad evidence. If
loading fails, that capability remains NotEstablished. Every operational
capability remains NotTested: ModelRead, ScenarioRead, ScenarioCreate,
AlternativeRead, AlternativeCreate, SolverRun, ResultRead and ModelWrite.
Product/assembly presence does not establish an engineering operation. A partial
profile classification records the limited scope explicitly.

## Synthetic tests and machine acceptance

The package-free net48/x64 harness injects synthetic location sources, candidate
inspectors and metadata. It tests NotFound, Selected, Ambiguous, override success
and rejection, version non-ranking and inert metadata, incomplete core inventory,
identity mismatch, capability matrix preservation, project reference boundaries
and generic-root discovery. It creates no files and does not require the installed
product.

`tools/probes/OpenFlowsRuntimeBindingAcceptance` is a diagnostic executable, not
a product project. Run it once after the Release build. It reads the optional
process override, performs production discovery, selects or reports explicit
ambiguity, binds the selected two core assemblies at most once, constructs the
profile and writes `docs/evidence/p3/runtime_acceptance.json`. It refuses to
overwrite prior evidence. Evidence includes candidates and source names, selected
product/version/path metadata, required assembly identities and hashes, load
outcome, adapter family, capability states and model-access status. No sample
model path is inspected, opened or hashed.

## P4 boundary

P4 may add read-only access to the safe Bentley tutorial sample through the
selected runtime, after a dedicated bounded acceptance plan checks the sample
bundle before and after access. P3 performs no model access and provides no model
reader adapter. P3 is not authority to mutate a model or to infer any untested
capability.
