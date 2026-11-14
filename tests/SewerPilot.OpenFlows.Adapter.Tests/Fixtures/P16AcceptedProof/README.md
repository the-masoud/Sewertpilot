These are public, privacy-redacted copies of the retained request/result from accepted P16 run
`da674540abae4a40a08d0efb0a7f703e`, referenced by
`docs/evidence/p16/headless-result-provenance/final-report.md`.

They contain actual vendor observations on an authorized disposable model, not invented
hydraulic values. The request's `TEST_ONLY_RESULT_PROOF` describes the disposable technical
proof scope. It is preserved in the immutable receipt's ObservationScope; it does not grant
engineering approval. Synthetic TEST_ONLY result sets and fixture-source classifications
remain rejected by the production trust entry point.

Tests replay the internal completion factory with the recorded inventory. They do not
execute the runtime or open any model. Production issues this receipt only after its owned
P16 process exits successfully, protocol validation passes and current file hashes match.
Public JSON deserialization cannot mint a receipt. No archive-import interface is added.

The minimal mapping contains only the observed Flow metric. Other metrics remain absent;
this change does not reinterpret the accepted secondary metrics or numeric-encode Surcharge.
The application lineage labels in the tests are test identifiers, not engineering decisions.

Public-export change (2026-11-14): personal profile names in recorded paths are replaced with `Public`. Numeric observations, run identifiers and recorded inventories are unchanged. These JSON files are replay fixtures, not byte-identical original acceptance receipts and not executable engineering inputs. The original private receipts remain unchanged.
