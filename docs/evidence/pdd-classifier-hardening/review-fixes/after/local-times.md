| Experiment | local suite, 29697a4 | local suite, 5fd8632 | protocol capture, 5fd8632 |
| --- | --- | --- | --- |
| `baseline` | 9.1 s, passed | 9.4 s, passed | 9.0 s, passed |
| `CLOSE-001-duplicate-send` | 9.0 s, detected | 9.0 s, detected | 8.8 s, detected |
| `CLOSE-001-suppressed-send` | 8.8 s, detected | 8.9 s, detected | 8.8 s, detected |
| `CLOSE-002-state-bit-loss` | 8.9 s, detected | 8.8 s, detected | 8.9 s, detected |
| `CLOSE-003-premature-release` | 9.0 s, detected | 8.9 s, detected | 9.1 s, detected |
| `CLOSE-003-suppressed-release` | 9.9 s, detected | 9.0 s, detected | 8.9 s, detected |
| `REASSEMBLY-001-offset-stall` | 9.8 s, detected | 8.7 s, detected | 9.1 s, detected |
| `REASSEMBLY-002-offset-stall` | 9.3 s, detected | 9.0 s, detected | 9.2 s, detected |
| `REASSEMBLY-001-oversized-result` | 9.2 s, detected | 8.9 s, detected | 9.5 s, detected |
| `MASK-001-xor-to-or` | 8.8 s, detected | 8.6 s, detected | 9.6 s, detected |
| `MASK-001-key-write` | 9.0 s, detected | 8.6 s, detected | 9.7 s, detected |
| `MASK-002-tail-xor-to-or` | 8.8 s, detected | 8.7 s, detected | 9.3 s, detected |
| `MASK-002-corrupt-bulk-byte` | 8.8 s, detected | 8.9 s, detected | 9.2 s, detected |
| `MASK-002-preserve-prefix` | 9.4 s, detected | 8.8 s, detected | 9.2 s, detected |
| `MASK-003-xor-to-or` | 9.0 s, detected | 8.9 s, detected | 9.3 s, detected |
| `MASK-003-preserve-prefix` | 8.8 s, detected | 9.0 s, detected | 9.1 s, detected |
| `HEADER-001-short-sixteen-bit` | 8.8 s, detected | 8.5 s, detected | 9.0 s, detected |
| `HEADER-001-short-sixty-four-bit` | 9.0 s, detected | 8.8 s, detected | 9.0 s, detected |

local suite, 29697a4: mutants with a time 17, min 8.8 s, max 9.9 s, over 30 s (a third of the 90 s budget): 0
local suite, 5fd8632: mutants with a time 17, min 8.5 s, max 9.0 s, over 30 s (a third of the 90 s budget): 0
protocol capture, 5fd8632: mutants with a time 17, min 8.8 s, max 9.7 s, over 30 s (a third of the 90 s budget): 0
