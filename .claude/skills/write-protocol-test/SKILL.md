---
name: write-protocol-test
description: Use when writing or changing one test of the WebsocketClientLite protocol core, of an owner named in verification/boundaries.json, or of the asynchronous lifecycle (Close, abort, queued writes, reconnect, disposal, IsConnected). It takes the test from its claim to a recorded failing run, a passing run on every target and a hand-back of at most ten lines.
---

# Write one protocol test

The rules are in [verification/README.md](../../../verification/README.md#rules-for-verification-work),
"Rules for verification work", rules 1 to 9. Read them first. This file holds only the order in
which they apply; where it and the rules differ, the rules win.

One pass of this procedure is one test. A theory with its rows is one test.

## When to use it

- A new or changed test of the protocol core: header and frame parsing, framing, masking, the
  handshake and Close, the generated code in `verification/generated/` and the C# that calls it.
- A test of code owned by an entry in [verification/boundaries.json](../../../verification/boundaries.json),
  or one named by an entry there or in `verification/requirements.json`.
- A test of the asynchronous lifecycle: Close, abort, queued writes, reconnect, disposal and the
  `IsConnected` and `Sender` state of `ClientWebSocketRx`.

## 1. Before writing: the claim, its answer and its check

Write these down before any test code:

- **Claim.** The one sentence the test backs, and where it is: a requirement id in
  `verification/requirements.json` (such as `HEADER-001-TEST`), a boundary entry id, or a file and
  line in the documents. Quote the sentence. If no document holds it, the claim is new intent: go
  to step 7.
- **Source of the expected answer.** One of: an RFC 6455 section; a library policy named in a
  requirement's `origin` (`Library policy: ...`); a value written by hand from either; a verified
  reference such as the generated code, compared beside independent bytes (rule 2). Never a
  production helper.
- **The check.** The production rule that the test detects, the line or lines where production
  runs it, and the assertion that must fail when it is removed or shifted. A test with two sides,
  such as "accepts the limit and rejects its successor", names a check for each side.
- **Boundaries.** Read the `boundaries.json` entries for the code under test (AGENTS.md).

## 2. While writing

- Rule 2: expected bytes come from the RFC, are written by hand, or are computed in the test from
  the RFC's rule.
- Rule 5: a rejection test sends the complete input an accepting implementation needs. Compare
  production text only through the shared internal constant.
- Rule 6: order asynchronous steps with signals (`TaskCompletionSource` created with
  `TaskCreationOptions.RunContinuationsAsynchronously`) and release them in the order the test
  chooses. No `Thread.Sleep` or `Task.Delay` orders anything. A bounded wait such as
  `WaitAsync(Limit)` is a diagnostic limit.
- Reuse a harness: `LoopbackWebSocketServer`, the `Harness` in `ControlledCloseTests`, or `Input`
  and `Connection` in `ParserRetentionTests`.
- Give each assertion a message that names what went wrong. That message is the failure line the
  evidence records.

## 3. See it fail (rule 3 or rule 4)

- For a fix, run the test on the commit before the fix (rule 3).
- For existing behaviour (rule 4), commit the test on the topic branch, then make two copies of
  that commit with `git archive <commit> | tar -x -C <dir>` in a scratch directory: one untouched,
  one edited. Remove or shift the one check, at every line where production runs that rule and
  nowhere else; in generated code, edit `verification/generated/Masking.g.cs`, not the Dafny.
  Record the edit with `diff -ruN -x bin -x obj <untouched> <edited>`.
- Run the test in the edited copy with a `--filter` for it and a TRX logger. One library target is
  enough for this run; record which.
- It must fail on the named assertion. A compile error, another assertion or a timeout is not a
  detection: keep that run, report it (rule 8), correct the test or the edit, and make a new run.
- A two-sided test needs a failing copy for each side.
- If the test fails on the unchanged code, it is a suspected defect (rule 7). Record the failing
  run, do not fix it in the same change, and go to step 7.

## 4. See it pass

- Run the same filter on the unchanged commit.
- Run `./test-all-tfms.sh` so the test runs against every library build, and record the count per
  target.

## 5. Record the evidence

Under `docs/evidence/<topic>/`, as loose files in the style of `docs/evidence/header-length`:

- `README.md`: the date, the host (OS, kernel, .NET SDK, test host), the commits used, and for each
  run its command, its result (run and passed, run and failed, inconclusive, not run) and its
  failure line.
- For each run: `<run>.diff`, `<run>.trx` (it holds the failure line), `<run>.exit` (exit code and
  duration) and `<run>.identity.txt` (the commit, the edit, and the SHA-256 of the edited files as
  built).
- A console log is optional. `*.log` is gitignored, so a log the README cites is added with
  `git add -f`; check `git status --ignored docs/evidence/<topic>` before handing back.
- A failed or interrupted run stays in the evidence; a new run gets a new name.

## 6. Hand back

At most ten lines per test. The owner reviews this instead of the test code, so every line names
something they can open:

```text
Test: <Class.Method>, <n> rows
Claim: "<sentence>" (<requirement id | boundary entry | file:line>)
Expected answer: <RFC section | library policy | hand-written value | verified reference beside independent bytes>
Failing run: copy of <commit>, edit <name> (<what it removes or shifts>), <target>; failed: "<failure line>"
Passing run: <commit>, <filter result>; ./test-all-tfms.sh at <commit>: <n of n> on each of <targets>
Does not cover: <what stays unbacked, combined coverage, targets the failing run did not use>
Evidence: docs/evidence/<topic>/
```

Give one "Failing run" line per edit. Name what the test does not cover even when it seems obvious.

## 7. Stop and ask (rule 9)

Stop and end with `STATUS: QUESTION` when:

- no document holds the claim, or the RFC and the documents leave the expected answer open;
- the test would pass only with weaker expected behaviour or a changed contract;
- the test fails on the unchanged code (rule 7).

Finish everything that does not depend on the answer, commit nothing that does, and give each
question with its evidence, the options and a recommendation.
