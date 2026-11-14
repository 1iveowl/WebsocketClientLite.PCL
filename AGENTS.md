# Repository guidance for AI agents

This file is the canonical guidance for every AI coding assistant working in this
repository — Claude Code, Codex, GitHub Copilot, and any other agent. `CLAUDE.md`
points here so the rules do not drift.

## Attribution: an agent is never an author

Everything an agent produces here is authored by the person who asked for it. An
agent must never attribute any part of that work to itself, in git or anywhere the
history is read.

- Never add a `Co-Authored-By:` / `Co-authored-by:` trailer naming an AI agent,
  model, or vendor to a commit message.
- Never add a "generated with", 🤖, or tool-advertisement line to a commit message,
  a pull request title, or a pull request body.
- Never set `user.name`, `user.email`, `GIT_AUTHOR_*`, or `GIT_COMMITTER_*` to an
  agent identity. Commit and push with the repository user's own git identity.
- Never name an agent as an author in release notes, changelog entries, or code
  comments describing who wrote something.
- This applies equally to amended commits, squashes, rebases, cherry-picks, and to
  any commit created by a script or workflow an agent writes.

The requirement is about the record, not about a single command: `git shortlog -sne`
and this repository's contributor list must show people only.

Before pushing, confirm nothing was added:

```bash
# The commits about to be pushed; use -n 20 instead when the branch has no upstream yet.
git log '@{upstream}..HEAD' --format='%an <%ae>'
git log '@{upstream}..HEAD' --format='%B' | grep -Ei 'co-authored-by|generated with|🤖'
```

The first command must show human identities only; the second must print nothing.

If either one flags something, rewrite the offending commit rather than adding a
follow-up commit — a trailer already pushed is part of the history the rule exists
to keep clean.

## Herdr sessions

The dev container installs Herdr and its version-matched control skill. Before
controlling panes, require `HERDR_ENV=1` and read that skill. An agent outside
Herdr must not control the focused session. Use explicit returned pane IDs or
unique agent names, preserve the working directory and avoid stealing focus.

Do not stop the Herdr server, rebuild the container or update agent CLIs while
an agent chain is running. Do not close or send input to panes you did not
create unless the owner explicitly requests it. The human handles startup trust and permission dialogs. Detaching a client keeps
processes alive; restoring conversations after a rebuild does not restore an
interrupted turn or check. Verify task state before continuing.

## Branches and releases

The full process is in [CONTRIBUTING.md](CONTRIBUTING.md). What an agent must follow:

- Work on a topic branch made from the open release branch (`releases/<x.y.z>`; ask the
  owner which one if more than one exists), named `feat/…`, `fix/…`, `docs/…` or `chore/…`.
  One task per branch. Never commit to `main`, and never commit to a release branch
  unless the owner asks for it.
- Start each commit message with the matching prefix (`feat:`, `fix:`, `docs:`, `chore:`).
- Never change `GitVersion.yml`: pushing that change to a release branch, or opening a
  pull request into one with it, publishes the packages to NuGet. Releasing is the
  owner's step.
- Never push to `main`, force-push, or delete a branch unless the owner asks for it.
- Agents working in parallel use separate git worktrees, never the same checkout.

## Project-specific guidance

- Build: `dotnet build src/main/WebsocketClientLite.sln`. The .NET 11 SDK builds every
  target framework (`netstandard2.0`, `netstandard2.1`, `net8.0`, `net9.0`, `net10.0`, `net11.0`).
- Test: `./test-all-tfms.sh` runs the unit tests against each library build; CI runs it in
  Release and requires at least 70 % line coverage (`scripts/check-coverage.sh`).
- Keep changes compatible with every target framework; conditional code uses `#if`.
- The README documents the public API and has one row per release in its "Release history" table. The release notes are in `CHANGELOG.md`, one `## New in Version <x.y.z>` section per release.
- Layout, the dev container, remote hosts and Herdr: [docs/development.md](docs/development.md).
