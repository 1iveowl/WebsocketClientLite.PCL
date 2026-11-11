# 10.3 session plan: header traceability, test parity and two boundary experiments

Written 2026-11-11. The work is too large for one session, so it runs as nine
steps, S1 to S9, each in a new session started with the prompt given below. Run
them in order. The owner merges pull requests between steps at the merge points.
Releasing stays the owner's step.

| Step | Work | Branch | Pull request | Needs | Model, effort |
| --- | --- | --- | --- | --- | --- |
| S1 | RFC citations in `Protocol.dfy`, regenerated C# | `chore/header-rule-traceability` | #136 (draft) | none | Opus 5.5, high |
| S2 | Header requirement entries, header mutants, unmapped table, all checks | same | #136 | S1 | Opus 5.5, high |
| M1 | **Owner reviews and merges #136 into `releases/10.3.0.0`** | | | S2 | review: optional |
| S3 | Test parity, part 1: tests that pass on end of stream, read-before-payload | `fix/header-test-parity` | new draft | M1 | Opus 5.5, high |
| S4 | Test parity, part 2: reserved values, limit boundary, truncation, checks | same | same | S3 | Opus 5.5, high |
| M2 | **Owner reviews and merges the test parity PR** | | | S4 | review: optional |
| S5 | Experiment 1 baseline: decision table and before numbers | `chore/reconnect-pure-decision` | new draft | none | Opus 5.5, high |
| S6 | Experiment 1: pure function, plain tests, after numbers, recommendation | same | same | S5 | Opus 5.5, high or xhigh |
| M3 | **Owner decides adopt or not adopt and merges or closes** | | | S6 | review: optional |
| S7 | Experiment 2 baseline: core call inventory and before numbers | `chore/core-boundary-types` | new draft | M1 | Opus 5.5, high |
| S8 | Experiment 2: typed adapter, after numbers, recommendation | same | same | S7 | Opus 5.5, high |
| M4 | **Owner decides adopt or not adopt and merges or closes** | | | S8 | review: optional |
| S9 | Release readiness on `releases/10.3.0.0`, README draft | `docs/readme-10.3.0` | new | M1 to M4 | Sonnet 5.5, high |
| M5 | **Owner releases**: `GitVersion.yml`, publish, release branch into `main` | | | S9 | |

Start each session with `/model` and `/effort` set as the table says, using the
explicit model ID: `claude-opus-5-5` or `claude-sonnet-5-5`, never an alias. S6
refactors the concurrency-sensitive P3-D lifecycle code without changing its
behaviour; xhigh gives more margin there if the extra cost is acceptable. S9 mostly runs known checks and drafts
the README, so Sonnet is enough.

Before each merge point M1 to M4, you may run `/code-review` on the step's pull
request in a fresh session, so that the review is not anchored on the session that
did the work. Findings are posted on the pull request and fixed on its branch
before merging. The review is optional and does not replace the step's own checks.

S5 and S6 do not depend on the header work and may run while M1 or M2 is
pending. Every other step checks its "Needs" column first and ends
`STATUS: BLOCKED` when it is not met.

## Owner decisions (2026-11-11)

1. Pull requests target `releases/10.3.0.0`, created from `main` at `30cf44a`.
2. Each pull request is merged into `releases/10.3.0.0` when its steps are done
   (M1 to M4). `main` receives everything at the end through the release pull
   request.
3. S2 may make the minimal tooling changes that header requirements and mutants
   need, and nothing more:
   - add `HEADER` to the requirement ID pattern in
     `verification/requirements.schema.json`;
   - add `HEADER` to the mutant ID pattern in `tools/Pdd/Mutations.cs` and
     `ProtocolCore.Protocol.DecodeHeader` to its allow-list of mutation scopes;
   - put header mutants in the existing `verification/mutations/payload.json`.
     The payload suite already selects every requirement that is not `CLOSE`.
     There is no new catalog file and no new field;
   - update the requirement count the Pdd tests expect.

   Each change is listed in the pull request. Anything beyond these is asked
   first.
4. Test parity extends the rejection rows to every reserved value: RSV2, RSV3,
   opcodes 4 to 7 and 11 to 15.
5. The 7 November session log stays local. Its timeline extract is archived in
   `docs/evidence/header-length/original-session-timeline.json`.

Later owner decisions are posted as a comment headed "Owner decision <date>" on
the pull request of the step they affect. Sessions read those comments.

## Common rules for every step

These apply to every session in addition to AGENTS.md and CONTRIBUTING.md, which
are binding.

- **Attribution.** No `Co-Authored-By` trailer naming an agent, model or vendor,
  and no "generated with" or 🤖 line in commits, pull request titles or pull
  request bodies. Commit with the repository user's git identity. Before every
  push run the two checks in AGENTS.md: the first must show people only, the
  second must print nothing.
- **Branches.** Work only on the step's branch. Never commit to `main` or a
  release branch, never force-push, never merge, never change `GitVersion.yml`,
  never release. Push the step's branch and open or update its pull request
  against `releases/10.3.0.0` as the step says. Do not rewrite pushed commits.
- **Scope.** No change to public API, public behaviour or Dafny contracts
  (`requires`, `ensures`, invariants, predicate bodies) unless a step says
  otherwise. No new production dependency. If something only works with such a
  change, stop and ask.
- **Evidence.** A timeout is inconclusive, never a detection or a pass. A failed or
  interrupted run is kept and reported, never retried until green and never
  relabelled. Record baselines before changing code, from a clean tree, and keep
  the commands and raw output under `docs/evidence/<topic>/`, as loose files in
  the style of `docs/evidence/p3-f` and `docs/evidence/header-length`. Label
  everything with the date it was made.
- **Check results.** Report each check as run and passed, run and failed,
  inconclusive, or not run. Never summarise a check you did not run as passing.
  The commands are in `verification/README.md`, sections "Checks" and "CI
  verification receipts", and in `tools/Pdd/README.md`. The full local evidence
  set is the four `./scripts/pdd.sh capture` jobs (protocol, tests, controlled,
  stryker), each into a new output directory, then `aggregate`.
