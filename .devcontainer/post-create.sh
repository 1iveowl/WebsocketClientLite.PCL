#!/usr/bin/env bash
set -euo pipefail

solution_path="src/main/WebsocketClientLite.sln"
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# Git on a bind-mounted checkout. See references/pattern.md, "Git status is slow on a bind-mounted checkout".
# The one .git/index is read by host git and by container git. Git caches dev/ino/uid/gid per entry, those differ
# between the two views, so every entry looks dirty and git re-reads and hashes every tracked file on each status.
# checkStat=minimal compares only mtime, size and mode, which is stable across both views. Global on purpose: it is
# a property of the mount, not of one repository, so worktrees and any nested clone inherit it.
git config --global core.checkStat minimal
git config --global core.trustctime false

# The untracked-file walk is the other half of a slow status. Enable the cache only where git confirms that this
# filesystem reports directory mtime reliably; a false positive here would make git miss new files.
if git -C "${repo_root}" update-index --test-untracked-cache >/dev/null 2>&1; then
	git -C "${repo_root}" config core.untrackedCache true
else
	echo "Untracked cache unsupported in ${repo_root}; leaving it off." >&2
fi

# Docker creates named volumes as root. Make persistent tool state available to
# the non-root account required for Claude's bypass-permissions mode.
sudo chown -R "$(id -u):$(id -g)" "${CLAUDE_CONFIG_DIR}" "${CODEX_HOME}" "${COPILOT_HOME}" "${HOME}/.config/herdr" "${HOME}/.config/gh" "${HOME}/.azure"

skills_installer="/opt/ai-skill/scripts/install-macos.sh"
if [[ -f "${skills_installer}" ]]; then
	bash "${skills_installer}" --no-prompt
else
	echo "Shared skills installer is unavailable at ${skills_installer}; skipping skill installation." >&2
fi

npm install -g --allow-scripts=@anthropic-ai/claude-code @anthropic-ai/claude-code @openai/codex

claude_settings="${CLAUDE_CONFIG_DIR}/settings.json"
mkdir -p "${CLAUDE_CONFIG_DIR}" "${CODEX_HOME}"

# The JavaScript template literal is intentionally protected from shell expansion.
# shellcheck disable=SC2016
node -e '
const fs = require("fs");
const path = process.argv[1];
let settings = {};
try { settings = JSON.parse(fs.readFileSync(path, "utf8")); } catch (error) {
  if (error.code !== "ENOENT") throw error;
}
settings.defaultMode = "bypassPermissions";
fs.writeFileSync(path, `${JSON.stringify(settings, null, 2)}\n`);
' "${claude_settings}"

codex_config="${CODEX_HOME}/config.toml"
touch "${codex_config}"
sed -i -E '/^(approval_policy|sandbox_mode)[[:space:]]*=/d' "${codex_config}"
# The two keys are top-level, so they go first: appended after a table such as [projects."..."] or the [features]
# the herdr integration adds, TOML would read them as keys of that table. Leading blank lines are dropped.
{
	printf 'approval_policy = "never"\nsandbox_mode = "danger-full-access"\n'
	sed '/./,$!d' "${codex_config}"
} > "${codex_config}.tmp"
mv "${codex_config}.tmp" "${codex_config}"

# herdr, a terminal multiplexer for running several coding agent sessions side by side; it is a convenience for
# the developer, not something the build or the app needs, so any failure here logs and the setup continues.
install_herdr() {
	# The installer verifies the binary's checksum and puts it in ~/.local/bin, which ~/.profile adds to the PATH
	# only when the directory existed as the login shell started, and a fresh container has none. The link puts it
	# on the PATH of the first session, and "herdr update" still replaces the binary it points to.
	if ! curl -fsSL https://herdr.dev/install.sh | sh; then
		echo "Could not install herdr; continuing without it." >&2
		return
	fi
	sudo ln -sf "${HOME}/.local/bin/herdr" /usr/local/bin/herdr || return 1

	# The agent integrations' hooks report a session to herdr with python3, and the image has only python3-minimal,
	# which lacks the json module; without the full standard library they exit early and a restart cannot resume.
	if ! { sudo apt-get update -qq \
		&& sudo DEBIAN_FRONTEND=noninteractive apt-get install -y -qq --no-install-recommends python3; }; then
		echo "Could not install python3; herdr will not resume agent sessions after a restart." >&2
	fi

	# The integrations let herdr resume a Claude Code or Codex conversation in its pane after the server restarts,
	# a rebuild included. They write a hook script and its entry into the persisted homes and are idempotent.
	local agent
	for agent in claude codex; do
		if ! herdr integration install "${agent}"; then
			echo "Could not install the herdr ${agent} integration; continuing." >&2
		fi
	done

	# The skill teaches an agent running in a herdr pane to drive herdr. The binary prints the copy matching its own
	# version, so it is rewritten on every create instead of being kept with the shared skills in /opt/ai-skill.
	local skill_root
	for skill_root in "${CLAUDE_CONFIG_DIR}/skills" "${CODEX_HOME}/skills" "${COPILOT_HOME}/skills"; do
		mkdir -p "${skill_root}/herdr" || return 1
		herdr --skill > "${skill_root}/herdr/SKILL.md.tmp" || return 1
		mv "${skill_root}/herdr/SKILL.md.tmp" "${skill_root}/herdr/SKILL.md" || return 1
	done

	local completions="${HOME}/.local/share/bash-completion/completions"
	mkdir -p "${completions}" || return 1
	herdr completion bash > "${completions}/herdr"
}
if ! install_herdr; then
	echo "The herdr setup did not finish; continuing." >&2
fi

# Restore last so a feed or project failure cannot abort assistant configuration.
# A failure here still fails postCreateCommand, but the container stays usable.
if [[ -f "${solution_path}" ]]; then
	dotnet restore "${solution_path}"
else
	echo "${solution_path} has not been created yet; skipping restore."
fi
