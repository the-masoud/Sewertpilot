# P4 read-only model access

The Adapter targets net48/x64 and references the vendor-neutral Contracts and
the P3 Compatibility library. Its three responsibilities are the model reader,
an internal one-shot process client, and a working-copy integrity helper.
Construction does not discover or load a vendor runtime. `ReadWithEvidence`
performs the explicit read and returns a detached SewerPilot-owned observation,
diagnostics, fingerprints and capability profile. Existing Contracts are unchanged.
The inherited contract reader remains gated on established ModelRead evidence;
`ReadWithEvidence` supplies the explicit bootstrap operation.

## Runtime and lifecycle

P3 discovery, candidate selection, core binding and profile construction are
reused. No compatible candidate fails deterministically; equally valid candidates
are ambiguous. Product version/build are diagnostic metadata and do not rank
runtimes. There are no compile-time Bentley references or copied vendor binaries.

The lifecycle runs on one STA thread in the default AppDomain of the dedicated net48/x64 production RuntimeHost process. Parent metadata inspection reuses the P3 reflection-only worker in its default domain; core binding reuses the P3 worker inside the child default domain. No secondary AppDomain is created or unloaded on the P4 production path. Dynamic binding
uses only the selected runtime root, rejects filesystem links and checks loaded
vendor assembly locations. Managed assemblies actually loaded from that root
are recorded with SHA-256. The resolver does not search arbitrary machine paths.
Native dependency lookup temporarily uses the selected root; the prior directory
settings are restored after the lifecycle. Calls are serialized within this host
process because those directory settings are process-wide.

The R4/R6-proven sequence is:

1. `ProductizationLibrary.NewSewerGEMSApplicationDescription` using the executing
   Adapter assembly version, the named `Standalone` enum member, and that same
   assembly version as a string. The enum is only a required vendor argument;
   licensing work is outside P4.
2. `StormSewerProductApplicationModel`, `StormSewerProductParentFormModel`, then
   `Haestad.Delaware.Application.DelawareParentFormUIModel(parent, true)`.
3. `InitializeOnStartUp`, followed by `SetCOMUserInterface` for the application,
   parent and UI model. A hidden internal WinForms owner is never shown.
4. Exactly one `OpenFileWithSpecifiedExtension(workingCopyPath, false)`.
5. `CurrentGraphicalProject`, use as `IDomainProject`, and its `DomainDataSet`.
6. Resolve `ConduitElementManager` by enum name; invoke `DomainElementManager`.
   Resolve `ElementIDs()` through the concrete/base/interface hierarchy and
   invoke it once. Resolve the most-specific public `Item[Int32]` getter and read
   index zero once. The observed datum must be an Int32. An empty collection
   produces `NoReadableConduitElement`; there is no other element fallback.
7. In cleanup, `MakeClean`, `CloseCurrentProject(new ProjectProperties())`,
   `CloseParentFormUIModel`, and reverse-order disposal. Each applicable cleanup
   action is attempted even after a read failure. The STA is joined before result persistence.

The proven runtime manager is `DelawareConduitElementManager`, with `ElementIDs`
declared by `ModelingElementManagerBase` and returning `HmIDCollection`.
Compatibility depends on the callable members, not the concrete manager name.
The recorded enum value is diagnostic only. Datum 27 is prior sample evidence;
it is not an Adapter requirement. Only the acceptance runner checks that value
when the source .stsw hash equals the exact R6 sample hash.

## Source protection and evidence

For `X.stsw`, only the exact existing files `X.stsw`, `X.stsw.sqlite` and
`X.stsw.dwh` are considered. The source directory is never enumerated. Each
source is fingerprinted before copying to
`%TEMP%/SewerPilot/OpenFlows/ReadOnly/<unique-id>/`; each copy is fingerprinted
before opening. Only the copied .stsw is passed to the vendor open method.

After the parent has observed RuntimeHost termination, the same pre-existing source and copied
files are fingerprinted again. Any mismatch prevents acceptance. Newly created
transient files and removed entries in the isolated folder are recorded separately and left intact. Unexpected entries prevent acceptance.
Working copies are retained with the evidence; P4 does not implement retention
policy or persistent project storage.

