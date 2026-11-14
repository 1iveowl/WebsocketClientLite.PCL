# Evidence for P3: Runtime Async for net11.0

The decision report is [`verification/runtime-async.md`](../../../verification/runtime-async.md).
Everything here was run on 2026-11-14 in the Linux x64 dev container, SDK 11.0.100,
runtime 11.0.0.

## Correctness: the full local four-job capture

On commit `1830aa2`, clean worktree, with the `net11.0` build compiled with
`runtime-async=on`. Machine evidence gate: **passed**. `summary.md` and `receipt.json` are the
aggregate's output, copied unchanged.

| Job | Seconds |
| --- | --- |
| protocol | 188 |
| tests | 189 |
| controlled | 78 |
| stryker | 366 |

- All six library builds passed the full suite on a .NET 11.0.0 host. The `net11.0` build is
  the one with Runtime Async.
- The controlled ordering, boundary, queued, lease, retention and reconnect experiments ran
  against the `net11.0` build (`Evidence.PrimaryTfm`) and passed.
- Stryker against `net11.0`: 42 killed, 27 survived, 7 no coverage, 0 timeouts, 3 compile
  errors, 26 ignored, the same as without Runtime Async (P1, `docs/evidence/net11-platform/`),
  and in all reported files the same 163 ignored and 10 compile errors. Every outcome matched
  a recorded disposition; all 16 required kills were killed.

## Stryker mutated the Runtime Async build

Stryker compiles the library itself, so it was not given that its compilation honours
`runtime-async=on`. A temporary test, not committed, asserted that the loaded
`WebsocketClientLite` assembly has no `IAsyncStateMachine` types. It passed in a normal test
run. Under `scripts/stryker-close.sh`, which stops on an initial test failure, Stryker's
initial run of 329 tests passed, and the run finished with the same 55.26 % score. Stryker
runs that initial run on its own instrumented compilation, so that compilation had no state
machines. The probe covers no handler code, so it did not run in the per-mutant runs; those
use the same compilation with one mutant switched on.

## The two builds

`state-machines.txt` is the output of `smcount/` (metadata only, nothing loaded) on the two
`net11.0` libraries of the benchmark: 48 state machines and no runtime-async methods off,
none and 48 on, 139,264 and 104,448 bytes.

## Cost

`benchmark/` holds the 40 runs of `verification/benchmark-async/compare.sh` (ten blocks of
off, on, on, off) and `summary.md`, made by `summarize.py` from them. The table and its
reading are in the decision report. Each run file starts with the library it loaded and its
state machine count; `compare.sh` failed none.
