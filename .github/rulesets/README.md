# Branch protection as code

[`protect-main.json`](protect-main.json) is this repository's GitHub **ruleset** —
branch protection — stored as code. Manage it from the repository root:

```sh
make rulesets-apply     # create/update the ruleset on GitHub
make rulesets-diff      # fail if GitHub no longer matches this file
make rulesets-export    # pull GitHub's version back into this file (after a UI edit)
```

All three run [`scripts/github-rulesets.sh`](../../scripts/github-rulesets.sh),
which talks to `/repos/{owner}/{repo}/rulesets` via `gh api`. The `gh ruleset`
command group is read-only (`check`, `list`, `view`), so it cannot do this.

This file covers how the rules are *stored and rebuilt*. For the day-to-day
workflow they impose, see
[The workflow these rules require](../../README.md#the-workflow-these-rules-require).

## What `protect-main` enforces

It targets `~DEFAULT_BRANCH` rather than a literal `refs/heads/main`, so it keeps
protecting the right branch in a fork or a rename, and in any repository created
from this template.

| | |
| --- | --- |
| Enforcement | `active` |
| Direct pushes | blocked — pull request required, from any branch |
| Required check | `ci` (GitHub Actions) |
| Branch must be up to date | yes (`strict_required_status_checks_policy`) |
| Approvals | 1, dismissed on every push |
| Bypass actors | repository admin, `"pull_request"` mode |
| Merge methods | merge commits only |
| Signed commits | required |
| Code scanning | CodeQL, errors / high-or-higher security alerts |
| Code quality | errors |
| Force push / deletion | blocked |

Four things about this configuration are load-bearing and easy to break:

- **`required_approving_review_count` is `1` and `bypass_actors` grants the admin
  role a `"pull_request"` bypass.** These two go together and must be changed
  together. You cannot approve your own pull request, so the approval is not
  satisfiable on a solo repository without the bypass; and the bypass, being a
  ruleset-wide exemption, also lets an admin merge past a red `ci`. The result
  prevents an accidental merge of a failing build, not a determined one.

  `"pull_request"` mode is used rather than `"always"` so that direct pushes to the
  default branch stay blocked even for an admin.

  To make the CI requirement absolute, empty `bypass_actors` **and** set the
  approval count back to `0` in the same edit — an empty bypass with a non-zero
  count leaves a repository nobody can merge into. Add a `CODEOWNERS` file before
  turning `require_code_owner_review` back on, since that rule is inert without
  one.
- **The merge method is a merge commit, to preserve authorship.** A merge commit
  adds one new object and leaves the branch's own commits untouched, so each keeps
  the signature its author made, while GitHub signs the merge commit itself to
  satisfy `required_signatures`. Squash and rebase both rewrite history and lose
  that — see below.
- **`required_linear_history` is deliberately absent.** It forbids merge commits,
  so it cannot coexist with the merge-commit policy above. Re-adding it means
  going back to squash or rebase, and giving up author signatures on the default
  branch.

## Signed commits rule out rebase merges

A rebase merge rewrites every commit into a new object, which discards the
author's signature, and GitHub has no key with which to re-sign on the author's
behalf. Listing `rebase` in `allowed_merge_methods` alongside `required_signatures`
yields a merge button that always fails with:

> Base branch requires signed commits. Rebase merges cannot be automatically
> signed by GitHub.

A squash merge does work — GitHub signs the single commit it creates — but it
collapses the branch into one new commit, so the author's own signatures never
reach the default branch. Merge commits are the only method that both satisfies
the signing rule and keeps the author's signatures in history. The cost is that
the default branch is not linear; that is the deliberate trade.

## The ruleset cannot enable a merge method the repository forbids

`allowed_merge_methods` only *narrows* what the repository already permits. The
repository has its own `allow_merge_commit` / `allow_squash_merge` /
`allow_rebase_merge` switches, they are not part of any ruleset, and this file
cannot set them. Ask for a method the repository has switched off and the merge is
refused outright:

> Merge commits are not allowed on this repository.

So a repo adopting this ruleset needs merge commits enabled as well — under
**Settings → General → Pull Requests**, or:

```sh
gh api --method PATCH repos/OWNER/REPO -F allow_merge_commit=true
```

This is the one part of the gate that `make rulesets-apply` cannot reproduce for
you, because it lives on the repository rather than in a ruleset.

## The required check must actually run

A required status check that is never reported is never green, so the pull request
waits on it forever. The `context` value here (`ci`) is the **job id** in
[`.github/workflows/ci.yml`](../workflows/ci.yml), and that workflow's
`pull_request` trigger has to include every branch this ruleset protects. If you
rename the job, or protect another branch, update both files together.

The `code_scanning` rule has the same dependency: it needs CodeQL results to
exist. This repository has code scanning **default setup** enabled, which supplies
them. Disabling it, or applying this ruleset to a repository without it, leaves
that rule waiting on a tool that never runs.

## What is portable between repositories

This file is meant to be copied into repos created from this template, so it only
references ids that mean the same thing everywhere:

- **`integration_id: 15368`** in `required_status_checks` is the GitHub Actions
  app — a global app id, identical in every repository.
- **`~DEFAULT_BRANCH`** resolves per repository, so no branch name is hard-coded.

Do **not** commit rulesets that reference `actor_type: "User"` or `"Team"`: those
ids are scoped to one account or organization and will resolve to the wrong thing,
or fail, elsewhere. (`RepositoryRole` ids are global and would be safe, but see
the `bypass_actors` note above before adding any.)

Ruleset **ids** are likewise per repository, which is why `apply` matches on the
`name` field instead. Rename the ruleset in this file and the next `apply` creates
a second one rather than renaming the original.

## Why these targets are not part of `make ci`

Rulesets are admin API surface. A workflow's default `GITHUB_TOKEN` cannot read
them and cannot be granted the rights via `permissions:`, so a `rulesets-diff`
step in CI would fail for reasons that have nothing to do with the code under
test — and it would break the rule that `make ci` behaves the same on a laptop as
in a pipeline. Run these locally, or from a workflow with an admin PAT if you want
drift detection enforced.

## Editing

JSON has no comments. `scripts/github-rulesets.sh` strips any key beginning with
`_`, so a `"_comment"` key here is harmless — but `make rulesets-export`
regenerates the file verbatim from the API and will drop it. Durable explanation
belongs in this file.

If you change protection in the GitHub UI, run `make rulesets-export` to bring the
change back into the repo; otherwise the next `make rulesets-apply` will silently
revert it.

**`apply` never deletes.** It creates and updates the rulesets named by the files
here, and ignores anything else on the repo. Removing a file therefore leaves its
ruleset live on GitHub — delete that one yourself:

```sh
id=$(gh api repos/OWNER/REPO/rulesets --jq '.[] | select(.name=="NAME") | .id')
gh api --method DELETE repos/OWNER/REPO/rulesets/$id
```

The omission is deliberate: a prune step would give a script that runs against any
repo the power to remove protection it did not create.
