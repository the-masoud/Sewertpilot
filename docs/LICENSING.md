# Licensing and redistribution boundaries

## Original project work

The maintainer authorized Apache License 2.0 publication of independently owned SewerPilot code for this readiness task. [LICENSE](../LICENSE) contains the complete, unmodified text from the [Apache Software Foundation](https://www.apache.org/licenses/LICENSE-2.0.txt).

The grant applies to original SewerPilot source, test code, build scripts, configuration, authored documentation and the project's own screenshot. It does not assert exclusive ownership by a named individual, transfer another contributor's rights or relicense commercial products. Contributions must be made by their rights holders or authorized contributors.

The bounded source audit inspected the tracked public tree, source/project references, generated records and restored package metadata. No separately attributed embedded third-party source or vendor assemblies were identified in the tracked source export. This is an audit of available evidence, not a reconstructed authorship history; the original private history was not imported. No conflicting file-level notice was removed, and no upstream NOTICE file was identified in the original tracked source. A project NOTICE was therefore not invented.

## File categories

| Material | Treatment |
| --- | --- |
| Original C# code under `src/` and `tests/`, project files, scripts and authored docs | Apache-2.0, subject to any applicable retained file-level notices |
| Runtime watchdog | Original project Python controller using Python standard-library/Windows APIs; copied byte-for-byte into the public source build |
| `packages.lock.json` and generated fixture envelopes | Project metadata/records; package contents and underlying vendor products keep their own licenses |
| P16 replay JSON | Recorded vendor tutorial observations with personal paths redacted; no grant to Bentley software, models, brands or underlying third-party content is implied |
| NuGet packages | Restored from their publishers, not checked in or relicensed as SewerPilot code |
| Bentley/OpenFlows and Autodesk interfaces | Original integration code may be Apache-2.0; referenced commercial assemblies, model files, SDKs, licenses and trademarks remain outside that grant |

No customer engineering model, proprietary SDK, commercial license file, private engineering document or installer is transferred into this change. Existing source/test references to bounded local workflows are not distribution of the referenced models or private acceptance archives. Personal profile names in existing public paths were redacted; that does not erase earlier Git history or imply that historical captures were republished.

## Restored dependencies

The focused public build restored **52 package identities** into an isolated package directory. Metadata declared MIT for 48 and Apache-2.0 for `ModelContextProtocol` / `ModelContextProtocol.Core` 2.2.0. Two legacy packages had license URLs instead of SPDX expressions:

- `NETStandard.Library` 2.0.3 includes `LICENSE.TXT` (MIT) and `THIRD-PARTY-NOTICES.TXT`.
- `Microsoft.NETCore.Platforms` 1.1.0 includes `dotnet_library_license.txt` (Microsoft .NET Library terms) and `ThirdPartyNotices.txt`.

Do not assume that every restored package is Apache-2.0 or that a license expression is a complete third-party inventory. Preserve each package's actual license and bundled notices when redistributing its binaries; review the corresponding package distribution before making a public binary release. This mission publishes source and references, not the restored packages or a binary bundle. Existing package lock files retain their original identities/content hashes. No vulnerability-clearance claim is made by this licensing check.

## Commercial dependencies

Bentley OpenFlows Sewer / SewerGEMS is separately installed and licensed for supported model intake and native calculations. Autodesk assemblies are separately installed and licensed for the optional Civil 3D integration. No Bentley/Autodesk assembly or source code is relicensed or distributed here. Public build success does not establish runtime compatibility, engineering certification, vendor endorsement or permission to redistribute vendor components.
