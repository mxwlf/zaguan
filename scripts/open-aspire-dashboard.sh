#!/usr/bin/env sh
# ---------------------------------------------------------------------------
# Open the Aspire dashboard in a browser
# ---------------------------------------------------------------------------
# Waits for the AppHost named by $1 to come up, then opens its dashboard URL.
# Meant to be backgrounded alongside `aspire run` — see the `run` target in the
# Makefile.
#
#   ./scripts/open-aspire-dashboard.sh src/some.apphost/some.apphost.csproj
#
# WHY THIS EXISTS: the Aspire CLI has no option to do it. There is no `--open`
# or `--launch-browser` flag on `aspire run`, no `features.*` config entry for
# it (`aspire config list --all`), and the CLI binary contains no browser
# launcher at all — the `"launchBrowser": true` strings inside it belong to the
# launchSettings.json TEMPLATE that `aspire new` scaffolds, complete with
# `{{hostName}}` placeholders. Checked against CLI 13.4.6.
#
# WHY NOT A launchSettings.json PROFILE: `aspire run` does accept
# `--launch-profile`, so a profile with `launchBrowser: true` looks like the
# idiomatic answer. It is not. A launch profile opens its own `applicationUrl`,
# whereas the dashboard is served by the CLI on an ephemeral port behind a
# one-time login token (`/login?t=…`) minted at startup. A profile cannot know
# either, so it would open the wrong address and land on a login prompt.
# Capturing the real URL is the only thing that works.
#
# WHY `aspire ps` AND NOT THE OUTPUT OF `aspire run`: the obvious approach is to
# pipe `aspire run` through a filter and watch for the URL it prints. That costs
# the whole interactive experience — piping takes stdout off a TTY, so the
# spinners, colours and progress rendering degrade, and output can sit in a pipe
# buffer. `aspire ps --format Json` reports `dashboardUrl` for every running
# AppHost, token and all, so the URL can be read out of band while `aspire run`
# keeps the terminal entirely to itself.
#
# WHY IT NEVER FAILS LOUDLY: this is a convenience running beside the thing the
# developer actually asked for. `aspire run` already prints the URL, so if any
# of this does not work the information is still on screen. Every exit is 0 and
# the only output is the fallback URL when no browser opener can be found —
# anything chattier would interleave with, and garble, the AppHost's own
# terminal rendering.
#
# WHY IT DOES NOT TRY TO IGNORE AN ALREADY-RUNNING INSTANCE: an earlier version
# recorded the AppHost pids present at startup so it could hold out for a new
# one. That is a race it cannot win — this script is backgrounded and samples
# within milliseconds, while `aspire run` needs seconds to build and register —
# and losing the race means silently opening nothing. Taking whatever instance
# is running for this AppHost cannot do worse than opening a dashboard that
# works, because two non-isolated instances of one AppHost do not coexist.
set -eu

apphost=${1:-}
if [ -z "$apphost" ]; then
	echo "usage: $(basename "$0") <apphost-project-path>" 1>&2
	exit 2
fi

# Seconds to wait for the AppHost to register. Generous because the first run on
# a clean checkout restores and builds the whole graph before anything starts.
timeout=${ASPIRE_DASHBOARD_TIMEOUT:-180}

# Prints the dashboard URL of a running AppHost matching $1, or nothing.
#
# python3 rather than jq: it is already a hard prerequisite of this repository
# (the Makefile builds .venv with it), whereas jq is not, and this runs from a
# core development target. Paths are compared after realpath so the relative
# path make passes matches the absolute one the CLI reports.
dashboard_url() {
	# shellcheck disable=SC2016 # The python source is single-quoted deliberately: nothing in it is
	# meant to be expanded by the shell, and the AppHost path arrives as an argv entry instead.
	aspire ps --format Json 2>/dev/null | python3 -c '
import json, os, sys

target = os.path.realpath(sys.argv[1])

try:
    entries = json.load(sys.stdin) or []
except Exception:
    raise SystemExit(0)

for entry in entries:
    if os.path.realpath(entry.get("appHostPath") or "") != target:
        continue
    # `aspire ps` reports a status; the detached form of `aspire run` does not.
    if (entry.get("status") or "running") != "running":
        continue
    url = entry.get("dashboardUrl")
    if url:
        print(url)
        break
' "$apphost"
}

# Hands a URL to the platform's browser.
open_url() {
	if command -v open > /dev/null 2>&1; then
		open "$1" > /dev/null 2>&1 || true
	elif command -v xdg-open > /dev/null 2>&1; then
		xdg-open "$1" > /dev/null 2>&1 || true
	elif command -v cmd.exe > /dev/null 2>&1; then
		# WSL and Git Bash. The empty "" is start's title argument; without it a
		# quoted URL is taken as the window title and nothing opens.
		cmd.exe /c start "" "$1" > /dev/null 2>&1 || true
	else
		printf 'Dashboard: %s\n' "$1"
	fi
}

deadline=$(($(date +%s) + timeout))

while [ "$(date +%s)" -lt "$deadline" ]; do
	url=$(dashboard_url || true)

	if [ -n "$url" ]; then
		open_url "$url"
		exit 0
	fi

	sleep 1
done

# Timed out. Silent on purpose: `aspire run` has either failed, in which case it
# said so, or printed the URL itself.
exit 0
