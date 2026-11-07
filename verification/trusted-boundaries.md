|Name|Compiled|Explicit Assumption|Extern|Issue|Mitigation|
|----|--------|-------------------|------|-----|----------|
| ProtocolCore.NativeCopy.CopySlice | True | False | True | Declaration with `{:extern}` has a ensures clause. | Test external callee (maybe with `/testContracts`). |
| ProtocolCore.NativeCopy.CopySlice | True | False | True | Declaration with `{:extern}` has a requires clause. | Test any external callers (maybe with `/testContracts`). |
