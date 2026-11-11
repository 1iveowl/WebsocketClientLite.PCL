# PDD verification receipt

> Note, 2026-11-11: this is a copy of `pdd/summary.md` from `capture-55ad2f7.zip`, which keeps the generated file unchanged. The evidence paths below are inside that archive, under `pdd/`, so the generated links were turned into plain paths. Nothing else differs.

Machine evidence gate: **passed**

Machine obligations, controlled ordering and boundary experiments, recorded Stryker dispositions and existing CI gates; PR review remains separate and unsatisfied.

Tested commit: `55ad2f7b51c1f16f17a929a2e041517b8ec981b9`
PR source head: ``

Five library builds use the actual hosts recorded below; they are not five independently tested runtimes.

- netstandard2.0: passed; host: .NET 10.0.14
- netstandard2.1: passed; host: .NET 10.0.14
- net8.0: passed; host: .NET 10.0.14
- net9.0: passed; host: .NET 10.0.14
- net10.0: passed; host: .NET 10.0.14

| Obligation | Status |
| --- | --- |
| CLOSE-001-PROOF | passed `pdd/protocol/proof.log` |
| CLOSE-001-TEST | passed `pdd/tests/tests/netstandard2.0/results.trx`, `pdd/tests/tests/netstandard2.1/results.trx`, `pdd/tests/tests/net8.0/results.trx`, `pdd/tests/tests/net9.0/results.trx`, `pdd/tests/tests/net10.0/results.trx` |
| CLOSE-001-MUTATION | passed `pdd/protocol/mutations/results.json` |
| CLOSE-002-PROOF | passed `pdd/protocol/proof.log` |
| CLOSE-002-TEST | passed `pdd/tests/tests/netstandard2.0/results.trx`, `pdd/tests/tests/netstandard2.1/results.trx`, `pdd/tests/tests/net8.0/results.trx`, `pdd/tests/tests/net9.0/results.trx`, `pdd/tests/tests/net10.0/results.trx` |
| CLOSE-002-MUTATION | passed `pdd/protocol/mutations/results.json` |
| CLOSE-003-PROOF | passed `pdd/protocol/proof.log` |
| CLOSE-003-TEST | passed `pdd/tests/tests/netstandard2.0/results.trx`, `pdd/tests/tests/netstandard2.1/results.trx`, `pdd/tests/tests/net8.0/results.trx`, `pdd/tests/tests/net9.0/results.trx`, `pdd/tests/tests/net10.0/results.trx` |
| CLOSE-003-MUTATION | passed `pdd/protocol/mutations/results.json` |
| CLOSE-004-TEST | passed `pdd/tests/tests/netstandard2.0/results.trx`, `pdd/tests/tests/netstandard2.1/results.trx`, `pdd/tests/tests/net8.0/results.trx`, `pdd/tests/tests/net9.0/results.trx`, `pdd/tests/tests/net10.0/results.trx` |
| CLOSE-004-CONTROLLED | passed `pdd/tests/tests/netstandard2.0/results.trx`, `pdd/tests/tests/netstandard2.1/results.trx`, `pdd/tests/tests/net8.0/results.trx`, `pdd/tests/tests/net9.0/results.trx`, `pdd/tests/tests/net10.0/results.trx`, `pdd/controlled/defect/results.json`, `pdd/controlled/boundaries/results.json` |
| CLOSE-005-TEST | passed `pdd/tests/tests/netstandard2.0/results.trx`, `pdd/tests/tests/netstandard2.1/results.trx`, `pdd/tests/tests/net8.0/results.trx`, `pdd/tests/tests/net9.0/results.trx`, `pdd/tests/tests/net10.0/results.trx` |
| CLOSE-005-CONTROLLED | passed `pdd/tests/tests/netstandard2.0/results.trx`, `pdd/tests/tests/netstandard2.1/results.trx`, `pdd/tests/tests/net8.0/results.trx`, `pdd/tests/tests/net9.0/results.trx`, `pdd/tests/tests/net10.0/results.trx`, `pdd/controlled/boundaries/results.json` |
| REASSEMBLY-001-PROOF | passed `pdd/protocol/proof.log` |
| REASSEMBLY-001-TEST | passed `pdd/tests/tests/netstandard2.0/results.trx`, `pdd/tests/tests/netstandard2.1/results.trx`, `pdd/tests/tests/net8.0/results.trx`, `pdd/tests/tests/net9.0/results.trx`, `pdd/tests/tests/net10.0/results.trx`, `pdd/controlled/retention/results.json` |
| REASSEMBLY-001-MUTATION | passed `pdd/protocol/mutations/results.json` |
| REASSEMBLY-002-PROOF | passed `pdd/protocol/proof.log` |
| REASSEMBLY-002-TEST | passed `pdd/tests/tests/netstandard2.0/results.trx`, `pdd/tests/tests/netstandard2.1/results.trx`, `pdd/tests/tests/net8.0/results.trx`, `pdd/tests/tests/net9.0/results.trx`, `pdd/tests/tests/net10.0/results.trx` |
| REASSEMBLY-002-MUTATION | passed `pdd/protocol/mutations/results.json` |
| REASSEMBLY-003-TEST | passed `pdd/tests/tests/netstandard2.0/results.trx`, `pdd/tests/tests/netstandard2.1/results.trx`, `pdd/tests/tests/net8.0/results.trx`, `pdd/tests/tests/net9.0/results.trx`, `pdd/tests/tests/net10.0/results.trx` |
| MASK-001-PROOF | passed `pdd/protocol/proof.log` |
| MASK-001-TEST | passed `pdd/tests/tests/netstandard2.0/results.trx`, `pdd/tests/tests/netstandard2.1/results.trx`, `pdd/tests/tests/net8.0/results.trx`, `pdd/tests/tests/net9.0/results.trx`, `pdd/tests/tests/net10.0/results.trx` |
| MASK-001-MUTATION | passed `pdd/protocol/mutations/results.json` |
| MASK-002-PROOF | passed `pdd/protocol/proof.log` |
| MASK-002-TEST | passed `pdd/tests/tests/netstandard2.0/results.trx`, `pdd/tests/tests/netstandard2.1/results.trx`, `pdd/tests/tests/net8.0/results.trx`, `pdd/tests/tests/net9.0/results.trx`, `pdd/tests/tests/net10.0/results.trx` |
| MASK-002-MUTATION | passed `pdd/protocol/mutations/results.json` |
| MASK-003-PROOF | passed `pdd/protocol/proof.log` |
| MASK-003-TEST | passed `pdd/tests/tests/netstandard2.0/results.trx`, `pdd/tests/tests/netstandard2.1/results.trx`, `pdd/tests/tests/net8.0/results.trx`, `pdd/tests/tests/net9.0/results.trx`, `pdd/tests/tests/net10.0/results.trx` |
| MASK-003-MUTATION | passed `pdd/protocol/mutations/results.json` |
| WRITE-001-TEST | passed `pdd/tests/tests/netstandard2.0/results.trx`, `pdd/tests/tests/netstandard2.1/results.trx`, `pdd/tests/tests/net8.0/results.trx`, `pdd/tests/tests/net9.0/results.trx`, `pdd/tests/tests/net10.0/results.trx`, `pdd/controlled/queued/results.json` |
| LEASE-001-TEST | passed `pdd/tests/tests/netstandard2.0/results.trx`, `pdd/tests/tests/netstandard2.1/results.trx`, `pdd/tests/tests/net8.0/results.trx`, `pdd/tests/tests/net9.0/results.trx`, `pdd/tests/tests/net10.0/results.trx`, `pdd/controlled/leases/results.json` |
| EPOCH-001-TEST | passed `pdd/tests/tests/netstandard2.0/results.trx`, `pdd/tests/tests/netstandard2.1/results.trx`, `pdd/tests/tests/net8.0/results.trx`, `pdd/tests/tests/net9.0/results.trx`, `pdd/tests/tests/net10.0/results.trx`, `pdd/controlled/reconnect/results.json` |
| HEADER-001-PROOF | passed `pdd/protocol/proof.log` |
| HEADER-001-TEST | passed `pdd/tests/tests/netstandard2.0/results.trx`, `pdd/tests/tests/netstandard2.1/results.trx`, `pdd/tests/tests/net8.0/results.trx`, `pdd/tests/tests/net9.0/results.trx`, `pdd/tests/tests/net10.0/results.trx` |
| HEADER-001-MUTATION | passed `pdd/protocol/mutations/results.json` |
| HEADER-002-PROOF | passed `pdd/protocol/proof.log` |
| HEADER-002-TEST | passed `pdd/tests/tests/netstandard2.0/results.trx`, `pdd/tests/tests/netstandard2.1/results.trx`, `pdd/tests/tests/net8.0/results.trx`, `pdd/tests/tests/net9.0/results.trx`, `pdd/tests/tests/net10.0/results.trx` |

Review evidence is separate and not-run; no requirement is claimed satisfied without it.
Stryker: passed; controlled ordering experiment: passed; boundary experiments: passed.

Receipts are traceability records, not signed attestations.

