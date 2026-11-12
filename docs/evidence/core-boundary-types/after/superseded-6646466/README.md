# Superseded after runs on 6646466 (2026-11-11)

These runs measured the first version of the production commit, `6646466`. That
version put the raw-code `ReadDataframeAsync` overload in `DataframeParsing`, which
made the symbol `DataframeParsing.ReadDataframeAsync` ambiguous for
`./scripts/pdd.sh validate` ("2 declarations") and failed 79 Pdd tests. The overload
was moved to its own class, `DataframeParsingRawState`, and the fix was folded into
the production commit before anything was pushed, giving `13ed44f`. Nothing else in
the production change differs. All after runs were then repeated on `13ed44f`; they
are in the parent directory and are the ones the report uses. These are kept, not
relabelled.

- `benchmark-run-N.out`: the three README benchmark runs on `6646466`, under
  `flock ~/worktrees/.timing.lock`. Restored from commit `4228afe`. Their `.meta` files
  (the `ps` output taken inside the lock and the load average) were overwritten by
  mistake when the runs on `13ed44f` wrote to the same paths, because a `git mv` of
  the ignored `.meta` files failed first. What the session console showed for them:
  run 1 exit 0, 8.34 s, finished 22:18:16Z; run 2 exit 0, 8.93 s, finished 22:21:10Z;
  run 3 exit 0, 8.40 s, finished 22:22:24Z. The `ps` output is lost. The benchmark
  compiles only `VerifiedNativeCopy.cs`, `Masking.g.cs` and `WebsocketMasking.cs`,
  which neither commit changes.
- `decision-path/after/`: the three probe runs on `6646466`, with their `.meta` files.
- `seeded/`: the seeded adapter defects on `6646466`, with their `.meta` files.
