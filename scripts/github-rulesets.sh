#!/usr/bin/env sh
# ---------------------------------------------------------------------------
# GitHub rulesets as code
# ---------------------------------------------------------------------------
# Stores this repository's branch protection (GitHub *rulesets*) as committed
# JSON in .github/rulesets/, and rebuilds it on any repo with the GitHub CLI.
#
#   export  overwrite .github/rulesets/ with the repo's live rulesets
#   apply   create/update the repo's rulesets from .github/rulesets/
#   diff    report drift between the committed files and the live rulesets
#           (exits non-zero when they differ)
#
# WHY NOT `gh ruleset`: that command group is read-only (check/list/view). All
# writes go through `gh api` against /repos/{owner}/{repo}/rulesets.
#
# WHY RULESETS ARE MATCHED BY NAME: ruleset ids are assigned per repository, so
# a committed id is meaningless in a repo created from this template. `apply`
# looks up the id for each file's `name` and PUTs if it exists, POSTs if not.
# That makes `apply` idempotent and safe to run on a fresh repo.
#
# WHAT IS PORTABLE ACROSS REPOS (and so safe to commit here):
#   - `integration_id` in required_status_checks: a global GitHub App id
#     (15368 = GitHub Actions), identical in every repo.
#   - `actor_type: RepositoryRole` bypass actors: role ids are global
#     (5 = admin).
# NOT portable — do not commit rulesets that reference these:
#   - `actor_type: Team` or specific users: ids are org/account-scoped.
#
# WHY THIS IS NOT PART OF `make ci`: reading or writing rulesets needs repo
# admin rights. A workflow's default GITHUB_TOKEN does not have them and cannot
# be granted them via `permissions:`, so wiring `diff` into CI would fail for
# reasons unrelated to the code under test. Run these commands locally, or from
# a workflow with an admin PAT if you want enforced drift detection.
#
# Requires: `gh` (authenticated), python3, and a POSIX shell.
# Override the target repo with REPO=owner/name; it defaults to the checkout's.
# ---------------------------------------------------------------------------
set -eu

RULESET_DIR=".github/rulesets"
PYTHON="${PYTHON:-python3}"

die() {
	printf 'Error: %s\n' "$*" >&2
	exit 1
}

# Server-assigned and repo-specific fields are dropped so committed files hold
# only the ruleset's *definition*. Keys starting with "_" are dropped too: that
# covers the API's own `_links`, and means a `_comment` key is tolerated in a
# committed file (JSON has no comments) without being sent to the API or
# showing up as drift. Note that `export` regenerates files verbatim from the
# API, so it does not preserve such comments — durable prose belongs in
# .github/rulesets/README.md.
NORMALIZE_PY='
import json, sys

DROP = {"id", "node_id", "source", "source_type",
        "created_at", "updated_at", "current_user_can_bypass"}

def clean(node):
    if isinstance(node, dict):
        return {k: clean(v) for k, v in node.items()
                if k not in DROP and not k.startswith("_")}
    if isinstance(node, list):
        return [clean(v) for v in node]
    return node

json.dump(clean(json.load(sys.stdin)), sys.stdout, indent=2, sort_keys=True)
sys.stdout.write("\n")
'

# Normalize the ruleset JSON on stdin. Sorted keys and a fixed indent keep
# committed files and live API output textually comparable for `diff`.
normalize() {
	"$PYTHON" -c "$NORMALIZE_PY"
}

# Read the `name` field out of a ruleset JSON file, failing clearly if the file
# is not valid JSON or has no name.
ruleset_name() {
	"$PYTHON" -c '
import json, sys
path = sys.argv[1]
try:
    doc = json.load(open(path))
    sys.stdout.write(doc["name"])
except Exception as err:
    sys.exit("Error: %s is not a usable ruleset file: %s" % (path, err))
' "$1"
}

# A ruleset name is free text; reduce it to a safe, predictable filename.
slugify() {
	printf '%s' "$1" | tr '[:upper:]' '[:lower:]' | tr -cs 'a-z0-9' '-' | sed 's/^-//; s/-$//'
}

require_gh() {
	command -v gh > /dev/null 2>&1 || die 'the GitHub CLI (gh) was not found on your PATH. See https://cli.github.com.'
	gh auth status > /dev/null 2>&1 || die 'the GitHub CLI is not authenticated. Run: gh auth login'
	command -v "$PYTHON" > /dev/null 2>&1 || die "$PYTHON was not found on your PATH."
}

