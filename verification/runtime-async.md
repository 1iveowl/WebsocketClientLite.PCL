# Runtime Async for the net11.0 build

Step P3 of the [10.4 plan](../docs/net11-10.4-plan.md), measured on 2026-11-14. Owner
decision 2: turn on .NET 11 Runtime Async for `net11.0` only, with the full evidence run and
before and after numbers, and do not adopt it if the tests or the numbers argue against it.

**Recommendation: adopt.** The `net11.0` build has no compiler-generated async state
machines left. It is a quarter smaller, it allocates slightly less on every measured path,
and it is not measurably slower anywhere. Correctness evidence is in
[`docs/evidence/runtime-async/`](../docs/evidence/runtime-async/README.md).

## What changes

`WebsocketClientLite.csproj` sets `<Features>runtime-async=on</Features>` when the target is
`net11.0`. Runtime Async is off by default in SDK 11.0.100, and the `Features` flag is the
only switch: the SDK does not define `UseRuntimeAsync`. The other five builds keep state
machines; the flag is a .NET 11 runtime feature. `-p:WebsocketRuntimeAsync=false` builds
`net11.0` without it, for comparison. No source line changes.

## It takes effect

Counted in the metadata of the two `net11.0` builds (types that implement
`IAsyncStateMachine`, and methods with `MethodImplAttributes` 0x2000, `Async`):

| Build | Size | Async state machines | Runtime-async methods |
| --- | --- | --- | --- |
| off | 139,264 bytes | 48 | 0 |
| on | 104,448 bytes | 0 | 48 |

The two counts are equal: the 48 async methods that had a state machine are now suspended and resumed by the runtime (the counts match; the tool does not pair them one by one).

## Cost

`verification/benchmark-async/` is an end-to-end loopback benchmark through the public
API: connect, handshake and close a session, and echo text messages of 32 B, 4 KiB and
64 KiB on one connection. `compare.sh` builds both variants and runs them in ten blocks of
off, on, on, off (20 runs each). It fails a run whose library is not the variant it was
meant to be. Each run reports the median of seven trials after a warm-up;
`summarize.py` combines the runs. .NET 11.0.0, Linux x64 dev container:

| Workload | Off, µs (median, range of 20) | On, µs (median, range of 20) | On / off | Off, B/op | On, B/op |
| --- | --- | --- | --- | --- | --- |
| connect-handshake-close | 494.03 (241.24 to 937.84) | 459.35 (213.52 to 928.65) | 0.930 | 65,533 | 65,182 |
| echo-32B | 12.67 (11.99 to 17.10) | 12.00 (10.92 to 17.88) | 0.947 | 1,579 | 1,363 |
| echo-4KiB | 15.75 (13.07 to 23.56) | 14.14 (12.92 to 23.27) | 0.898 | 22,008 | 21,784 |
| echo-64KiB | 90.00 (80.73 to 107.31) | 89.34 (79.89 to 96.05) | 0.993 | 329,354 | 329,130 |

The medians are all slightly lower with Runtime Async, but the ranges overlap widely, so
the timing claim is only that there is no measurable slowdown. Allocation per operation is
lower in every workload, by 216 to 351 bytes: 14 % of a 32-byte echo, and less than 1 % where
the payload dominates. The in-process server shares the process, so its time and
allocations are in both columns; it is the same code in both builds. These are local
measurements, not a network throughput claim or a portable JIT guarantee.

## What it does not show

- The race and lifecycle tests exercise schedules the tests control. Runtime Async changes
  how every `await` in the `net11.0` build suspends; the controlled experiments run against
  that build (P1 made `net11.0` their target), but they do not prove arbitrary schedules.
- Live stack traces and debugger frames change (fewer infrastructure frames). Exception
  stack traces were already cleaned by the compiler and look the same.
- Consumers on .NET 11 get this build. Consumers on earlier runtimes get builds without it.

## A finding on the way: the test server's small echoes wait 40 ms

The first benchmark used the test helper `LoopbackWebSocketServer.EchoLoopAsync`. A 32-byte
echo took 44,869 µs and a 64 KiB echo 142 µs. The helper writes a frame's header and its
payload as two writes; with Nagle's algorithm on, the second write waits for the ACK of the
first, which the client delays by about 40 ms. With one write per frame the 32-byte echo
takes 16 µs, so the client is not affected. The benchmark echoes with one write. The tests
still use the helper: every small echo in them pays the delay, and nothing in them depends
on latency.
