# P3-D intent before implementation

Baseline: release 6709c99, containing A and Phase 2 G. The exact G archive and
receipt digests match the Phase 3 plan. Sequential subscriptions (including
finite Retry/Repeat) with internal sockets are supported. Each attempt owns its
service and socket. A retained Sender remains attached to that attempt; after
teardown it rejects sends and never forwards to a successor. Sender is the most
recent successful handshake, not a reconnecting proxy, and may remain non-null
when disconnected. Old teardown must not dispose a successor.

Disposal is permanent: a subscription after disposal must terminate with
ObjectDisposedException without admitting a service. Setup already in progress
must either be registered before disposal takes ownership or rejected and cleaned
up. Subscription cancellation must clean up a service whose creation completes
late. There is no promise that external connection work finishes by a deadline.

Concurrent active subscriptions' aggregate IsConnected/Sender policy is explicitly
deferred. No tests here define that public policy. A successor subscribed from an
old terminal callback is sequential logical reconnect even if old cleanup has not
returned. G observer/ping and caller-context cases remain inherited acceptance.

Any new production guard requires an executed reduced counterexample. Tests and
isolated defects establish selected C# schedules only. Human semantic acceptance
remains a PR review obligation, distinct from machine evidence.

## Reduced counterexamples and scoped correction

At 6709c99 both Dispose and DisposeAsync followed by a new loopback subscription
reach WebsocketConnected instead of ObjectDisposedException. A second reduced
schedule completes generation 1, starts generation 2 from its terminal callback,
holds that callback until generation 2 handshakes, then joins generation 1's
actual shared close task. The successor remains registered but IsConnected is
false. These are executed failures, not inferred races.

The correction uses one lifecycle lock to order admission/registration against
permanent disposal. It never waits for service cleanup under that lock. The
factory previously returned Task.FromResult around synchronous construction; it
now returns the service directly. Rx Using installs the identity-bound disposer
before async connection setup. A per-subscription identity gates only the final
false notification. Public signatures, protocol core, sender and connection-handler
algorithms and G observer/ping seams are unchanged. Scope review found no need for
a scheduler, public reconnect API or broader Rx redesign.

Nine cases exercise the two terminal paths (Repeat and Retry), a reentrant terminal
callback, three held-handshake teardown modes, two permanent-disposal modes and
failed handshake cleanup. Tests inspect actual registered service identity and
closed stream state; real successor echo proves positive progress. Private-field
inspection is test-only and fails loudly on owner-field renaming. The isolated
missing-removal edit must fail `Old service retained registration` after the
unchanged baseline succeeds; timeout or an unrelated failure cannot count.

The held-handshake cases test subscription cancellation, not a new meaning for
the public cancellationToken (which also participates in frame/control handling).
No arbitrary connect/dispose schedule or OS connection deadline is proved. The
reentrant schedule's progress assumes the second handshake and finite callbacks
return. G's single-generation observer, ping and non-pumping-context tests remain
mandatory inherited evidence. Human acceptance is still separate from this scoped
implementation review and from automated receipts.