P4 does **not** claim a formal vendor read-only file mode. Read safety is
demonstrated by an isolated working copy, zero save/write calls, and byte-identical
before/after SHA-256 verification. No save, scenario/alternative creation, solver,
results or model-value setter is implemented, including dormant helpers.

ModelRead becomes Supported only when selection, binding, initialization, open,
project, dataset, Int32 datum, complete close, both integrity comparisons and
zero write calls, valid durable result, completed STA, exit code zero and termination without watchdog all succeed without a recorded failure. The first three runtime
capabilities retain P3 evidence. All seven other operational capabilities remain
NotTested. Owned result/evidence objects contain no live vendor session, vendor
type or WinForms type in public signatures.

## Acceptance and limits

The package-free synthetic harness verifies capability gates, selection semantics,
public signatures, hash protection, named enum resolution, inherited read-member
resolution, one-index access, cleanup and the executable zero-write boundary.
It does not require an installed vendor runtime. The thin acceptance executable
calls only the production reader and serializes its returned evidence. Earlier
R2-R6 evidence is retained inside `prior_p4_evidence`; historical R3/R5 diagnostic
scripts remain preserved artifacts and are not the production acceptance path.

This bounded phase proves one explicit conduit ID read on the accepted lesson
family. It does not establish general model enumeration, scenario/alternative
access, calculations, result access or writes. Compatibility across other vendor
installations requires independent runtime evidence. The returned opaque identity
identifies an observation, not a deterministic persistent model identity.

P5's boundary is baseline protection, deterministic model identity, controlled
working-copy policy and an explicit write-safety boundary. None is implemented
as part of P4.

## R8 localization: P4 remains open

The one supervised R8 diagnostic captured the genuine Int32 conduit ID 27 and
returned from vendor close/disposal and STA completion. The subsequent
`AppDomain.Unload` entered at journal stage 151 and did not return. The external
watchdog terminated only its worker at the fixed 120-second deadline. All three
source-family and pre-existing copied files matched their before hashes after
termination; no transients were found. This is diagnostic integrity evidence,
not completed production acceptance or a ModelRead promotion.

The flushed journal records UTC, monotonic sequence, process/thread/AppDomain,
apartment and message-loop context. The read and vendor close calls returned with
`Application.MessageLoop` false. The prior R6 host also had no `Application.Run`
or `DoEvents`; it exited as a process rather than unloading a vendor AppDomain.
These observations identify the lifetime boundary but do not establish the cause
inside CLR/vendor unloading or justify a message-pump fix. No correction was
applied. Original P4 evidence is preserved, Atria was not invoked, and no phase
commit or P5 implementation was performed. See `docs/evidence/p4/r8_localization.json`.

## R9 one-shot process protocol

The parent writes a validated neutral request to a unique controlled IPC directory
and starts the production RuntimeHost exactly once. It waits at most 120 seconds;
if exceeded, it kills only the Process handle it started and waits a bounded five
seconds for termination acknowledgement. A watchdog exit is never acceptance.
The child writes result.json.tmp, flushes its file to disk, closes it and atomically
moves it to result.json in the same directory. It flushes ResultCommitted to the
stage journal. Only after the vendor cleanup, STA join and every owned finally /
using scope have finished does Main record ControlledExitRequested and call
Environment.Exit(0). No required cleanup follows that call.

The parent consumes a complete schema/nonce/core-inventory validated result only
after process exit, requires the durable commit marker from its child PID, and
performs final source and working-copy hashing itself. Exit code zero alone is
insufficient. IPC DTOs are internal and contain only neutral values. No retries,
message pump, arbitrary sleeps, alternate vendor lifecycle or write APIs exist.
R8 evidence remains immutable; R9 acceptance is recorded separately.
## R9 deterministic closeout: P4 remains open

The first affected build failed because RuntimeHost lacked the framework JSON
serializer reference. The single permitted correction added it and adjusted the
verifier. The corrected build compiled Adapter, RuntimeHost and the thin acceptance
runner, but the affected test harness also lacked System.Web.Extensions and failed
with five CS0012 diagnostics. The correction budget is exhausted. No further
implementation correction, focused test execution, live model operation or commit
was performed. The verifier passed once. R2-R8 evidence hashes remain unchanged.
See r9_deterministic_validation.json for the exact bounded build blocker.