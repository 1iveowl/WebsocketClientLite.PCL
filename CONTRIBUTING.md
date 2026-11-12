# Contributing

Thank you for helping improve WebsocketClientLite.PCL. Bug reports, questions and pull requests are welcome.

## Reporting a bug

Open an [issue](https://github.com/1iveowl/WebsocketClientLite.PCL/issues) with the package version, the target framework, the server you connect to (if you can share it), and the smallest code that shows the problem.

## Sending a pull request

1. Fork the repository and create a branch for your change.
2. Build and test as described in [docs/development.md](docs/development.md): `dotnet build src/main/WebsocketClientLite.sln` and `./test-all-tfms.sh`. The repository's [dev container](docs/development.md#dev-container) has everything installed.
3. Keep the change compatible with every target framework (`netstandard2.0`, `netstandard2.1`, `net8.0`, `net9.0`, `net10.0`) and add or update tests. Local validation and release CI require at least 70 % line coverage. Run the [full local evidence checks](verification/README.md#ci-verification-receipts-phase-2-increment-d) before merging; routine pushes and pull requests into release branches do not run GitHub CI.
4. Open the pull request against `main`. The maintainer moves it onto the release it will ship in.

Commit messages start with a short prefix: `feat:`, `fix:`, `docs:` or `chore:`.

## How changes become releases

This part describes the maintainer's workflow, so you know where your change goes.

```
main ─────────●────────────────────────────●──▶  latest release
               \                          /  pull request, merge commit
releases/9.2.0  ●───────●───────●────────●  ← version bump in GitVersion.yml publishes
                 \     / \     /
feat/…, fix/…     ●───●   ●───●   one task per branch, squash-merged pull requests
```

- `main` always holds the latest release. Changes reach it only from a release branch.
- The next version is developed on `releases/<x.y.z>`, made from `main`: a new minor version for features, a new patch version for fixes only.
- Each task is a topic branch from the release branch (`feat/…`, `fix/…`, `docs/…`, `chore/…`), merged into it through a pull request. GitHub CI runs for release preparation, on pushes or pull requests to `releases/**` that change `GitVersion.yml` (matching the publish workflow), and on every pull request into `main`, which requires its `build-and-test` check. Manual CI dispatch remains available for release validation. Run CI locally for ordinary development.
- A release is three steps on the release branch:
  1. Add the `## New in Version <x.y.z>` section at the top of `CHANGELOG.md`, with the full three-part version in the heading, and a one-line row for it in the README's "Release history" table. The publish workflow uses that section as the GitHub release text and stops before publishing if it is missing.
  2. Set `next-version` in `GitVersion.yml`, directly on the release branch. Pushing that change runs the publish workflow: it pushes the packages to NuGet.org and GitHub Packages and creates the GitHub release with its version tag. A pull request into a release branch that changes `GitVersion.yml` starts the same workflow, so the version never changes in a topic branch.
  3. Merge the release branch into `main` through a pull request.
- A release branch is closed once published. Later work goes to the next release branch, and a fix for a published version gets a new patch branch from `main`.
