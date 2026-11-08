# Development

How to build and test WebsocketClientLite.PCL, and the optional ways to work on it: in a dev container, in a dev container on a remote Linux host, and with Herdr for running coding agents side by side.

- [Build and test on your machine](#build-and-test-on-your-machine)
- [Dev container](#dev-container)
- [Dev container on a remote Linux host](#dev-container-on-a-remote-linux-host)
- [Herdr](#herdr)
- [Troubleshooting](#troubleshooting)

## Build and test on your machine

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and git. The .NET 10 SDK builds every target framework (`netstandard2.0`, `netstandard2.1`, `net8.0`, `net9.0`, `net10.0`), and the tests run on the .NET 10 runtime. You do not need the older SDKs.

```bash
git clone https://github.com/1iveowl/WebsocketClientLite.PCL.git
cd WebsocketClientLite.PCL
dotnet build src/main/WebsocketClientLite.sln
./test-all-tfms.sh            # the tests against every target framework build, Debug
./test-all-tfms.sh Release    # the same in Release, as CI runs it
```

Run the [full local evidence checks](../verification/README.md#ci-verification-receipts-phase-2-increment-d) before merging routine changes. GitHub CI is reserved for releases, with manual dispatch available for release validation.

The scripts need bash: macOS, Linux, WSL or Git Bash.

The test project targets `net10.0` and links one library build at a time through the `LibTfm` property, so a single runtime exercises each build's `#if` code paths. To test one build:

```bash
dotnet test src/main/WebsocketClientLiteTest/WebsocketClientLiteTest.csproj -p:LibTfm=netstandard2.0
```

Coverage as CI checks it (at least 70 % of lines):

```bash
dotnet test src/main/WebsocketClientLiteTest/WebsocketClientLiteTest.csproj -c Release \
  --collect:"XPlat Code Coverage" --results-directory ./coverage
./scripts/check-coverage.sh ./coverage 70
```

| Path | What it is |
|---|---|
| `src/main/WebsocketClientLite` | The library (`WebsocketClientLite.PCL` package) |
| `src/interface/IWebsocketClientLite` | Its interfaces, published as the separate `IWebsocketClientLite` package |
| `src/main/WebsocketClientLiteTest` | Unit tests |
| `src/test/NETCore.Console.Test` | A console sample that connects to public echo servers |
| `.github/workflows` | `ci.yml` runs release checks and `build.yml` publishes packages when `GitVersion.yml` changes in a push or pull request to `releases/**`; ordinary changes are validated locally |

## Dev container

The repository has a [dev container](https://containers.dev/) with everything already installed:

| Tool | Notes |
|---|---|
| .NET 10 SDK | Builds and tests every target framework, as above |
| Node.js LTS, GitHub CLI, Azure CLI | `gh` and `az` logins survive a rebuild |
| Claude Code, Codex | CLIs and VS Code extensions, plus GitHub Copilot and Copilot Chat extensions |
| [Herdr](#herdr) | A terminal workspace for running several agents side by side |
| [TokenEconomizer](#tokeneconomizer) | A Claude Code and Codex plugin that lowers what agent sessions cost |
| C# Dev Kit | For VS Code |

### Open it

You need Docker (Docker Desktop, OrbStack, Colima or Docker Engine) and VS Code with the [Dev Containers](https://marketplace.visualstudio.com/items?itemName=ms-vscode-remote.remote-containers) extension. On Windows, clone and open the repository from WSL.

1. Clone the repository and open `WebsocketClientLite.PCL.code-workspace` (or the folder) in VS Code.
2. Choose **Reopen in Container** when VS Code offers it, or run **Dev Containers: Reopen in Container**.

The first build takes a few minutes; the setup script then restores the solution. After that, File > Open Recent goes straight into the container. Change anything under `.devcontainer/` and you need **Dev Containers: Rebuild Container**; reopening is not enough.

Without VS Code, the [devcontainer CLI](https://github.com/devcontainers/cli) works as well:

```bash
npm install -g @devcontainers/cli
devcontainer up --workspace-folder .
devcontainer exec --workspace-folder . ./test-all-tfms.sh
```

### What stays across rebuilds

Named Docker volumes keep the agents' logins and history, Herdr's layout, what TokenEconomizer has learned, and the `gh` and `az` logins: `claude-home-<folder>`, `codex-home-<folder>`, `copilot-home-<folder>`, `herdr-config-<folder>`, `gh-config-<folder>`, `azure-config-<folder>` and `tokeneconomizer-<folder>`, where `<folder>` is the checkout's folder name. Two checkouts with the same folder name share them. Remove the volumes to start from scratch.

### Coding agents run without permission prompts

The container is set up as a trusted sandbox. Claude Code starts in `bypassPermissions` mode, and Codex runs with `approval_policy = "never"` and `sandbox_mode = "danger-full-access"`. The agents can edit the checkout, which is your real working copy mounted from the host, and run any command in the container, using any credentials you sign in with there. If you do not want that, remove those settings from `.devcontainer/post-create.sh` and `.devcontainer/devcontainer.json` before you build the container.

In a terminal, `yolo` starts Claude Code and `yolo-codex` starts Codex without permission prompts. They are shell aliases that the setup script adds to `~/.bashrc`, for `claude --dangerously-skip-permissions` and `codex --dangerously-bypass-approvals-and-sandbox`.

`ANTHROPIC_API_KEY` and `OPENAI_API_KEY` are passed into the container when they are set on the host. Otherwise sign in inside the container. [AGENTS.md](../AGENTS.md) holds the rules every agent follows in this repository.

### TokenEconomizer

The setup script installs [TokenEconomizer](https://github.com/microsoft/TokenEconomizer) as a Claude Code and a Codex plugin. It learns from earlier sessions in this repository: it gives new sessions the repository facts they kept looking up, leaves out tools that go unused, and redirects a turn that is stuck repeating a failure. It needs no API key. The clone it runs from is in `~/.local/share/TokenEconomizer`, pinned to a commit in `.devcontainer/post-create.sh`; to update it, review the upstream changes and move the pin.

Two steps are left to you after the first build:

- **Claude Code:** in a session, run `/tokeneconomizer:toolprune refresh-catalog` once, and again after Claude Code updates. Without it, tool pruning sees only the deferred tools. `/tokeneconomizer:status` shows that the plugin is loaded.
- **Codex:** start `codex`, choose **Trust all and continue** when it shows **Hooks need review** (or approve them later with `/hooks`), and start it again; the hooks run from the next session. Check it with `$tokeneconomizer:status`.

When it leaves tools out, it writes deny rules to `.claude/settings.local.json` and keeps that file out of git; `/tokeneconomizer:toolprune restore` gives them back. Set `TE_DISABLE=1` to turn it off for one session. The rest of its commands and settings are in its [plugin README](https://github.com/microsoft/TokenEconomizer/blob/main/plugin/README.md).

### Optional shared skills folder

The container mounts `~/GitHub/ai-skill` from the host at `/opt/ai-skill`. If that folder holds a collection of agent skills with an installer at `scripts/install-macos.sh`, the setup script links them into the agents' homes. If you have no such folder, the container creates it empty on the host before it starts, and the setup skips it. You can ignore it.

### Git performance

The checkout is bind-mounted from the host, so host and container share one `.git/index`. The setup script sets `core.checkStat=minimal` and `core.trustctime=false` (globally, inside the container) and enables the untracked cache where it works. Without them, `git status` re-reads every tracked file on each run.

## Dev container on a remote Linux host

The same dev container runs on a Linux machine that you reach over SSH: a home server, a mini PC or a cloud VM. Builds use that machine's CPU, and agents keep working while your laptop sleeps or travels.

The host needs Docker Engine (with your user in the `docker` group), git and an SSH server. It needs no .NET; everything lives in the container. A private network such as [Tailscale](https://tailscale.com/) makes it reachable from anywhere without opening ports.

1. Clone the repository on the host. The folder name decides the volume names.
2. In VS Code, install the **Remote - SSH** extension, run **Remote-SSH: Connect to Host...** and open the clone.
3. Choose **Reopen in Container**. The container, its volumes and the shared skills folder are on the host; `~` in `devcontainer.json` means the host user's home folder.

Optionally build the container on the host first, so VS Code only attaches: `npm install -g @devcontainers/cli` there, then `devcontainer up --workspace-folder .` in the clone.

To reach the container from a terminal without VS Code (replace the host and the clone's path):

```bash
ssh -t me@devbox 'docker exec -it -u vscode -w /workspaces/WebsocketClientLite.PCL \
  $(docker ps -q --filter label=devcontainer.local_folder=/home/me/src/WebsocketClientLite.PCL) bash -l'
```

## Herdr

[Herdr](https://herdr.dev/) runs several coding agents side by side in one window, and keeps them running when you close it. It comes in two forms, which do not share sessions.

### The herdr terminal UI, inside the container

The container installs `herdr`, with its Claude Code and Codex integrations, so a restarted Herdr can resume their conversations. Open a terminal in the container and run:

```bash
herdr
```

The prefix key is Ctrl+B; the dev container's VS Code settings pass it to the terminal instead of toggling the sidebar. Ctrl+B then `q` detaches and leaves everything running, and `herdr` reattaches. Herdr's layout lives in the `herdr-config-<folder>` volume. A rebuild ends the processes in it.

On a remote host, attach from any terminal (replace `bash -l` in the command above with `bash -lc herdr`).

### The Herdr desktop app

The desktop app runs on herdr-core (`hc`), and adds machines that it reaches over SSH. The dev container has no SSH server, so `scripts/herdr-devcontainer-via.sh` connects through `docker exec` instead. With `--ssh`, it first goes to a remote Docker host. Install the app, let it install `hc` and `herdr-app`, start the dev container once, and then add it as a machine:

```bash
# Docker on this machine (the path is your checkout)
hc machine add --name wscl --install \
  --via "$PWD/scripts/herdr-devcontainer-via.sh $PWD"

# Docker on a remote host (the second path is the clone on that host)
hc machine add --name wscl-devbox --install \
  --via "$PWD/scripts/herdr-devcontainer-via.sh --ssh me@devbox /home/me/src/WebsocketClientLite.PCL"
```

`hc` copies itself into the container, and the machine appears in the app's sidebar. Start a Space on it with the sidebar's **+** next to the machine, or:

```bash
herdr-app do space.new --name WebsocketClientLite.PCL --machine wscl --folder /workspaces/WebsocketClientLite.PCL
```

Terminals and agents in that Space run inside the container as `vscode`. Some details:

- Machine names allow only lower-case letters, digits, `-`, `_` and `.`, up to 32 characters.
- The helper's paths cannot contain spaces, because `hc` splits the `--via` command on them.
- The helper starts a stopped container when the Docker host has the devcontainer CLI.
- A rebuild removes `hc` from the container. Run `hc machine update wscl` afterwards.

## Troubleshooting

**`bind source path does not exist: .../GitHub/ai-skill`.** The container's `initializeCommand` creates that folder. A checkout from before it was added, or a host without `mkdir -p` (Windows outside WSL), fails here. Create the folder by hand, or open the repository from WSL.

**VS Code shows hundreds of changed files while `git status` is clean.** It found a second repository, usually an accidental `git init` in a git-ignored folder. Find it with `find . -name .git -not -path './.git/*'`.

**Changes to `.devcontainer/` have no effect.** Run **Dev Containers: Rebuild Container**.

**Builds on the host and in the container interfere.** Both write the same `bin/` and `obj/` folders. Build in one place, or run `dotnet clean` when you switch.

## Dafny protocol verification

The [protocol verification guide](../verification/README.md) records the contracts,
C# trust boundaries, pinned toolchain, regeneration commands, tests and performance
methodology. Run `./scripts/dafny-masking.sh --check` after changing either Dafny
source or the generated code; the historical script name now covers the full core.

## Actual runtime and OS qualification

`LibTfm` selects a library build. It does not change the test process runtime.
The existing test project also accepts `TestHostTfm`, defaulting to `net10.0`:

```bash
dotnet test src/main/WebsocketClientLiteTest/WebsocketClientLiteTest.csproj -c Release \
  -p:TestHostTfm=net8.0 -p:LibTfm=net8.0
```

Install the matching runtime; major roll-forward cannot count as qualification.
The [P3-E record](p3-e-validation.md) lists the manually qualified OS/runtime
cells, exact source and assembly identities, negative fixtures, costs and
reproduction commands. `test-host` and `aggregate --host-cells` use the existing
PDD tool to capture and check those cells. The focused host receipt does not
replace the full local four-job aggregate.

The CI workflow's manual `host_qualification=true` input runs two fresh jobs per
selected .NET 10 OS without repeating proof or mutation tools. The default remains
full release validation. Windows mandatory promotion and macOS weekly scheduling
are deferred under the release-only CI policy; .NET 8/9 checks are transitional,
not an ongoing support promise. Every published library target is retained.