- **Decide or ask.** Decide yourself when a choice stays inside the step, is easy
  to reverse, and the plan, the briefs, the code and the documents point to one
  reasonable option. List each such choice under "Decisions for owner review".
  Ask when the choice changes behaviour another step relies on, is hard to
  reverse, needs a schema or tooling change not authorized above, or would
  weaken, skip or approximate a check or a "Done when" item. To ask, first finish
  everything that does not depend on the answer, do not commit the dependent
  part, and end with `STATUS: QUESTION`, then each question with its evidence,
  the options and your recommendation. The owner answers in the same session.
- **Context.** Keep the session below about 500K tokens of context. If the step
  will not fit, finish the current bounded piece, commit only if the tree builds
  and its checks pass, post a comment headed "Hand-off" on the step's pull
  request (done, verified with evidence, uncommitted state, remaining steps in
  order) and end with `STATUS: HANDOFF`. The owner then reruns the same step's
  prompt in a new session.
- **Long commands.** Run them in the foreground, or write to a log and wait for
  the process to exit. Never end a message while a check is still running.
- **Receipt.** Post the receipt as a comment on the step's pull request, then end
  the session with it. The first characters of the final message are
  `STATUS: DONE`, `STATUS: QUESTION`, `STATUS: HANDOFF` or `STATUS: BLOCKED`,
  with nothing before them. In at most 20 lines, the receipt holds:
  - each "Done when" item with its result;
  - each check with its result word;
  - Decisions for owner review;
  - the files changed;
  - the commit hashes.

## Steps

### S1. RFC citations beside every proved contract

Branch `chore/header-rule-traceability`, pull request #136. Brief: Appendix A,
item C. Items A and B of that brief are done. Their results are in
`docs/header-length-validation.md` on the branch and in the #136 description.

Verified at `28e30cb` on 2026-11-11:

- `verification/Protocol.dfy` has no RFC citation.
- `verification/Masking.dfy:6` is the form to copy:
  `// RFC 6455 section 5.3. ...`.
- The `origin` fields in `verification/requirements.json` show the
  "Library policy: <what>" form.
- `scripts/dafny-masking.sh --write` regenerates
  `verification/generated/Masking.g.cs`; `--check` verifies and compares.
