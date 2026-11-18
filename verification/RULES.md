# Rules for verification work

These rules hold for every change to the Dafny contracts, the [requirement map](requirements.json),
the [boundary register](boundaries.json), the mutation catalogs and the tests that check the protocol
core, whoever makes it. Rules 4 to 9 are about writing those tests, and also hold for tests of the
boundary register's owners and of the asynchronous lifecycle. They state how tests are written from
now on, not how every earlier test was written. The
[write-protocol-test skill](../.claude/skills/write-protocol-test/SKILL.md) is the procedure that
applies them to one test.

1. **The RFC wins.** When a requirement is in question, RFC 6455 decides: not an agent's reading of
   it, and not the owner's. Each proved contract names the RFC section it comes from, or says
   "Library policy:" and what the policy is; `./scripts/pdd.sh validate` fails when one is missing
   ([tools/Pdd/README.md](../tools/Pdd/README.md)). Library policy covers choices the RFC leaves to the
   implementation, such as size limits, and never overrides a MUST. Where the code, a contract or a
   test disagrees with the RFC, the disagreement is written down and resolved in the RFC's favour.
   The RFC's errata are reviewed before a contract is frozen ([adoption plan](../docs/dafny-pdd-plan.md));
   a reported erratum is named beside the contract it concerns, as for the Close reason in `CloseValid`.
   The one deliberate departure is opt-in and outside the proved core: `ExcludeZeroApplicationDataInPong`
   sends the one-byte empty Pong that Slack's RTM API expects (see the
   [README](../README.md#slack-rtm-api)), with no RFC framing guarantee.
2. **Expected bytes never come from production code.** A test takes its expected bytes from the RFC,
   such as the masked "Hello" of section 5.7 in `Decode_MatchesRfc6455HelloVector`, writes them out
   by hand, or computes them in the test from the RFC's rule, as `Decode_PreservesScratchKeyAndRoundTrips`
   does with `b ^ key[i % 4]` from section 5.3. It never computes them with production helpers, which
   would let the code check itself. A verified reference such as the generated `MergeScalar` may be
   compared beside independent bytes, never instead of them
   (`BulkMergeMatchesScalarProofAndIndependentConcatenation`). This was a plan rule for phase 3
   ([`docs/pdd-phase-3-plan.md`](../docs/pdd-phase-3-plan.md), lines 612 and 613: "Keep independent
   expected bytes; do not let production helpers calculate their own oracle") and is now a standing
   rule.
3. **See a new test fail on the old code.** Before the fix that makes a new test pass is trusted, the
   test is run against the code before the fix and seen to fail, for the reason the test names. A test
   that passes on the old code, or fails there for another reason, does not show the fix. The failing
   run is recorded: the commit it ran at, the command, the failure line, and any change the test needed
   to run on the old code. The
   [header length replay](../docs/header-length-validation.md) shows why: the test for non-minimal
   lengths first passed on the 9.2.0 parser for the wrong reason. That parser accepted the header and
   then failed on the missing payload, with the exception type the test expected.
4. **See a new test of existing behaviour fail on a copy without it.** For a fix, rule 3 holds. A new
   test of behaviour the code already has is run against an isolated copy of the source
   (`git archive <commit> | tar -x` into a scratch directory, never the working checkout) in which the
   one check the test names is removed or shifted, and it must fail there on its named assertion. The
   run is recorded: the copy's commit, the edit as a diff, the command and the failure line. Such an
   edit is a hand-written change to the C# and shows that the test detects one missing check; it is not
   a Dafny mutant. Some rules can only be removed together with another rule that rejects the same
   input, such as the bit-63 rule and the size limit, so no test shows either alone; that is written
   beside the test as combined coverage ([header test parity](../docs/header-test-parity-validation.md),
   "Method" and "Limits"; the P3-A negative experiment in
   [`docs/pdd-phase-3-plan.md`](../docs/pdd-phase-3-plan.md)).
5. **Send complete inputs, not error messages.** A rejection test sends everything an accepting
   implementation would need, such as the payload a header announces, so that an implementation that
   accepts the input returns a result and fails the test on its own. Checking that an error is not
   some other message holds only until the message is reworded. Production text that a test does
   compare against is an internal constant both sides use, such as
   `TcpConnectionService.AbortedUnexpectedlyMessage`. Lessons 5 and 6 of the
   [10.3 session plan](../docs/pdd-10.3-session-plan.md) show why: a header sent without its payload
   also passes when the parser accepts the header and then reaches the end of the stream.
6. **No sleep establishes ordering.** A test forces asynchronous order with explicit signals, entry
   and release points such as a `TaskCompletionSource`, and releases them in the order it chooses. A
   test is never fixed by assuming FIFO: `SemaphoreSlim` and the library's gates promise no queue
   order. A bounded wait is a diagnostic limit, not a result: a missing settlement is reported as a
   controlled progress failure only when the schedule forced the order, and otherwise it is a timeout,
   which rule 8 calls inconclusive. A forced schedule shows the orders it forces, not every order
   ([ControlledCloseTests.cs](../src/main/WebsocketClientLiteTest/ControlledCloseTests.cs), line 18;
   P3-A in [`docs/pdd-phase-3-plan.md`](../docs/pdd-phase-3-plan.md);
   [queued-write-ownership.md](queued-write-ownership.md)).
7. **Reproduce a suspected defect before calling it a defect.** The test that is meant to show the
   defect fails on the unchanged code first, for the reason it names, and that run is recorded as
   rule 3 asks. The fix is a separate, scoped commit, without a broad redesign, and the same test then
   passes. Until the reproduction exists, the finding is reported as a suspicion. The P3-A transport
   abort defect shows the order: the reduced test failed on the release source with `Stuck owner`, and
   the scoped fix in `b4462c7` then made it pass ([p3-a-validation.md](../docs/p3-a-validation.md),
   "Reproduction and intermediate attempts"; session P3-A in
   [`docs/pdd-phase-3-plan.md`](../docs/pdd-phase-3-plan.md): "Reproduce suspected defects before
   claiming them").
8. **Report results as they are.** A timeout is inconclusive, never a pass or a detection. A failed or
   interrupted run is kept and reported, never retried until green and never relabelled. Each check is
   reported as run and passed, run and failed, inconclusive, or not run, and a check that was not run
   is never summarised as passing. These were common rules for every step of the
   [10.3 session plan](../docs/pdd-10.3-session-plan.md) ("Evidence" and "Check results").
9. **Intent is the owner's.** When a test would need weaker expected behaviour, a changed contract, or
   a decision that the RFC and the documents leave open, the agent stops and asks instead of choosing,
   and does not weaken the test to make it pass. Intent is written down before the tests that check
   it, as in [reconnect-intent.md](reconnect-intent.md), and a review of wording does not freeze intent
   on the owner's behalf ([close-intent-review.md](close-intent-review.md)). A small choice that stays
   inside the task and is easy to reverse is made and listed for owner review ("Decide or ask" in the
   [10.3 session plan](../docs/pdd-10.3-session-plan.md)). Where the RFC decides, rule 1 holds and the
   question is not one of intent.
