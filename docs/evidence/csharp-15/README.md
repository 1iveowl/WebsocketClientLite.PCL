# Evidence for P2: C# 15 in the library, deferred

P2 replaces the internal handshake tuple `(HandshakeStateKind, WebsocketClientLiteException?)`
with a C# 15 union, `HandshakeOutcome(HandshakeAccepted, HandshakeFailed)`, and uses a
`with(...)` collection expression argument for the header dictionary. It works: Release
build with no warnings on all six targets, and 328 tests passed on each of the six library
builds on a .NET 11.0.0 host (2026-11-14). It is not merged for 10.4.0 (owner decision 4 in
the [plan](../../net11-10.4-plan.md)), because the Stryker gate cannot compile it.

The four logs are `scripts/stryker-close.sh` runs on 2026-11-14, with ANSI colours and the
worktree path removed:

| Log | Source | Stryker | Result |
| --- | --- | --- | --- |
| `stryker-p2.log` | this branch (union and `with`) | 5.0.0, the pinned version | CS0246 `union` not found, CS1001, CS0501 in `HandshakeOutcome.cs`; "Failed to restore the project to a buildable state", score 0.00 % |
| `stryker-p2-51.log` | this branch | 5.1.0, the newest (2026-11-10) | the same errors |
| `stryker-with.log` | P1 plus only the `with(...)` line | 5.0.0 | CS8652 "collection expression arguments is currently in Preview and unsupported"; build aborted |
| `stryker-with-51.log` | P1 plus only the `with(...)` line | 5.1.0 | the same error |

Stryker compiles the whole library with the Roslyn it bundles, not the SDK's, so one C# 15
construct in any file stops every mutant. `scripts/stryker-close.sh` exited 0 in all four
runs although it sets `pipefail`: Stryker itself returns 0 after its fatal compile error.
The Pdd gate fails on the missing report, but the script on its own does not.

Three properties of the union were checked on 2026-11-14 with a separate two-project probe
(a `netstandard2.0` and `net11.0` library with an `InternalsVisibleTo` .NET 11 consumer):

- Before .NET 11 the compiler needs `IUnion` and `UnionAttribute`; `UnionPolyfill.cs`
  declares them internal for those targets. The consumer matched the cases of both library
  builds correctly, with no warning.
- `default(HandshakeOutcome)` matches no case: a switch expression over it throws
  `SwitchExpressionException`, and the compiler does not warn. The connect path keeps a
  switch expression for that reason; a discard arm or a switch statement would treat it as
  an acceptance.
- CA1508 calls the last arm of that switch expression always true. It is suppressed on
  those lines only, with the reason beside it.

To merge this after a Stryker release that bundles Roslyn 5.12: update the pin in
`.config/dotnet-tools.json` and the version check in `tools/Pdd` (`HasPinnedStrykerVersion`),
rebase, run Stryker, and re-triage `verification/stryker-close-dispositions.json`. The
change moves lines in `WebsocketConnectionHandler.cs`, and a mutant's key includes its line,
so every disposition below line 104 needs its new key.
