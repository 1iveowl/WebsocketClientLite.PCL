# PDD verification receipt

Machine evidence gate: **passed**

Machine obligations, controlled ordering and boundary experiments, recorded Stryker dispositions and existing CI gates; PR review remains separate and unsatisfied.

Tested commit: `e752ffcd62000e51264ec81771ebf1a9014f8b0e`
PR source head: ``

6 library builds use the actual hosts recorded below; they are not 6 independently tested runtimes.

- netstandard2.0: passed; host: .NET 11.0.0
- netstandard2.1: passed; host: .NET 11.0.0
- net8.0: passed; host: .NET 11.0.0
- net9.0: passed; host: .NET 11.0.0
- net10.0: passed; host: .NET 11.0.0
- net11.0: passed; host: .NET 11.0.0

| Obligation | Status |
| --- | --- |
| CLOSE-001-PROOF | passed [protocol/proof.log](protocol/proof.log) |
| CLOSE-001-TEST | passed [tests/tests/netstandard2.0/results.trx](tests/tests/netstandard2.0/results.trx), [tests/tests/netstandard2.1/results.trx](tests/tests/netstandard2.1/results.trx), [tests/tests/net8.0/results.trx](tests/tests/net8.0/results.trx), [tests/tests/net9.0/results.trx](tests/tests/net9.0/results.trx), [tests/tests/net10.0/results.trx](tests/tests/net10.0/results.trx), [tests/tests/net11.0/results.trx](tests/tests/net11.0/results.trx) |
| CLOSE-001-MUTATION | passed [protocol/mutations/results.json](protocol/mutations/results.json) |
| CLOSE-002-PROOF | passed [protocol/proof.log](protocol/proof.log) |
| CLOSE-002-TEST | passed [tests/tests/netstandard2.0/results.trx](tests/tests/netstandard2.0/results.trx), [tests/tests/netstandard2.1/results.trx](tests/tests/netstandard2.1/results.trx), [tests/tests/net8.0/results.trx](tests/tests/net8.0/results.trx), [tests/tests/net9.0/results.trx](tests/tests/net9.0/results.trx), [tests/tests/net10.0/results.trx](tests/tests/net10.0/results.trx), [tests/tests/net11.0/results.trx](tests/tests/net11.0/results.trx) |
| CLOSE-002-MUTATION | passed [protocol/mutations/results.json](protocol/mutations/results.json) |
| CLOSE-003-PROOF | passed [protocol/proof.log](protocol/proof.log) |
| CLOSE-003-TEST | passed [tests/tests/netstandard2.0/results.trx](tests/tests/netstandard2.0/results.trx), [tests/tests/netstandard2.1/results.trx](tests/tests/netstandard2.1/results.trx), [tests/tests/net8.0/results.trx](tests/tests/net8.0/results.trx), [tests/tests/net9.0/results.trx](tests/tests/net9.0/results.trx), [tests/tests/net10.0/results.trx](tests/tests/net10.0/results.trx), [tests/tests/net11.0/results.trx](tests/tests/net11.0/results.trx) |
| CLOSE-003-MUTATION | passed [protocol/mutations/results.json](protocol/mutations/results.json) |
| CLOSE-004-TEST | passed [tests/tests/netstandard2.0/results.trx](tests/tests/netstandard2.0/results.trx), [tests/tests/netstandard2.1/results.trx](tests/tests/netstandard2.1/results.trx), [tests/tests/net8.0/results.trx](tests/tests/net8.0/results.trx), [tests/tests/net9.0/results.trx](tests/tests/net9.0/results.trx), [tests/tests/net10.0/results.trx](tests/tests/net10.0/results.trx), [tests/tests/net11.0/results.trx](tests/tests/net11.0/results.trx) |
| CLOSE-004-CONTROLLED | passed [tests/tests/netstandard2.0/results.trx](tests/tests/netstandard2.0/results.trx), [tests/tests/netstandard2.1/results.trx](tests/tests/netstandard2.1/results.trx), [tests/tests/net8.0/results.trx](tests/tests/net8.0/results.trx), [tests/tests/net9.0/results.trx](tests/tests/net9.0/results.trx), [tests/tests/net10.0/results.trx](tests/tests/net10.0/results.trx), [tests/tests/net11.0/results.trx](tests/tests/net11.0/results.trx), [controlled/defect/results.json](controlled/defect/results.json), [controlled/boundaries/results.json](controlled/boundaries/results.json) |
| CLOSE-005-TEST | passed [tests/tests/netstandard2.0/results.trx](tests/tests/netstandard2.0/results.trx), [tests/tests/netstandard2.1/results.trx](tests/tests/netstandard2.1/results.trx), [tests/tests/net8.0/results.trx](tests/tests/net8.0/results.trx), [tests/tests/net9.0/results.trx](tests/tests/net9.0/results.trx), [tests/tests/net10.0/results.trx](tests/tests/net10.0/results.trx), [tests/tests/net11.0/results.trx](tests/tests/net11.0/results.trx) |
| CLOSE-005-CONTROLLED | passed [tests/tests/netstandard2.0/results.trx](tests/tests/netstandard2.0/results.trx), [tests/tests/netstandard2.1/results.trx](tests/tests/netstandard2.1/results.trx), [tests/tests/net8.0/results.trx](tests/tests/net8.0/results.trx), [tests/tests/net9.0/results.trx](tests/tests/net9.0/results.trx), [tests/tests/net10.0/results.trx](tests/tests/net10.0/results.trx), [tests/tests/net11.0/results.trx](tests/tests/net11.0/results.trx), [controlled/boundaries/results.json](controlled/boundaries/results.json) |
| REASSEMBLY-001-PROOF | passed [protocol/proof.log](protocol/proof.log) |
| REASSEMBLY-001-TEST | passed [tests/tests/netstandard2.0/results.trx](tests/tests/netstandard2.0/results.trx), [tests/tests/netstandard2.1/results.trx](tests/tests/netstandard2.1/results.trx), [tests/tests/net8.0/results.trx](tests/tests/net8.0/results.trx), [tests/tests/net9.0/results.trx](tests/tests/net9.0/results.trx), [tests/tests/net10.0/results.trx](tests/tests/net10.0/results.trx), [tests/tests/net11.0/results.trx](tests/tests/net11.0/results.trx), [controlled/retention/results.json](controlled/retention/results.json) |
| REASSEMBLY-001-MUTATION | passed [protocol/mutations/results.json](protocol/mutations/results.json) |
| REASSEMBLY-002-PROOF | passed [protocol/proof.log](protocol/proof.log) |
| REASSEMBLY-002-TEST | passed [tests/tests/netstandard2.0/results.trx](tests/tests/netstandard2.0/results.trx), [tests/tests/netstandard2.1/results.trx](tests/tests/netstandard2.1/results.trx), [tests/tests/net8.0/results.trx](tests/tests/net8.0/results.trx), [tests/tests/net9.0/results.trx](tests/tests/net9.0/results.trx), [tests/tests/net10.0/results.trx](tests/tests/net10.0/results.trx), [tests/tests/net11.0/results.trx](tests/tests/net11.0/results.trx) |
| REASSEMBLY-002-MUTATION | passed [protocol/mutations/results.json](protocol/mutations/results.json) |
| REASSEMBLY-003-TEST | passed [tests/tests/netstandard2.0/results.trx](tests/tests/netstandard2.0/results.trx), [tests/tests/netstandard2.1/results.trx](tests/tests/netstandard2.1/results.trx), [tests/tests/net8.0/results.trx](tests/tests/net8.0/results.trx), [tests/tests/net9.0/results.trx](tests/tests/net9.0/results.trx), [tests/tests/net10.0/results.trx](tests/tests/net10.0/results.trx), [tests/tests/net11.0/results.trx](tests/tests/net11.0/results.trx) |
| MASK-001-PROOF | passed [protocol/proof.log](protocol/proof.log) |
| MASK-001-TEST | passed [tests/tests/netstandard2.0/results.trx](tests/tests/netstandard2.0/results.trx), [tests/tests/netstandard2.1/results.trx](tests/tests/netstandard2.1/results.trx), [tests/tests/net8.0/results.trx](tests/tests/net8.0/results.trx), [tests/tests/net9.0/results.trx](tests/tests/net9.0/results.trx), [tests/tests/net10.0/results.trx](tests/tests/net10.0/results.trx), [tests/tests/net11.0/results.trx](tests/tests/net11.0/results.trx) |
| MASK-001-MUTATION | passed [protocol/mutations/results.json](protocol/mutations/results.json) |
| MASK-002-PROOF | passed [protocol/proof.log](protocol/proof.log) |
| MASK-002-TEST | passed [tests/tests/netstandard2.0/results.trx](tests/tests/netstandard2.0/results.trx), [tests/tests/netstandard2.1/results.trx](tests/tests/netstandard2.1/results.trx), [tests/tests/net8.0/results.trx](tests/tests/net8.0/results.trx), [tests/tests/net9.0/results.trx](tests/tests/net9.0/results.trx), [tests/tests/net10.0/results.trx](tests/tests/net10.0/results.trx), [tests/tests/net11.0/results.trx](tests/tests/net11.0/results.trx) |
| MASK-002-MUTATION | passed [protocol/mutations/results.json](protocol/mutations/results.json) |
| MASK-003-PROOF | passed [protocol/proof.log](protocol/proof.log) |
| MASK-003-TEST | passed [tests/tests/netstandard2.0/results.trx](tests/tests/netstandard2.0/results.trx), [tests/tests/netstandard2.1/results.trx](tests/tests/netstandard2.1/results.trx), [tests/tests/net8.0/results.trx](tests/tests/net8.0/results.trx), [tests/tests/net9.0/results.trx](tests/tests/net9.0/results.trx), [tests/tests/net10.0/results.trx](tests/tests/net10.0/results.trx), [tests/tests/net11.0/results.trx](tests/tests/net11.0/results.trx) |
| MASK-003-MUTATION | passed [protocol/mutations/results.json](protocol/mutations/results.json) |
| WRITE-001-TEST | passed [tests/tests/netstandard2.0/results.trx](tests/tests/netstandard2.0/results.trx), [tests/tests/netstandard2.1/results.trx](tests/tests/netstandard2.1/results.trx), [tests/tests/net8.0/results.trx](tests/tests/net8.0/results.trx), [tests/tests/net9.0/results.trx](tests/tests/net9.0/results.trx), [tests/tests/net10.0/results.trx](tests/tests/net10.0/results.trx), [tests/tests/net11.0/results.trx](tests/tests/net11.0/results.trx), [controlled/queued/results.json](controlled/queued/results.json) |
| LEASE-001-TEST | passed [tests/tests/netstandard2.0/results.trx](tests/tests/netstandard2.0/results.trx), [tests/tests/netstandard2.1/results.trx](tests/tests/netstandard2.1/results.trx), [tests/tests/net8.0/results.trx](tests/tests/net8.0/results.trx), [tests/tests/net9.0/results.trx](tests/tests/net9.0/results.trx), [tests/tests/net10.0/results.trx](tests/tests/net10.0/results.trx), [tests/tests/net11.0/results.trx](tests/tests/net11.0/results.trx), [controlled/leases/results.json](controlled/leases/results.json) |
| EPOCH-001-TEST | passed [tests/tests/netstandard2.0/results.trx](tests/tests/netstandard2.0/results.trx), [tests/tests/netstandard2.1/results.trx](tests/tests/netstandard2.1/results.trx), [tests/tests/net8.0/results.trx](tests/tests/net8.0/results.trx), [tests/tests/net9.0/results.trx](tests/tests/net9.0/results.trx), [tests/tests/net10.0/results.trx](tests/tests/net10.0/results.trx), [tests/tests/net11.0/results.trx](tests/tests/net11.0/results.trx), [controlled/reconnect/results.json](controlled/reconnect/results.json) |
| HEADER-001-PROOF | passed [protocol/proof.log](protocol/proof.log) |
| HEADER-001-TEST | passed [tests/tests/netstandard2.0/results.trx](tests/tests/netstandard2.0/results.trx), [tests/tests/netstandard2.1/results.trx](tests/tests/netstandard2.1/results.trx), [tests/tests/net8.0/results.trx](tests/tests/net8.0/results.trx), [tests/tests/net9.0/results.trx](tests/tests/net9.0/results.trx), [tests/tests/net10.0/results.trx](tests/tests/net10.0/results.trx), [tests/tests/net11.0/results.trx](tests/tests/net11.0/results.trx) |
| HEADER-001-MUTATION | passed [protocol/mutations/results.json](protocol/mutations/results.json) |
| HEADER-002-PROOF | passed [protocol/proof.log](protocol/proof.log) |
| HEADER-002-TEST | passed [tests/tests/netstandard2.0/results.trx](tests/tests/netstandard2.0/results.trx), [tests/tests/netstandard2.1/results.trx](tests/tests/netstandard2.1/results.trx), [tests/tests/net8.0/results.trx](tests/tests/net8.0/results.trx), [tests/tests/net9.0/results.trx](tests/tests/net9.0/results.trx), [tests/tests/net10.0/results.trx](tests/tests/net10.0/results.trx), [tests/tests/net11.0/results.trx](tests/tests/net11.0/results.trx) |

Review evidence is separate and not-run; no requirement is claimed satisfied without it.
Stryker: passed; controlled ordering experiment: passed; boundary experiments: passed.

Receipts are traceability records, not signed attestations.