target_repo() {
	if [ -n "${REPO:-}" ]; then
		printf '%s' "$REPO"
	else
		gh repo view --json nameWithOwner --jq .nameWithOwner \
			|| die 'could not determine the current repository. Run from a GitHub checkout, or set REPO=owner/name.'
	fi
}

# Write a name<TAB>id line for every ruleset in the repo. Fetched ONCE per
# command: it keeps `apply`/`diff` to a single list call, and — more
# importantly — it fails loudly here if the repo cannot be read, so that
# "cannot reach the repo" is never silently reported as "the repo has no such
# ruleset".
write_index() {
	gh api "repos/$1/rulesets" --jq '.[] | "\(.name)\t\(.id)"' > "$2" \
		|| die "could not list rulesets for $1. Check the repo name, and that your token has admin rights on it."
}

# Print the id recorded for a ruleset name, or nothing. Matching happens in awk
# rather than a jq filter so names containing quotes cannot break the query.
lookup_id() {
	awk -F '\t' -v want="$1" '$1 == want { print $2 }' "$2"
}

# List the committed ruleset files, failing loudly rather than silently doing
# nothing when there are none.
write_file_list() {
	[ -d "$RULESET_DIR" ] || die "$RULESET_DIR does not exist. Create it, or run: make rulesets-export"
	set -- "$RULESET_DIR"/*.json
	[ -e "$1" ] || die "no ruleset files in $RULESET_DIR."
	printf '%s\n' "$@"
}

cmd_export() {
	repo="$(target_repo)"
	mkdir -p "$RULESET_DIR"
	write_index "$repo" "$TMP/index"
	[ -s "$TMP/index" ] || die "$repo has no rulesets to export."
	cut -f 2 "$TMP/index" | while read -r id; do
		[ -n "$id" ] || continue
		gh api "repos/$repo/rulesets/$id" > "$TMP/live.json"
		name="$("$PYTHON" -c 'import json, sys; sys.stdout.write(json.load(open(sys.argv[1]))["name"])' "$TMP/live.json")"
		file="$RULESET_DIR/$(slugify "$name").json"
		normalize < "$TMP/live.json" > "$file"
		printf 'exported %s -> %s\n' "$name" "$file"
	done
}

cmd_apply() {
	repo="$(target_repo)"
	write_index "$repo" "$TMP/index"
	write_file_list > "$TMP/files"
	while read -r file; do
		name="$(ruleset_name "$file")"
		id="$(lookup_id "$name" "$TMP/index")"
		if [ -n "$id" ]; then
			normalize < "$file" \
				| gh api --method PUT "repos/$repo/rulesets/$id" --input - > /dev/null
			printf 'updated %s (id %s)\n' "$name" "$id"
		else
			normalize < "$file" \
				| gh api --method POST "repos/$repo/rulesets" --input - > /dev/null
			printf 'created %s\n' "$name"
		fi
	done < "$TMP/files"
}

cmd_diff() {
	repo="$(target_repo)"
	write_index "$repo" "$TMP/index"
	write_file_list > "$TMP/files"
	drift=0
	# Read from a file rather than a pipeline: a pipeline would run this loop in
	# a subshell, losing $drift and with it the exit status.
	while read -r file; do
		name="$(ruleset_name "$file")"
		id="$(lookup_id "$name" "$TMP/index")"
		if [ -z "$id" ]; then
			printf 'ABSENT on %s: %s\n' "$repo" "$name"
			drift=1
			continue
		fi
		normalize < "$file" > "$TMP/committed.json"
		gh api "repos/$repo/rulesets/$id" | normalize > "$TMP/live.json"
		if diff -u "$TMP/committed.json" "$TMP/live.json" \
			--label "$file" --label "$repo ruleset $id" > "$TMP/report"; then
			printf 'in sync: %s\n' "$name"
		else
			printf 'DRIFT: %s\n' "$name"
			cat "$TMP/report"
			drift=1
		fi
	done < "$TMP/files"
	[ "$drift" -eq 0 ] \
		|| die "committed rulesets do not match $repo. Reconcile with: make rulesets-apply (push the repo's version) or make rulesets-export (pull GitHub's)."
}

require_gh

TMP="$(mktemp -d)"
# shellcheck disable=SC2064 # expand $TMP now, not when the trap fires.
trap "rm -rf '$TMP'" EXIT INT TERM

case "${1:-}" in
	export) cmd_export ;;
	apply) cmd_apply ;;
	diff) cmd_diff ;;
	*) die "usage: $0 {export|apply|diff}" ;;
esac
