|Name|Compiled|Explicit Assumption|Extern|Issue|Mitigation|
|----|--------|-------------------|------|-----|----------|
| ProtocolCore.NativeCopy.CopySlice | True | False | True | Declaration with `{:extern}` has a ensures clause. | Test external callee (maybe with `/testContracts`). |
| ProtocolCore.NativeCopy.CopySlice | True | False | True | Declaration with `{:extern}` has a requires clause. | Test any external callers (maybe with `/testContracts`). |

<a id="boundary-epoch-progress"></a>
### Sequential reconnect progress

P3-D tests selected two-generation and paused-handshake schedules through the
production client and real loopback transports. Rx terminal/disposal behavior,
finite callbacks, eventual scheduling and external connection completion remain
trusted. See [intent and scoped review](reconnect-intent.md). Concurrent active
subscriptions' shared status policy remains deferred.