- `HeaderRoundTrip` has its own 120-second limit (#133).
- In `HeaderValid`, the clause `(length & 0x8000000000000000) == 0` is implied by
  `length <= 0x7fffffff`. It stays, because predicate bodies are not changed.

What to do:

1. Record the verifier's last output line before any edit.
2. Read RFC 6455 itself (https://www.rfc-editor.org/rfc/rfc6455.txt) and its
   errata. Cite from the text, not from memory.
3. Add one comment per contract-bearing method and lemma, and per predicate
   those contracts use (`Known`, `PrefixValid`, `HeaderValid`, `TxState`,
   `CloseCode`, `CloseValid`, `HandshakeValid`). Each names its RFC 6455 section,
   or says "Library policy:" and what the policy is. Where one contract mixes
   both, say so in the same comment. If a contract cannot be tied to a section,
   do not cite anything approximate: list it as unresolved.
4. Show that the `.dfy` diff is comments only, for example by comparing both
   versions with comment lines removed.
5. Regenerate, run `--check`, and record the last line again. It must be
   identical. A timeout is inconclusive: report it and do not retry it into a
   pass.

Done when:

1. Every such declaration in `Protocol.dfy` has a citation or a policy label, or
   is in the unresolved list.
2. The `.dfy` diff is shown to be comments only.
3. The C# is regenerated and `scripts/dafny-masking.sh --check` passed.
4. The verifier's last line is identical before and after, quoted exactly.
5. One commit. The branch is pushed and the receipt is posted on #136.

Estimate, not measured: 200K to 300K tokens.

```text
Run step S1 of the 10.3 session plan in repository 1iveowl/WebsocketClientLite.PCL
(checkout /workspaces/WebsocketClientLite.PCL).

First: git fetch -q origin, then read the plan in full:
git show origin/releases/10.3.0.0:docs/pdd-10.3-session-plan.md, or, if that path
does not exist yet, git show origin/docs/pdd-10.3-session-plan:docs/pdd-10.3-session-plan.md.
Read AGENTS.md and CONTRIBUTING.md; they are binding, as are the plan's "Common
rules for every step" and "Owner decisions". Do step S1 and nothing else. Its
brief is Appendix A, item C.

Then: confirm git status is clean, switch to chore/header-rule-traceability and
fast-forward it to origin/chore/header-rule-traceability, and read the
description and every comment on pull request #136 with gh. Check S1's "Needs";
if it is not met, end STATUS: BLOCKED.

Do the work under "What to do", meet every "Done when" item, push the branch,
post the receipt as a comment on #136 and end with the receipt as the plan's
Common rules describe.
```

### S2. Header requirements, header mutants, the unmapped table and every check

Branch `chore/header-rule-traceability`, pull request #136. Brief: Appendix A,
items D, E and F, and its "Done means". Owner decision 3 authorizes the tooling
changes listed there.

Verified at `28e30cb` on 2026-11-11:

- `requirements.schema.json` allows only `CLOSE|REASSEMBLY|MASK|WRITE|LEASE|EPOCH`
  IDs.
- `tools/Pdd/Mutations.cs` accepts only `CLOSE|REASSEMBLY|MASK` mutant IDs. Its
  allow-list of mutation scopes has no `DecodeHeader`, and it reads only
  `close.json` and `payload.json`.
- `tools/Pdd.Tests/RequirementsTests.cs:45` expects 14 requirements.
- `DecodeHeader` sets its status by calling `HeaderValid`, which is the
  specification and is not a mutation target. A mutant therefore replaces that
  call in `DecodeHeader`'s body with a weakened inline copy of the check, for
  example accepting a 16-bit length below 126. It should fail at
  `ensures ... (status == 2 <==> HeaderValid(first, second, length, limit))`.
  Because the body calls the specification directly, that part of the proof is
  close to true by construction. Say so in the PR. The proof's real content is
  the length decoding (`ReadLength`, `Word`), the header size and the need-more
  status.
- The strengthened `RejectsMalformedFrames` can change Stryker outcomes. Do not
  edit `verification/stryker-close-dispositions.json` to absorb a change; report
  it.

What to do:

1. Make the authorized tooling changes first, with the Pdd tests passing.
2. Add the `HEADER-*` requirement entries (item D). Point them at the existing
   contract (`DecodeHeader`, `Admit`), the call site
   (`DataframeParsing.ReadDataframeAsync`), the tests
   (`RejectsMalformedFrames`, and `HugeLengthsRejectOnHeaderOnly` where it
   applies) and `docs/header-length-validation.md`. The review obligation is not
   marked done.
3. Add the header mutants (item E) and run the full mutation suite. If a mutant
   is not detected at its named clause, stop: that is a finding, not something to
   tune away.
4. Write the table from item F into the PR.
5. Run every check in Appendix A's "Done means", plus the coverage gate and the
   four `pdd.sh capture` jobs.
6. Rewrite the #136 description as the brief requires, including "Decisions for
   owner review", and mark the PR ready for review. Do not merge.

Done when:

1. `./scripts/pdd.sh validate` and the Pdd tests passed with the new entries.
2. Every mutant is detected at its named clause, and the full mutation suite
   result is reported with every outcome.
3. The F table is complete for `Protocol.dfy` and `Masking.dfy`.
4. Every check is reported with its result word. The verifier's last line is
   quoted.
5. The #136 description is complete and the PR is ready for review.
6. The commits are pushed and the receipt is posted on #136.

Estimate, not measured: 300K to 400K tokens. The mutation and Stryker runs are
the long part. A hand-off between items 3 and 5 is acceptable.

```text
Run step S2 of the 10.3 session plan in repository 1iveowl/WebsocketClientLite.PCL
(checkout /workspaces/WebsocketClientLite.PCL).

First: git fetch -q origin, then read the plan in full:
git show origin/releases/10.3.0.0:docs/pdd-10.3-session-plan.md, or, if that path
does not exist yet, git show origin/docs/pdd-10.3-session-plan:docs/pdd-10.3-session-plan.md.
Read AGENTS.md and CONTRIBUTING.md; they are binding, as are the plan's "Common
rules for every step" and "Owner decisions". Do step S2 and nothing else. Its
brief is Appendix A, items D, E and F, and its "Done means".

Then: confirm git status is clean, switch to chore/header-rule-traceability and
fast-forward it to origin/chore/header-rule-traceability, and read the
description and every comment on pull request #136, including S1's receipt and
any "Hand-off" or "Owner decision" comment. Check S2's "Needs" (S1 ended DONE);
if it is not met, end STATUS: BLOCKED.

Do the work under "What to do", meet every "Done when" item, push, post the
receipt on #136 and end with the receipt as the Common rules describe. Never
merge.
```

### S3. Test parity, part 1

Branch `fix/header-test-parity`, made from `origin/releases/10.3.0.0` after M1.
Brief: Appendix C, W1 and W2 item 2, under the ground rules of Appendix A. The
production code does not change.

What to do:

1. Confirm by running, not only by reading, that each test in W1 passes for the
   wrong reason. The test is run on an isolated copy of the current source with
   only the guarded check removed. Keep the output.
2. Strengthen those tests as W1 says.
3. Add the read-before-payload theory of W2 item 2, using the counting stream
   from `ParserRetentionTests` (share it in the test project if needed).
4. Show every changed or new test failing against code that lacks the
   behaviour: `a779602` where that version differed, otherwise an isolated copy
   with only that check removed. Show it passing on the current source. Keep the
   commands, source identities and raw output under
   `docs/evidence/header-test-parity/`.
5. Open a draft pull request against `releases/10.3.0.0`.

Done when:

1. Each W1 weakness is confirmed or refuted by a kept run.
2. The changed and new tests fail first and pass on the current source, with the
   evidence kept.
3. `./test-all-tfms.sh Release` passed.
4. The draft PR is open and the receipt is posted on it.

Estimate, not measured: 250K to 350K tokens.

```text
Run step S3 of the 10.3 session plan in repository 1iveowl/WebsocketClientLite.PCL
(checkout /workspaces/WebsocketClientLite.PCL).

First: git fetch -q origin, then read the plan in full:
git show origin/releases/10.3.0.0:docs/pdd-10.3-session-plan.md, or, if that path
does not exist yet, git show origin/docs/pdd-10.3-session-plan:docs/pdd-10.3-session-plan.md.
Read AGENTS.md and CONTRIBUTING.md; they are binding, as are the plan's "Common
rules for every step" and "Owner decisions". Do step S3 and nothing else. Its
brief is Appendix C, W1 and W2 item 2.

Check S3's "Needs": gh pr view 136 --json state must say MERGED, and
origin/releases/10.3.0.0 must contain "aborted unexpectedly" in
RejectsMalformedFrames. If not, end STATUS: BLOCKED. Then confirm git status is
clean and create fix/header-test-parity from origin/releases/10.3.0.0. Read the
#136 description and its comments for the requirement entries S2 added.

Do the work under "What to do", meet every "Done when" item, push, open the draft
PR against releases/10.3.0.0 with no attribution line, post the receipt on it and
end with the receipt as the Common rules describe.
```

### S4. Test parity, part 2, and checks

Branch `fix/header-test-parity`, its draft PR. Brief: Appendix C, W2 items 4, 6
and 7. Owner decision 4 settles item 4.

What to do:

1. Add the rows and tests, each shown failing first as in S3, item 4.
2. Add the new tests to the test obligations of the `HEADER-*` requirement
   entries from S2, and run `./scripts/pdd.sh validate`.
3. Write `docs/header-test-parity-validation.md` in the style of
   `docs/header-length-validation.md`. It lists each claim (Appendix C, W2
   table), the test that now backs it, its failing-first evidence, and what stays
   unbacked.
4. Run the five-target matrix, the coverage gate, the Pdd tests and the four
   capture jobs. Report Stryker changes as in S2.
5. Complete the PR description with "Decisions for owner review" and mark it
   ready.

Done when:

1. Every W2 row has a backing test, or is listed as unbacked with the reason.
2. Each new test has failing-first evidence.
3. `pdd.sh validate` passed.
4. Every check is reported with its result word.
5. The PR is ready for review, and the receipt is posted on it.

Estimate, not measured: 250K to 350K tokens.

```text
Run step S4 of the 10.3 session plan in repository 1iveowl/WebsocketClientLite.PCL
(checkout /workspaces/WebsocketClientLite.PCL).

First: git fetch -q origin, then read the plan in full:
git show origin/releases/10.3.0.0:docs/pdd-10.3-session-plan.md, or, if that path
does not exist yet, git show origin/docs/pdd-10.3-session-plan:docs/pdd-10.3-session-plan.md.
Read AGENTS.md and CONTRIBUTING.md; they are binding, as are the plan's "Common
rules for every step" and "Owner decisions". Do step S4 and nothing else. Its
brief is Appendix C, W2 items 4, 6 and 7.

Then: confirm git status is clean, switch to fix/header-test-parity and
fast-forward it to its origin branch, find its pull request with
gh pr list --head fix/header-test-parity, and read the description and every
comment, including S3's receipt. Check S4's "Needs" (S3 ended DONE); if it is not
met, end STATUS: BLOCKED.

Do the work under "What to do", meet every "Done when" item, push, post the
receipt on the PR and end with the receipt as the Common rules describe. Never
merge.
```

### S5. Experiment 1 baseline: the reconnect lifecycle decision

Branch `chore/reconnect-pure-decision`, made from `origin/releases/10.3.0.0`.
Brief: Appendix B, ground rules, Experiment 1, items 1 and 4 (before). No
production or test code changes in this step.

Verified on 2026-11-11 (the session verifies again):

- `src/main/WebsocketClientLite/ClientWebSocketRx.cs` has `_lifecycleGate`,
  `_disposed` and `_generation` at lines 24 to 26, lock sites at lines 251, 263,
  279 and 301, and `_disposed = true` under the gate at lines 331 and 368.
- The intent is in `verification/reconnect-intent.md` and
  `docs/p3-d-validation.md`.
- The seeded defects are in `tools/Pdd/ReconnectDefects.cs`.
- The "no-adoption" rule is in `docs/pdd-phase-3-plan.md` at lines 519 and 520,
  and the "small pure decision" rule in the same file's tooling table at line 238.

What to do:

1. Verify the references.
2. Write the decision as a transition table in
   `verification/reconnect-decision.md`: states × events → next state and allowed
   effect. List the undecided cases, including concurrent subscriptions, which
   the intent defers.
3. From a clean tree, record the before numbers of Experiment 1, item 4:
   - reconnect cases that need a controlled schedule or a real loopback, and
     what each establishes;
   - stateful lines and lock sites in the region;
   - the reconnect suite's run time;
   - seeded-defect detection.

   Keep the commands and raw output under
   `docs/evidence/reconnect-decision/baseline/`.
4. Commit, push and open a draft pull request against `releases/10.3.0.0` with
   the table and the baseline numbers.

Done when:

1. The references are verified, with any differences listed.
2. The transition table is complete, with its undecided cases.
3. The baseline numbers and their commands and output are kept, with the base
   commit recorded.
4. The draft PR is open and the receipt is posted on it.

Estimate, not measured: 200K to 300K tokens.

```text
Run step S5 of the 10.3 session plan in repository 1iveowl/WebsocketClientLite.PCL
(checkout /workspaces/WebsocketClientLite.PCL).

First: git fetch -q origin, then read the plan in full:
git show origin/releases/10.3.0.0:docs/pdd-10.3-session-plan.md, or, if that path
does not exist yet, git show origin/docs/pdd-10.3-session-plan:docs/pdd-10.3-session-plan.md.
Read AGENTS.md and CONTRIBUTING.md; they are binding, as are the plan's "Common
rules for every step" and "Owner decisions". Do step S5 and nothing else. Its
brief is Appendix B: its ground rules and Experiment 1, items 1 and 4 (before
numbers only).

Then: confirm git status is clean and create chore/reconnect-pure-decision from
origin/releases/10.3.0.0. Record the base commit before anything else. Change no
production or test code in this step.

Do the work under "What to do", meet every "Done when" item, push, open the draft
PR against releases/10.3.0.0 with no attribution line, post the receipt on it and
end with the receipt as the Common rules describe.
```

### S6. Experiment 1: pure function, plain tests, after numbers, recommendation

Branch `chore/reconnect-pure-decision`, its draft PR. Brief: Appendix B,
Experiment 1, items 2, 3, 4 (after) and 5, and its "Done means".

What to do:

1. Implement the function exactly as the S5 table defines it. Keep its behaviour
   the same as today's code, including in the undecided cases.
2. Put the production change in its own commit, separate from the report and the
   evidence. The owner can then keep the report even if the change is not
   adopted.
3. Add the plain tests, then record the after numbers under
   `docs/evidence/reconnect-decision/after/`.
4. Seed one defect in the new function and show that the plain tests alone catch
   it. Show that the existing seeded reconnect defect is still detected.
5. If `./scripts/pdd.sh validate` would need a change to a requirement entry
   because a symbol moved, ask before editing it.
6. Write `verification/reconnect-decision.md`'s results and recommendation. Run
   every check named in the brief's "Done means", complete the PR description
   with "Decisions for owner review", and mark the PR ready.

Done when:

1. Production calls the pure function, and the shell only stores what the
   function returns.
2. The plain tests cover every state and event combination and both P3-D
   counterexamples.
3. The before and after numbers are kept with their commands.
4. Both seeded-defect results are reported.
5. The recommendation is made on the repository's criterion.
6. Every check is reported with its result word.
7. The PR is ready for review, and the receipt is posted on it.

Estimate, not measured: 350K to 450K tokens. A hand-off after item 3 is
acceptable.

```text
Run step S6 of the 10.3 session plan in repository 1iveowl/WebsocketClientLite.PCL
(checkout /workspaces/WebsocketClientLite.PCL).

First: git fetch -q origin, then read the plan in full:
git show origin/releases/10.3.0.0:docs/pdd-10.3-session-plan.md, or, if that path
does not exist yet, git show origin/docs/pdd-10.3-session-plan:docs/pdd-10.3-session-plan.md.
Read AGENTS.md and CONTRIBUTING.md; they are binding, as are the plan's "Common
rules for every step" and "Owner decisions". Do step S6 and nothing else. Its
brief is Appendix B: its ground rules, Experiment 1, items 2, 3, 4 (after) and 5,
and its "Done means".

Then: confirm git status is clean, switch to chore/reconnect-pure-decision and
fast-forward it to its origin branch, find its pull request with
gh pr list --head chore/reconnect-pure-decision, and read the description and
every comment, including S5's receipt. Check S6's "Needs" (S5 ended DONE); if it
is not met, end STATUS: BLOCKED.

Do the work under "What to do", meet every "Done when" item, push, post the
receipt on the PR and end with the receipt as the Common rules describe. Never
merge.
```

### S7. Experiment 2 baseline: the boundary of the verified core

Branch `chore/core-boundary-types`, made from `origin/releases/10.3.0.0` after
M1, so that it maps the final `boundaries.json`. Brief: Appendix B, ground rules,
Experiment 2, items 1 and 3 (before). No production or test code changes in this
step.

Verified on 2026-11-11:

- The DafnyRuntime paragraph is in `verification/README.md` at line 211.
- The benchmark command is in `verification/README.md`, section "Checks":
  `DOTNET_TieredCompilation=0 dotnet run --project verification/benchmark/MaskingBenchmark.csproj -c Release`.
- Its method is in "Performance methodology".

What to do:

1. List every call from hand-written C# into the generated core, with the
   preconditions the caller must establish and where it establishes them today.
   Separate decision calls from hot byte loops.
2. Map every `boundaries.json` entry to the calls it covers.
3. From a clean tree, run the masking and reassembly benchmarks as the README
   describes and record the line counts. Keep the commands and raw output under
   `docs/evidence/core-boundary-types/baseline/`. Report them as local
   measurements.
4. Commit, push and open a draft pull request.

Done when:

1. The call inventory is complete.
2. Every `boundaries.json` entry is mapped.
3. The baseline benchmarks and counts are kept, with the base commit recorded.
4. The draft PR is open and the receipt is posted on it.

Estimate, not measured: 200K to 300K tokens.

```text
Run step S7 of the 10.3 session plan in repository 1iveowl/WebsocketClientLite.PCL
(checkout /workspaces/WebsocketClientLite.PCL).

First: git fetch -q origin, then read the plan in full:
git show origin/releases/10.3.0.0:docs/pdd-10.3-session-plan.md, or, if that path
does not exist yet, git show origin/docs/pdd-10.3-session-plan:docs/pdd-10.3-session-plan.md.
Read AGENTS.md and CONTRIBUTING.md; they are binding, as are the plan's "Common
rules for every step" and "Owner decisions". Do step S7 and nothing else. Its
brief is Appendix B: its ground rules and Experiment 2, items 1 and 3 (before
numbers only).

Check S7's "Needs": gh pr view 136 --json state must say MERGED. If not, end
STATUS: BLOCKED. Then confirm git status is clean and create
chore/core-boundary-types from origin/releases/10.3.0.0. Record the base commit
before anything else. Change no production or test code in this step.

Do the work under "What to do", meet every "Done when" item, push, open the draft
PR against releases/10.3.0.0 with no attribution line, post the receipt on it and
end with the receipt as the Common rules describe.
```

### S8. Experiment 2: typed adapter, after numbers, recommendation

Branch `chore/core-boundary-types`, its draft PR. Brief: Appendix B,
Experiment 2, items 2, 3 (after) and 4, and its "Done means".

What to do:

1. Write the adapter for decision calls only. Do not change the Dafny source or
   the generated file. Add no allocation on any per-frame path.
2. Put the production change in its own commit, separate from the report and the
   evidence.
3. For each `boundaries.json` entry, report which part of its assumption the
   types now enforce, which part is unchanged, and which entries are untouched.
   Propose wording changes in the report. Do not edit `boundaries.json`.
4. Rerun the benchmarks the same way and record the after numbers under
   `docs/evidence/core-boundary-types/after/`.
5. Run every check named in the brief's "Done means", write the recommendation,
   complete the PR description with "Decisions for owner review", and mark the
   PR ready.

Done when:

1. An out-of-range state or event cannot be constructed by a caller of the
   adapter. If that is not achievable, the report says so.
2. The `boundaries.json` mapping is reported per entry.
3. The before and after benchmarks are kept as local measurements.
4. The recommendation is made on the repository's criterion.
5. Every check is reported with its result word.
6. The PR is ready for review, and the receipt is posted on it.

Estimate, not measured: 300K to 400K tokens.

```text
Run step S8 of the 10.3 session plan in repository 1iveowl/WebsocketClientLite.PCL
(checkout /workspaces/WebsocketClientLite.PCL).

First: git fetch -q origin, then read the plan in full:
git show origin/releases/10.3.0.0:docs/pdd-10.3-session-plan.md, or, if that path
does not exist yet, git show origin/docs/pdd-10.3-session-plan:docs/pdd-10.3-session-plan.md.
Read AGENTS.md and CONTRIBUTING.md; they are binding, as are the plan's "Common
rules for every step" and "Owner decisions". Do step S8 and nothing else. Its
brief is Appendix B: its ground rules, Experiment 2, items 2, 3 (after) and 4, and
its "Done means".

Then: confirm git status is clean, switch to chore/core-boundary-types and
fast-forward it to its origin branch, find its pull request with
gh pr list --head chore/core-boundary-types, and read the description and every
comment, including S7's receipt. Check S8's "Needs" (S7 ended DONE); if it is not
met, end STATUS: BLOCKED.

Do the work under "What to do", meet every "Done when" item, push, post the
receipt on the PR and end with the receipt as the Common rules describe. Never
merge.
```

### S9. Release readiness

Branch `docs/readme-10.3.0`, made from `origin/releases/10.3.0.0` after M1 to M4.

What to do:

1. List every pull request merged into `releases/10.3.0.0` since `30cf44a`, and
   every one closed without merging.
2. On a clean checkout of the release branch tip, run the build, the five-target
   matrix, the coverage gate, the Pdd tests, `pdd.sh validate`,
   `scripts/dafny-masking.sh --check`, the mutation suite and the four capture
   jobs with `aggregate`. Keep the output under `docs/evidence/release-10.3.0/`.
3. Draft the README section `## New in Version 10.3.0`, following the existing
   sections. It covers only what was merged and states adopted and not-adopted
   experiments plainly.
4. Open a pull request against `releases/10.3.0.0` with the draft and the
   evidence.
5. List the owner's remaining release steps from CONTRIBUTING.md. Do not perform
   them: no `GitVersion.yml` change, no release, no pull request into `main`.

Done when:

1. The merged and closed lists are complete.
2. Every check is reported with its result word.
3. The README draft PR is open and the receipt is posted on it.

Estimate, not measured: 200K to 300K tokens.

```text
Run step S9 of the 10.3 session plan in repository 1iveowl/WebsocketClientLite.PCL
(checkout /workspaces/WebsocketClientLite.PCL).

First: git fetch -q origin, then read the plan in full:
git show origin/releases/10.3.0.0:docs/pdd-10.3-session-plan.md, or, if that path
does not exist yet, git show origin/docs/pdd-10.3-session-plan:docs/pdd-10.3-session-plan.md.
Read AGENTS.md and CONTRIBUTING.md; they are binding, as are the plan's "Common
rules for every step" and "Owner decisions". Do step S9 and nothing else.

Check S9's "Needs": the pull requests of S2, S4, S6 and S8 are each MERGED or
CLOSED (gh pr list --base releases/10.3.0.0 --state all). If one is still open,
end STATUS: BLOCKED. Then confirm git status is clean and create
docs/readme-10.3.0 from origin/releases/10.3.0.0.

Do the work under "What to do", meet every "Done when" item, push, open the PR
against releases/10.3.0.0 with no attribution line, post the receipt on it and
end with the receipt as the Common rules describe. Do not change GitVersion.yml,
release, or open a pull request into main.
```

## Appendix A. Brief: header-length traceability

The owner's brief of 2026-11-11, verbatim. Items A and B are done on
`chore/header-rule-traceability`. The test in item B was strengthened first,
because as written it did not fail on the old parser
(`docs/header-length-validation.md`). Owner decision 3 above answers the "ask
first" for the tooling items D and E need.

```text
Task: close four traceability gaps around the header-length rule, and make RFC
citations complete for the proved protocol core. No behaviour change, no release.

Background
A video series uses this library as its worked example. It follows one rule end
to end: RFC 6455 section 5.2, "the minimal number of bytes MUST be used to encode
the length". The frame 81 7E 00 7D (length 125 in the 16-bit field) must be
rejected. Checking the repo at 30cf44a against what the series says turned up
these gaps:

1. verification/Protocol.dfy contains no RFC citation at all. Only
   verification/Masking.dfy:6 cites one (section 5.3). The intended rule is that
   every contract we prove is tied to a numbered RFC section, recorded next to
   the proof obligation (docs/dafny-pdd-plan.md:17).
2. verification/requirements.json has 14 requirements (Close, reassembly,
   masking, write, lease, epoch). None covers header validation, so the length
   rule has no entry.
3. No mutant in verification/mutations/ targets header code.
4. The rule arrived in one squash commit, c482b0c (#100), together with its
   contract, tests and CI gate. There is no record that the regression test was
   ever seen failing against the old parser.

The rule itself is in place and correct as far as we know: HeaderValid
(Protocol.dfy:26-34, the length clause is lines 31-32), DecodeHeader
(lines 37-46, ending in `status == 2 <==> HeaderValid(...)`), called through
Admit from src/main/WebsocketClientLite/Helper/DataframeParsing.cs:39, tested by
RejectsMalformedFrames in src/main/WebsocketClientLiteTest/PayloadParsingTests.cs
(the frame is `{ 0x81, 126, 0, 125 }`, line 218). Verify these references
yourself before relying on them.

Ground rules
- Read AGENTS.md and CONTRIBUTING.md first and follow them, including branch and
  attribution rules. Work on a branch; open a pull request; do not merge, do not
  touch GitVersion.yml, do not release.
- Do not rewrite git history and do not backdate anything. Everything added here
  is dated today and described as what it is.
- Do not change any `requires`, `ensures`, invariant or predicate body. If
  something only passes with a contract change, stop and report.
- No production behaviour change. If you find a real discrepancy with the RFC,
  stop and report it; do not fix it in this task.
- Follow the repo's existing evidence rules: a timeout is inconclusive, never a
  detection; a failed or interrupted run is kept and reported, not retried until
  green and not relabelled.
- Follow the existing patterns in each file. Do not invent new schema fields or
  new tooling unless a step cannot be done without it, and then ask first.

Work

A. Look for the original evidence first.
   Check whether anything from 7 November 2026 survives that shows the test
   failing before the fix: local reflog, unreachable commits
   (`git fsck --lost-found`), stashes, local branches not on the remote, CI run
   logs for #100. If you find it, do not restore it into history. Report what
   exists and where, and propose how to archive it under docs/evidence/.

B. Record a replay.
   Show that today's test fails on the parser as it was before the proof work,
   the way docs/p3-a-validation.md does for the queued-writer defect (original
   source plus the reduced test, run in a copy, output kept).
   - Baseline: a779602 (merge of release 9.2.0), or c482b0c^ if that is the
     better parent. Say which you used and why.
   - Run the non-minimal length cases from RejectsMalformedFrames against that
     source in a separate copy or worktree, never in the working checkout. Reduce
     the test if the old test project needs it, and keep the reduction minimal.
   - Then run the same cases on the current source and show they pass.
   - Keep the commands, the source identities and the raw output under
     docs/evidence/, and write a short validation note in docs/ in the style of
     the existing ones. Label it clearly as a replay made today, not as a record
     from 7 November.
   - If the old code does not fail these cases, stop and report. That would
     contradict what we believe and matters more than the rest of this task.

C. Cite the RFC beside every proved contract.
   In verification/Protocol.dfy, add a comment next to each contract-bearing
   function, method and lemma naming the RFC 6455 section it implements, in the
   same form as Masking.dfy:6. Where a contract is library policy and not an RFC
   rule (size limits, the Close reservation model and similar), say
   "Library policy" and what it is, as the `origin` fields in requirements.json
   already do. Do not guess: if you cannot tie a contract to a section, mark it
   as unresolved in your report instead of citing something approximate.
   Comments are embedded in the generated C#, so regenerate with the repo's
   script and make sure `scripts/dafny-masking.sh --check` passes.

D. Add the header rule to the requirement map.
   Add requirement entries to verification/requirements.json for header
   validation, at least one for minimal length encoding with section 5.2 as
   origin. Use the existing entry shape (id, intent, origin, contracts,
   production_paths, assumptions, obligations, exclusions) and point at the
   contract, call site and tests that already exist. Add the review obligation
   as the other entries have it; do not mark any human review as done.
   `./scripts/pdd.sh validate` must pass. If header validation rests on an
   assumption that is not in verification/boundaries.json, report it; add an
   entry only if it follows the existing pattern.

E. Add header mutants.
   Add one or two mutants to the catalog that break the minimal-length rule in
   the decoder's implementation, for example accepting a 16-bit length below
   126, or a 64-bit length below 65536. The catalog rule stands: mutate the
   implementation (the body of DecodeHeader or what it calls), never a proof
   clause. HeaderValid is the specification here, so it is not a mutation
   target. Each mutant must fail at the clause the catalog names, in the
   selected method. Run the full mutation suite and report every outcome,
   including any that is inconclusive. If a mutant is not detected, that is a
   finding: stop and report, do not adjust the mutant until it is caught.

F. Report what is still unmapped.
   List every proved contract in Protocol.dfy and Masking.dfy with: its RFC
   section or policy label, whether it has a requirement entry, and whether it
   has a mutant. Do not fill the remaining gaps in this task. The list is the
   deliverable.

Done means
- The verifier result is unchanged apart from anything the new mutants add; give
  the exact last line of the verifier output.
- `./scripts/pdd.sh validate`, the Pdd tests, `scripts/dafny-masking.sh --check`,
  the mutation suite and the test matrix were each run. For each, say run and
  passed, run and failed, inconclusive, or not run. Do not summarise a check you
  did not run as passing.
- A pull request with: the replay note and evidence, the citations and
  regenerated C#, the requirement entries, the mutants, and the table from F.
- A section "Decisions for owner review" listing anything you were unsure of,
  anything you stopped on, and any place where the repo's documents claim more
  than the repo shows.
```

## Appendix B. Brief: two boundary experiments

The owner's brief of 2026-11-11, verbatim. Experiment 1 is steps S5 and S6,
Experiment 2 is steps S7 and S8. The quotation "A justified no-adoption decision
is a successful deliverable" is in `docs/pdd-phase-3-plan.md`, lines 519 and 520.

```text
Task: two small experiments on whether moving decisions out of stateful code,
and typing the boundary of the verified core, makes evidence cheaper. Each may
end in "not adopted". The measurements are the deliverable either way.

Background
The library has a Dafny-verified protocol core called from hand-written C#.
The C# around the core holds the state, the locks, the sockets and the Rx
pipeline, and it is tested with controlled schedules, seeded defects and
Stryker. We want to know, with numbers, whether two changes reduce what has to
be tested that expensive way. The repo's own rule applies: "A justified
no-adoption decision is a successful deliverable" (docs/pdd-phase-3-plan.md),
and more modelling is "worth considering only for a small pure decision
actually called by production" (same file, the tooling table).

Ground rules
- Read AGENTS.md and CONTRIBUTING.md first and follow them, including branch and
  attribution rules. One branch and one pull request per experiment. Do not
  merge, do not touch GitVersion.yml, do not release.
- No change to public API, public behaviour or the Dafny contracts. No new
  production dependency.
- Do not redesign the Rx pipeline. Rx stays, as it is used today.
- Existing tests, controlled schedules, seeded-defect checks and gates must
  still pass unchanged. Do not delete or weaken a test to make a number look
  better. If an existing test becomes redundant, say so in the report and leave
  it in place.
- Evidence rules as elsewhere in this repo: a timeout is inconclusive, a failed
  run is kept and reported, nothing is retried until green.
- Record the baseline numbers BEFORE changing any code, from a clean checkout,
  and keep the commands and output.

Experiment 1: the reconnect lifecycle decision as a pure function

Today src/main/WebsocketClientLite/ClientWebSocketRx.cs decides admission,
registration and the final "not connected" notification with the fields
_disposed and _generation under _lifecycleGate, at several lock sites (around
lines 251 to 303, and in Dispose and DisposeAsync). This is the P3-D fix
(verification/reconnect-intent.md, docs/p3-d-validation.md). Verify these
references yourself.

1. Write down the decision as a transition: given the current lifecycle state
   and an event (subscribe, registered, terminal from a given generation,
   dispose), what is the next state and what may happen (admit, reject with
   ObjectDisposedException, notify not-connected, do nothing). Take the intended
   behaviour from reconnect-intent.md. Do not change it. Where the intent is
   silent (it explicitly defers concurrent subscriptions), keep today's
   behaviour and list the case as undecided.
2. Implement it as one pure function in C#: immutable input, a returned value,
   no fields, no locks, no I/O, no Rx types. Production must call it. The shell
   keeps the lock and the fields and only stores what the function returns.
   Whether to also express it in Dafny is NOT part of this experiment; note in
   the report whether it would be a natural next step.
3. Test the function with plain tests: every state and event combination, and
   the two P3-D counterexamples expressed as event sequences. No schedule, no
   socket, no clock.
4. Measure, before and after:
   - how many reconnect cases need a controlled schedule or a real loopback to
     establish what they establish, and how many of those are now also covered
     by a plain test of the decision;
   - what the remaining stateful code still has to guarantee that the plain
     tests cannot show (the lock is held, the function is called at the right
     points, cleanup runs). Be exact: this is the part that stays expensive;
   - lines of stateful code in the affected region, number of lock sites,
     test run time for the reconnect suite;
   - whether the seeded reconnect defect (tools/Pdd ReconnectDefects) is still
     detected, and whether a seeded defect in the new function is caught by the
     plain tests alone.
5. Recommend adopt or not adopt, on the repo's criterion: additional assurance
   per unit of maintained machinery. If the pure function ends up as a thin
   wrapper that mirrors the lock sites one to one and removes nothing, say so
   and recommend against.

Experiment 2: named types at the boundary of the verified core

The generated core takes and returns raw codes (byte status 0/1/2, byte state
0/1/2/8, close-state bits, event 0..2) and compiled Dafny does not check its
preconditions at run time (verification/README.md, the DafnyRuntime paragraph).
Several entries in verification/boundaries.json are in effect "the caller
passes a legal value", for example CLOSE-LOCK ("events are 0..2"),
REASSEMBLY-TOTALS and MASK-ADAPTER.

1. List every call from hand-written C# into the generated core, with the
   preconditions the caller must establish and where it establishes them today.
2. For the decision calls only (not the hot byte loops), introduce a thin,
   hand-written, internal adapter with named types: enums or small readonly
   structs for states, events and results, so that an out-of-range state or
   event cannot be constructed by a caller. Do not change the Dafny source or
   the generated file. Do not add allocation on any per-frame path.
3. Measure, before and after:
   - for each boundaries.json entry, which part of its assumption is now
     enforced by the type system, which part is unchanged, and which entries
     are untouched. Do not edit boundaries.json; propose the wording change in
     the report;
   - the masking and reassembly benchmarks in verification/benchmark, run as
     the README describes, to show the hot path is unchanged. These are local
     measurements; report them as such;
   - lines added, and whether any existing test became redundant.
4. Recommend adopt or not adopt, on the same criterion. If the adapter only
   renames numbers without making an illegal call unrepresentable, say so and
   recommend against.

Done means, for each experiment
- A pull request with the change, the plain tests, the baseline and after
  numbers with the commands that produced them, and the recommendation.
- For every check (build, five-target test matrix, coverage gate, verifier and
  generated-code check, Dafny mutations, controlled and seeded-defect checks,
  Stryker): run and passed, run and failed, inconclusive, or not run. Do not
  summarise a check you did not run as passing.
- A section "Decisions for owner review": anything undecided in the intent,
  anything you stopped on, and anything where the result is weaker than this
  brief hoped for.
```

Note for S8: C# enums do not prevent out-of-range values, because any integer
can be cast to an enum type. A readonly struct with a private constructor and
named static instances does prevent them. The brief's test, "an out-of-range
state or event cannot be constructed by a caller", is the bar.

## Appendix C. Test parity detail

Written 2026-11-11 after replay 2 (`docs/header-length-validation.md`). Line
references are to `releases/10.3.0.0` at `30cf44a`.

### W1. Tests that can pass on end of stream

A test that sends only a header and expects `WebsocketClientLiteException` also
passes when the parser accepts the header and then runs out of bytes. Replay 2
shows that the other rows of `RejectsMalformedFrames` already discriminate on
`a779602`, except the high-bit row. By reading only, to be confirmed by S3:

| Test | Input | Suspected weakness |
| --- | --- | --- |
| `Parse_FrameExceedingMaxFrameSize_Throws` | `82 7E 00 C8`, limit 100, no payload | Would still pass if the size check were removed |
| `Parse_OversizedControlFrame_Throws` | `89 7E 00 C8`, no payload | Would still pass if the control length check were removed |
| `RejectsMalformedFrames`, high-bit row | `81 7F 80 00 …` | Cannot be told apart from the size limit, because any length with bit 63 set exceeds `int.MaxValue`; keep it as combined coverage and say so in a comment |

`TruncatedHeadersNeverProduceFrames` expects end of stream on purpose and is not
affected. `a779602` already had the size and control length checks, so the
failing-first run for these uses an isolated copy of the current source with only
that check removed, as the P3-A assertion challenges do.

### W2. Tests at parity with what the documents claim

The claims are in:

- `docs/protocol-verification.md`, line 120;
- `verification/README.md`, line 176;
- `docs/dafny-pdd-plan.md`, line 57.

| # | Claim | Existing test | Gap | Step and proposed test |
| --- | --- | --- | --- | --- |
| 1 | Non-minimal lengths are rejected | `RejectsMalformedFrames`, two rows | Closed in #136 | None |
| 2 | Invalid headers fail before the payload is read or allocated | `RejectsSequenceBeforeAttemptingPayloadRead`, `HugeLengthsRejectOnHeaderOnly` | No header-validity row proves that no payload byte was read | S3: a theory over every invalid header row with the counting stream of `ParserRetentionTests`: bytes read equal the header size |
| 3 | Masked server frames are rejected | `RejectsMaskedServerFrame` | Covered; not checked for read order | S3: include its row in item 2 |
| 4 | Unsupported RSV bits and opcodes are rejected | RSV1 and opcode 3 only | RSV2, RSV3, opcodes 4 to 7 and 11 to 15 untested | S4: extend the rows (owner decision 4) |
| 5 | Invalid control frames are rejected | Oversized and fragmented control tests | Oversized test may pass on end of stream | S3, as W1 |
| 6 | The configured limit and its successor | `Parse_FrameExceedingMaxFrameSize_Throws` uses 200 against 100 | No boundary case | S4: limit L accepted; L + 1 rejected from the header |
| 7 | Every header truncation | `TruncatedHeadersNeverProduceFrames`, 64-bit header only | 16-bit header truncation untested | S4: add a 16-bit header |
| 8 | Boundaries 0, 125, 126, 65535, 65536 decode | `ParsesCanonicalServerLengths` | None | None |
