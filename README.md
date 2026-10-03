# dotnet-template

My baseline git template. It ships a shared git configuration and a set of
[pre-commit](https://pre-commit.com) hooks so every repo started from this
template gets consistent commit hygiene and commit-message formatting out of
the box.

All tooling lives in a **repo-local virtualenv** (`.venv/`) created by
`make setup`. Nothing is installed globally, and no specific global Python
version is required.

## Requirements

- **GNU make**, **git**, and a POSIX shell (on Windows: Git Bash or WSL).
- **Any Python interpreter `>= 3.10`** reachable as `python3`. It is used *only*
  to create `.venv/`, so whatever you already have works — system, Homebrew,
  pyenv, uv, asdf. If your default `python3` is older, point make at another
  interpreter instead of changing your system default:

  ```sh
  make setup PYTHON=python3.12          # or an absolute path
  ```

  Check what make will use with `make check-python`.

That is the whole list — `pre-commit` itself is **not** a prerequisite.
`make setup` installs the pinned version from
[`requirements-dev.txt`](requirements-dev.txt) into `.venv/`.

> **Why a venv instead of a required global Python version?** Some hooks
> (commitizen, sync-pre-commit-deps) are `language: python` and need
> Python `>= 3.10`. Their upstream manifests declare `language_version: python3`,
> which pre-commit resolves to *the interpreter running pre-commit itself* — not
> to whatever `python3` is on your `PATH`. So a `pre-commit` installed under an
> old interpreter (e.g. macOS's bundled Python 3.9) builds those hook
> environments with 3.9 and fails with `requires a different Python`.
>
> Rather than papering over that with per-hook `language_version` pins — which
> forced every contributor to install one exact Python version globally — the
> template runs pre-commit from `.venv/`, built from any Python `>= 3.10`. The
> hooks inherit that interpreter, so
> [`.pre-commit-config.yaml`](.pre-commit-config.yaml) needs no interpreter pins
> at all and your machine's Python setup is left alone.

## Setup

From the repository root:

```sh
make setup
```

This does three things:

1. Creates `.venv/` and installs the pinned tooling
   ([`requirements-dev.txt`](requirements-dev.txt)) into it.
2. Runs `git config --local include.path ../.gitconfig`, which makes the repo's
   local config include the committed [`.gitconfig`](.gitconfig). That config
   sets `core.hooksPath = .githooks/`, activating the committed hook scripts.
   Because the hooks live in `.githooks/` and are wired up through
   `include.path`, **`pre-commit install` is not required**.
3. Runs `pre-commit install-hooks` to pre-build the hook environments, so your
   first commit isn't slowed down by it.

You never need to *activate* `.venv`: the hook scripts in `.githooks/` and every
make target invoke `.venv/bin/pre-commit` by absolute path.

Run `make help` (or just `make`) to list the available targets.

## What gets configured

| File | Purpose |
| --- | --- |
| [`.gitconfig`](.gitconfig) | Sets `core.hooksPath = .githooks/` so the committed hooks are used. |
| [`.githooks/pre-commit`](.githooks/pre-commit) | Runs the `pre-commit`-stage hooks (formatting, secret detection, etc.) via `.venv`. |
| [`.githooks/commit-msg`](.githooks/commit-msg) | Runs commitizen via `.venv` to enforce [Conventional Commits](https://www.conventionalcommits.org/) message format. |
| [`.pre-commit-config.yaml`](.pre-commit-config.yaml) | Declares the hook repos and pinned versions (`rev`). |
| [`requirements-dev.txt`](requirements-dev.txt) | The pinned tooling installed into `.venv/` — just `pre-commit`. |
| `.venv/` | The repo-local virtualenv. Created by `make setup`, git-ignored, disposable (`make clean`). |

Each hook's own dependencies are installed by pre-commit into its own cached
environments (`~/.cache/pre-commit`, or `~/Library/Caches/pre-commit` on macOS),
not into `.venv/`.

### Hooks included

- **pre-commit-hooks**: trailing whitespace, end-of-file fixer, YAML checks,
  large-file guard (blocks files over 500 kB by default), case-conflict
  detection (catches filename collisions on case-insensitive filesystems like
  macOS/Windows), illegal Windows names, merge-conflict markers, private-key
  detection, byte-order-marker fix, and mixed line endings.
- **gitleaks**: scans for hardcoded secrets.
- **commitizen** (`commit-msg` stage): validates commit messages follow the
  Conventional Commits format, e.g.:
  ```
  feat: add user login
  fix(api): handle null response
  chore: bump dependencies
  ```
- **sync-pre-commit-deps**: keeps hook dependency versions in sync.
- **dotnet-build-test** (local): runs `make test`, which builds and then tests in
  `Release`. That is the configuration where analyzer findings are errors — Debug
  builds only warn, so you can iterate without cleaning up every diagnostic
  first, and this hook is what stops an unresolved one from being committed. It
  only runs when a build input (`.cs`, `.csproj`, `.slnx`, `.props`, `.targets`,
  `.json`) is staged, so documentation-only commits do not pay for a build and
  test run. `make lint` skips it, because `make ci` builds and tests through its
  own targets.

## CI/CD integration

This template is designed so that wiring it into **any** CI/CD platform is
simple and deterministic. It follows the industry-standard *thin wrapper*
pattern (see [Martin Fowler on Continuous
Integration](https://martinfowler.com/articles/continuousIntegration.html#AutomateTheBuild)):
all the actual check logic lives **in the repository** behind a single command,
and each CI platform's config does nothing more than check out the code,
provide a Python interpreter, and run that one command.

```
make ci                       ← single source of truth (runs locally too)
  └── .venv/ (built from requirements-dev.txt)
        └── pre-commit run --all-files
              └── hooks in .pre-commit-config.yaml

.github/workflows/ci.yml      ← thin stub: checkout → python → `make ci`
azure-pipelines.yml           ← thin stub: checkout → python → `make ci`
```

The same `make ci` a developer runs on their laptop is exactly what runs on
GitHub Actions and Azure DevOps — including building the venv, so the CI stubs
install nothing themselves. To change *what* CI does, edit the
[`Makefile`](Makefile) and [`.pre-commit-config.yaml`](.pre-commit-config.yaml)
— **not** the platform YAML.

| File | Purpose |
| --- | --- |
| [`Makefile`](Makefile) (`make ci`) | The portable entrypoint. All check logic lives here. Extend it with your project's build/test commands. |
| [`.github/workflows/ci.yml`](.github/workflows/ci.yml) | Thin GitHub Actions stub that runs `make ci`. |
| [`azure-pipelines.yml`](azure-pipelines.yml) | Thin Azure DevOps stub that runs `make ci`. |

### Extending `make ci` for your project

Add your build/test steps to the `ci` target in the [`Makefile`](Makefile). For
example, for a .NET project:

```make
ci: lint test ## Run the full CI check suite

test: ## Run the test suite
	dotnet test
```

Because the logic is in the Makefile, those steps run identically locally and
on every CI platform — no YAML changes required.

### What you still configure per platform (and why)

The thin-wrapper pattern minimizes platform-specific config but cannot
eliminate it. Each platform requires its own small YAML stub, and a few
concerns are inherently platform-specific and **cannot** be pushed into a
portable script:

- **The stub file itself** — GitHub needs `.github/workflows/*.yml`; Azure
  DevOps needs `azure-pipelines.yml`. The template ships both, pre-wired to
  `make ci`.
- **Triggers** (which branches/events run CI) — expressed differently on each
  platform. Both stubs ship pre-configured to run CI on:
  - **pushes** to `main`, and
  - **pull requests** targeting `main`, from any source branch.

  A pull request that never triggers a run cannot be gated on one, so the
  pull-request trigger must cover every branch you gate — see [Making CI a merge
  gate](#making-ci-a-merge-gate). Feature branches are intentionally absent from
  the push trigger: a branch with an open pull request would otherwise build the
  same commits twice for no extra signal.

  Adjust the `on`/`trigger`/`pr` sections in
  [`.github/workflows/ci.yml`](.github/workflows/ci.yml) and
  [`azure-pipelines.yml`](azure-pipelines.yml) to change this.
- **Publishing reports** — `make ci` *produces* the test and coverage reports
  identically everywhere, but surfacing them is platform-specific: GitHub
  Actions uploads them as a run artifact, Azure DevOps publishes them to its
  Tests and Code Coverage tabs. See [Where the reports
  appear](#where-the-reports-appear).
- **Secrets, service connections, OIDC, and permissions** — managed in each
  platform's settings/YAML, never in the repo.
- **Runner/agent image** and **which Python interpreter is on the agent** (the
  base for `.venv`).
- **Merge gating** — making a green run *mandatory* is a repository setting, not
  a pipeline setting. See [Making CI a merge gate](#making-ci-a-merge-gate).

Everything else — the actual checks — is shared via `make ci`.

### Making CI a merge gate

Running CI is not the same as requiring it. Nothing in this repository can stop
a commit from reaching `main` — the hooks in [`.githooks/`](.githooks) are
client-side and `git commit --no-verify` skips them, so enforcement has to live
on the server, in the forge.

**GitHub.** The gate is a [repository
ruleset](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/about-rulesets),
shipped here as
[`.github/rulesets/protect-main.json`](.github/rulesets/protect-main.json) so it
is reviewable and reproducible rather than click-configured. Apply it with:

```sh
make rulesets-apply     # create/update the ruleset on GitHub
make rulesets-diff      # fail if GitHub no longer matches the repo
make rulesets-export    # pull GitHub's version back in (after a UI edit)
```

These need the [GitHub CLI](https://cli.github.com), authenticated with admin
rights on the repository; unlike everything else here, `gh` is **not** installed
by `make setup`. The ruleset is reconciled **by name**, not by id: GitHub assigns
ruleset ids per repository, so a committed id would be meaningless in a repo
created from this template. `apply` looks up the name, updates the ruleset if it
exists and creates it if it does not — so it is idempotent and works on a fresh
repo. It never *deletes*; see
[`.github/rulesets/README.md`](.github/rulesets/README.md) for that and for the
full rule-by-rule breakdown.

It targets the default branch and requires the **`ci`** check — the job id in
[`.github/workflows/ci.yml`](.github/workflows/ci.yml), which is also the check
name GitHub reports — to pass before anything merges. Changes must arrive by
pull request, commits must be signed, CodeQL and code-quality findings must be
clean, and force-pushes and deletions are refused.

Two settings decide how real that gate is, and both are easy to get wrong:

- **`required_approving_review_count` is `1`, with a review dismissed on every
  push** (`dismiss_stale_reviews_on_push`). You cannot approve your own pull
  request, so on a solo or two-person repository this is *not* satisfiable on its
  own — it only works because of the bypass below. Add a `CODEOWNERS` file before
  turning `require_code_owner_review` back on, since that rule is inert without
  one.
- **`bypass_actors` grants the repository admin role a `"pull_request"` bypass.**
  This is what makes the approval above satisfiable, and it is a deliberate
  loosening: an admin can merge a pull request whose `ci` check is red. The gate
  stops an *accidental* merge of a failing build, not a determined one. The
  narrower `"pull_request"` mode is used rather than `"always"`, so direct pushes
  to the default branch stay blocked even for an admin.

  If you want the CI requirement to be absolute instead, empty `bypass_actors`
  **and** drop the approval count back to `0` in the same edit — an empty bypass
  with a non-zero count leaves a repository nobody can merge into. Requiring a
  pull request while requiring zero approvals keeps CI as the only gate and keeps
  the repository usable; that is the right configuration for a repo with no second
  reviewer, and it is what this file shipped with before it was aligned to
  `git-template`.

`strict_required_status_checks_policy` is `true`, which additionally requires a
branch to be up to date with the default branch before it merges. Without it a
stale-but-green run can merge and break `main`, because the check passed against
an older base.

One honest limit: the merge commit that lands on the default branch is a *new*
commit that CI never ran on, so what the gate guarantees is that the reviewed
*content* was green, not that a run exists for that exact SHA. Because
`strict_required_status_checks_policy` forces the branch to be up to date first,
the tree that lands is the tree that was tested — and unlike a squash or a
rebase, a merge commit keeps the tested commit itself in history as its second
parent, so the run still maps to a real commit on the default branch. The `push`
trigger on `main` records the truth afterwards but cannot block. Closing that last
gap needs a [merge
queue](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/configuring-pull-request-merges/managing-a-merge-queue),
which is only available on repositories owned by an organization.

**Azure DevOps.** There is no repository-side equivalent in YAML — for Azure
Repos the `pr:` trigger in [`azure-pipelines.yml`](azure-pipelines.yml) is
ignored entirely. Gate the branch instead with **Repos → Branches → `main` →
Branch policies → Build Validation**, pointing at this pipeline with *Policy
requirement* set to **Required**. The same two traps apply: keep the reviewer
minimum satisfiable, and leave "Allow bypass" off.

### The workflow these rules require

The workflow is **trunk-based**. `main` is the only long-lived branch: there is no
`develop` to stage through, and no release branches. Everything else is a
short-lived branch that exists just long enough to carry one pull request, and is
deleted after it merges.

With `protect-main` active, `main` cannot be written to directly. Every change
reaches it the same way:

1. **Branch off `main`.** Name it however you like — the rules place no constraint
   on source branches, and a feature branch needs no protection of its own, since
   it cannot reach `main` except through the gate below. Keep it short-lived; the
   point of trunk-based work is that branches merge in days, not weeks. Commits
   are checked locally by the hooks from `make setup`.
2. **Open a pull request into `main`.** A direct `git push origin main` is rejected
   by the ruleset, as is a force-push and a branch deletion.
3. **Let `ci` finish and pass.** It is a required check, so the merge button stays
   disabled until it reports success, and the branch must be up to date with
   `main` first. CodeQL and code-quality findings must be clean too.
4. **Merge with a merge commit, then delete the branch.** `main` accepts *only*
   merge commits — squash and rebase are not offered, because both rewrite history
   and discard the signature you made. A merge commit leaves your commits intact
   and GitHub signs the merge commit itself, satisfying `required_signatures`.

The approval in step 3 is not satisfiable on a solo repository — you cannot approve
your own pull request — so merging relies on the repository admin's
`"pull_request"` bypass. That means the gate prevents an accidental merge of a red
build rather than a determined one. See the two settings under [Making CI a merge
gate](#making-ci-a-merge-gate) for how to make it absolute instead.

### Where the reports appear

`make test` writes a TRX report and a Cobertura coverage file per test module into
`artifacts/test-results/`; `make coverage` merges the coverage files into one
report in `artifacts/coverage/` and fails the build below `COVERAGE_MIN_LINE`
(80% by default — override it, or set it empty to turn the gate off). Both
directories are gitignored, so the pipelines are what make them visible:

| Report | Where to look |
| --- | --- |
| Test results, coverage, slow tests, and each failure's recent history | **GitHub Actions → run → Summary**, as a job summary |
| Failed and skipped tests | Annotations on that Summary page, in the job log, and inline on a pull request's **Files changed** and **Checks** tabs |
| Per-assembly console output | The `make ci` step's log, one collapsible group per test module |
| The report files themselves (TRX, merged Cobertura, browsable HTML) | The **`ci-reports`** artifact on the run Summary page — uploaded even when the run is red, kept 30 days |
| Failure history snapshot | **GitHub Actions → Caches** (`gh-test-history-…`); its contents only surface inside the job summary |
| Test results and coverage on Azure DevOps | The run's native **Tests** and **Code Coverage** tabs |
| The SBOM | Inside the same **`ci-reports`** artifact on GitHub; a separate **`sbom`** pipeline artifact on Azure DevOps, which has no tab to render one |
| The NuGet packages | Their own **`package`** artifact on both platforms — they are the output, not a report, so fetching a build to install or inspect does not mean downloading test results |

Locally, `make coverage` prints the same coverage figures to the terminal and
leaves `artifacts/coverage/index.html` to open in a browser. The GitHub-specific
output is inert off a runner, so one command behaves correctly in both places.

### Target frameworks

One property in [`Directory.Build.props`](Directory.Build.props) decides what everything
targets:

```xml
<LibraryTargetFrameworks>net10.0</LibraryTargetFrameworks>
```

Give it a `;`-separated list to multi-target — `net10.0;net9.0` — and every project follows.

What the file does with that value is the part worth knowing, because getting it wrong produces
a failure that only some tools see. It sets **`TargetFramework`** (singular) for a single
framework and **`TargetFrameworks`** (plural) only for a real list.

Setting the plural property makes a project a **cross-targeting** build *even with one entry*.
A cross-targeting project is really two builds: an outer build that only dispatches, and an inner
build per framework that does the work — and most SDK targets exist only on the inner build. Ask
an outer build for one of them and you get:

```
error MSB4057: The target "GetTargetPath" does not exist in the project.
```

`dotnet build` never shows this, because the CLI drives the outer→inner dispatch itself and never
asks the outer build for a target it lacks. Tools that query a project directly do: **Rider calls
`GetTargetPath`** to resolve a project reference's output assembly, and fails on every project.
The same root cause also appears as `MSB4036 PickBestRid` and `MSB4057 ComputeRunArguments` under
the Aspire SDK — one defect wearing several faces, which makes it easy to patch per symptom
instead of fixing once.

A project can still override per-project. The conditions defer to a `TargetFramework` or
`TargetFrameworks` it sets itself — but because `Directory.Build.props` is imported *before* the
project body, one that wants to multi-target against a single-framework repo default has to clear
the singular as well:

```xml
<TargetFramework></TargetFramework>
<TargetFrameworks>net10.0;net9.0</TargetFrameworks>
```

### Versioning

Versions are **derived from git**, not written by hand. There is no version string in
any `.csproj`, and no `VERSION` variable to pass to make — a version you could
override on the command line would disagree with the one stamped into the assembly.
[Nerdbank.GitVersioning](https://github.com/dotnet/Nerdbank.GitVersioning) computes it
from [`version.json`](version.json) plus the commit being built:

```json
{
  "version": "0.1-alpha",
  "publicReleaseRefSpec": [
    "^refs/heads/main$",
    "^refs/tags/v\\d+\\.\\d+"
  ]
}
```

Two fields do all the work.

**`version`** is the major.minor you intend to ship, optionally with a prerelease tag.
The patch number is *not* in the file: it is the **version height**, the number of
commits since this field last changed. So `0.1-alpha` yields `0.1.1-alpha`,
`0.1.2-alpha`, `0.1.3-alpha` … one per commit, automatically ordered, with no bump
commits. Raising the field to `0.2` resets the height, and the next build is `0.2.1`.
Dropping `-alpha` is what declares the API stable.

**`publicReleaseRefSpec`** decides which refs produce clean versions. A build of a ref
that matches is a *public release* and is versioned plainly; anything else gets a
`-g<commit>` suffix so it can never be mistaken for a release:

| Building | Version |
| --- | --- |
| `main` | `0.1.4-alpha` |
| a `v0.1`-style tag | `0.1.4-alpha` |
| a feature branch or PR | `0.1.4-alpha-g1a2b3c4d5e` |

The same computation feeds several places at once, which is why nothing has to be kept
in sync by hand:

| Consumer | Value |
| --- | --- |
| `AssemblyVersion` | `0.1.0.0` — major.minor only, so a patch never breaks binding |
| `FileVersion` | `0.1.4.<revision>` |
| `AssemblyInformationalVersion` | `0.1.4-alpha+<commit>` — carries the exact commit |
| The `.nupkg` / `.snupkg` filename | `library.example.0.1.4-alpha.nupkg` |
| The SBOM's document subject | `dotnet-template 0.1.4-alpha` |

`nbgv` is wired in twice for this: as a `PackageReference` in
[`Directory.Build.props`](Directory.Build.props) that stamps the assembly during the
build, and as a pinned tool in
[`.config/dotnet-tools.json`](.config/dotnet-tools.json) that `make sbom` queries for
the document subject. Inspect what a commit will produce with:

```sh
dotnet nbgv get-version
```

> **CI must clone with full history.** The patch number is a *count of commits*, so a
> shallow clone has nothing to count. Both stubs therefore set it explicitly —
> `fetch-depth: 0` on `actions/checkout`, and an explicit `checkout: self` with
> `fetchDepth: 0` on Azure, which exists only for that reason. This is a dependency of
> `version.json`: before that file existed no height was computed, and the default
> shallow clone was harmless.

One consequence worth knowing: because `AssemblyVersion` is deliberately truncated to
`major.minor`, every patch in a minor series is binding-compatible, and the precise
build is identified by `AssemblyInformationalVersion` instead. That is the intended
trade, not an oversight.

Two things versioning currently blocks, both waiting on a first published release:

- **Baseline API-compatibility validation.** `EnablePackageValidation` is on for
  packable projects, but `PackageValidationBaselineVersion` is conditioned on
  `ApiCompatBaselineVersion`, which is unset — so validation checks a package's
  internal consistency and not whether it broke a previously shipped API. Set that
  property to a released version to engage it. `microsoft.dotnet.apicompat.tool` is
  pinned and waiting.
- **Publishing.** Nothing in this repository pushes packages anywhere; they are built
  and retained as CI artifacts only. Versions are now ordered and publishable, which
  is the prerequisite, but where they go is left to the project.

### Packaging is opt-in

`make pack` writes `.nupkg` and `.snupkg` files into
`artifacts/package/<configuration>/`. It runs as part of `make ci`, so a broken
package — bad metadata, a missing readme, a package-validation failure — fails the
build rather than being discovered at publish time.

That directory is **emptied first**, so it always holds exactly what the last run produced.
This is not tidiness: everything downstream of it is a glob. `publish.yml` pushes
`artifacts/package/release/*.nupkg` and attests the same pattern, so a version left over from
an earlier local pack would be published and attested alongside the intended one. CI never
hits this because a runner starts clean; a laptop accumulates. Only the current
configuration's directory is cleared, so packing `Debug` does not discard a `Release`
package.

**Nothing is packable unless the project says so.** `Directory.Build.props` sets
`IsPackable=false` for the whole repository, and a project that ships opts in and
describes itself:

```xml
<PropertyGroup>
  <IsPackable>true</IsPackable>
  <Description>What this package is.</Description>
  <PackageTags>your;tags</PackageTags>
  <PackageReadmeFile>README.md</PackageReadmeFile>
</PropertyGroup>

<ItemGroup>
  <None Include="README.md" Pack="true" PackagePath="\" />
</ItemGroup>
```

[`src/library.example`](src/library.example) is the worked example, including
[its own package readme](src/library.example/README.md).

The default is that way round because "packable unless you opt out" is wrong for a
repository that grows samples, benchmarks, integration-test hosts and internal
tools — none of which should be publishable by accident. The same reasoning applies
to *contents*: a project declares every file it puts in its package, because what
goes into a package describes that package. A shared `Pack` item would put the
repository README — which documents make targets and CI wiring — into every package
in the solution, saying nothing useful about any of them.

Shared in `Directory.Build.props` are only facts about the repository rather than
about any one package: license, authors, copyright, repository and project URLs.

> **Guards on `$(IsPackable)` belong in `Directory.Build.targets`, never in
> `Directory.Build.props`.** Props is imported *before* the project body, so a
> condition there reads whatever default is in scope instead of the project's own
> choice. When `EnablePackageValidation` was guarded in props, the test project —
> which sets `IsPackable=false` itself — still evaluated to
> `EnablePackageValidation=true`, and inherited a `PackageReadmeFile` it never
> packed. `Directory.Build.targets` is imported afterwards and sees the final value.

`PackageIcon` is left commented out in `Directory.Build.props`: it names a file that
does not exist, and unlike a missing description, pack *fails* (NU5046) on a declared
icon it cannot find. Add the file and a matching `Pack` item before enabling it.

### Publishing to nuget.org

[`.github/workflows/publish.yml`](.github/workflows/publish.yml) publishes on a version tag,
using [trusted publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
(OIDC) rather than a long-lived API key: `NuGet/login` exchanges the workflow's own identity
for a token valid about an hour, so there is no `NUGET_API_KEY` to rotate or leak.

It is a separate workflow from CI on purpose. Publishing needs `id-token: write`, and nothing
that runs on untrusted pull-request input should ever hold that; keeping them apart also means
a package is only produced by an explicit tag, never by a merge.

**Before the first publish**, three things must be set up outside the repository:

1. **A `PackageId` you are willing to live with.** NuGet package ids can never be renamed,
   deleted or reused — only unlisted. This repository publishes
   **`mxwlf.net.library.example`**, set explicitly in
   [`library.example.csproj`](src/library.example/library.example.csproj) rather than defaulting
   to the assembly name.

   **If you are building on this template, change it before your first tag.** That id sits under
   a reserved prefix belonging to the template's author, so nuget.org would reject it from
   anywhere else — `publish.yml` fails early with an explanation rather than letting you discover
   that at push time.
2. **A trusted publishing policy**, created on nuget.org under your **username → Trusted
   Publishing** ([direct link](https://www.nuget.org/account/trustedpublishing)):

   | Field | Value |
   | --- | --- |
   | Repository Owner | your GitHub account or organisation |
   | Repository | this repository's name |
   | Workflow File | `publish.yml` — the **exact filename**, no path, case-insensitive |
   | Environment | `release` — must equal the job's `environment:` in `publish.yml` |

   The workflow filename is part of the policy, so renaming `publish.yml` breaks publishing
   with *"no matching policy"* until the policy is updated. It is the filename that matters,
   not the workflow's `name:` field.

   A policy may start out **"temporarily active" for 7 days** — the docs say this usually
   happens with private repositories, but do not promise public ones skip it. The reason is
   that nuget.org needs GitHub's numeric repository and owner **ids** to pin the policy against
   resurrection attacks (deleting a repo and recreating it under the same name), and it only
   receives those from a successful publish. So if you see that status, publish within the
   window; it can be restarted at any time, even after lapsing.

   See [Publishing from several repositories](#publishing-from-several-repositories) for how
   this scales, and why you should not leave the package scope at its default.

3. **A matching GitHub Environment** — Settings → Environments → New environment. This
   repository uses `release`, because that is what its nuget.org policy names. Add an
   environment secret `NUGET_USER` holding your nuget.org **username (profile name), not your
   email address**. Add required reviewers there if you want an approval gate on every publish.

   The name is load-bearing on both sides: nuget.org validates the environment claim in the
   OIDC token whenever the policy names one, so a policy and a workflow naming different
   environments are refused at the token exchange — before the push, and without an obvious
   message. Pick one name and use it in the policy, the workflow and the environment.

Then releasing is a tag:

```sh
git tag v0.1.4-alpha
git push origin v0.1.4-alpha
```

The workflow verifies the tag matches the version nbgv computes, runs `make ci` so nothing is
published that would not pass a merge gate, attests the packages, and pushes. `--skip-duplicate`
makes a re-run idempotent, and the `.snupkg` is pushed automatically alongside the `.nupkg`.

> **Attestations are generated in this workflow, against the artifact it is about to push.**
> An attestation binds a *digest*, so it has to be produced from the exact bytes that reach
> nuget.org. Re-packing after attesting, or pushing a different run's output, leaves claims that
> silently do not describe the package anyone installs. If you restructure this workflow, keep
> pack → attest → push in one run.

Run `make pack` locally and inspect the `.nupkg` before your first tag. A wasted version number
cannot be reclaimed.

#### Publishing from several repositories

Policies scope on two independent axes, and only one of them takes a pattern.

**Repository: no wildcards.** A policy matches exactly one `Repository Owner` + `Repository` +
`Workflow File` (+ `Environment`). So *one policy per repository* — standing up a second
publishing repo means adding a second policy, not editing the first.

Because every repository built from this template ships the same `publish.yml`, only the
`Repository` field differs between them:

| Owner | Repository | Workflow File | Environment | Package scope |
| --- | --- | --- | --- | --- |
| `you` | `first-lib` | `publish.yml` | `release` | `You.First*` |
| `you` | `second-lib` | `publish.yml` | `release` | `You.Second*` |

That consistency is convenient — one less thing varying per repo — and it is also why renaming
`publish.yml` in any one of them quietly breaks that repo alone.

**Packages: globs, and action scopes.** A policy is owned by a user or an organisation, and by
default *applies to every package that owner owns*. Its **Scopes** narrow this: a glob pattern
selecting which package ids the policy covers, and which actions it permits — publishing new
packages, versus new versions of existing ones.

Two things follow, and both are worth doing:

- **Set the package glob.** Left at its default, each repository's policy can publish anything
  you own, so a mistake or compromise in one repo reaches unrelated packages. Narrowing each
  policy to the ids that repository actually owns costs nothing and contains the blast radius.
- **Drop the "new packages" scope once a repository has published.** An
  [ID prefix reservation](#id-prefix-reservation) stops *other people* creating ids under your
  prefix; it does not stop your own over-broad policy from creating them. After the first
  publish that permission is no longer needed.

The two mechanisms sit at different levels, which is what makes this manageable: a **prefix
reservation is per nuget.org owner** and covers every package from every repository — you apply
for it once — while **policies are per repository** and are the recurring step.

If you later move nuget.org ownership to an organisation, both the reservation and the policies
need to belong to that organisation. Note that an organisation-owned policy goes **inactive** if
the member who created it leaves the org, and becomes active again when they are re-added.

#### ID prefix reservation

This repository's package id sits under the reserved prefix **`mxwlf.*`**. A
[reservation](https://learn.microsoft.com/en-us/nuget/nuget-org/id-prefix-reservation) does two
things: packages matching it show the "reserved prefix" indicator on nuget.org and in Visual
Studio, and — more usefully — **nuget.org rejects any matching id submitted by anyone else**. It
is the mechanism that keeps someone from publishing a convincing lookalike.

It is not self-service. Email **account@nuget.org** with your nuget.org owner display name and
the prefixes you want. The
[criteria](https://learn.microsoft.com/en-us/nuget/nuget-org/id-prefix-reservation#id-prefix-reservation-criteria)
the NuGet team weighs are whether the prefix clearly identifies its owner, whether it is too
common or generic to belong to one owner (prefixes shorter than four characters are discouraged),
and whether leaving it unreserved would cause confusion. Existing published packages are not a
stated requirement, though identity questions may follow.

Two things in this repository exist partly to satisfy the associated
[publishing best practices](https://learn.microsoft.com/en-us/nuget/nuget-org/id-prefix-reservation#id-prefix-reservation-criteria):

- **`$(Authors)` is `mxwlf`, matching the prefix.** The guidance asks for identifying properties
  that are clear and consistent, *especially the package author*, so the author and the prefix
  agreeing is the point — this is why `$(Authors)` is the publishing handle while `$(Copyright)`
  carries the legal name.
- **The licence is declared with the `license` element, not `licenseUrl`.**
  `PackageLicenseExpression` produces `<license type="expression">MIT</license>`; the `licenseUrl`
  that also appears in the nuspec is NuGet's own back-compatibility shim, not something this
  repository sets.

### Attestations

Every push to `main` records two [artifact
attestations](https://docs.github.com/en/actions/security-for-github-actions/using-artifact-attestations/using-artifact-attestations-to-establish-provenance-for-builds)
against the packages it produced:

- **Build provenance** — a Sigstore-backed statement that this artifact digest was built by this
  workflow, from this commit, on this runner.
- **SBOM** — binds the SPDX document `make sbom` produced to the same artifacts, so the dependency
  inventory is something a consumer can verify rather than take on trust.

Verify either one with the GitHub CLI, against the `.nupkg` **as the build produced it** — the
`package` artifact on the run, or the file `make pack` leaves in `artifacts/package/`:

```sh
gh attestation verify mxwlf.net.library.example.0.1.18-alpha.nupkg --repo mxwlf/dotnet-template
gh attestation verify … --predicate-type https://spdx.dev/Document      # the SBOM one
```

> **A package downloaded from nuget.org will NOT verify, and that is not a fault.** nuget.org
> applies its own **repository signature** on upload, which adds a `.signature.p7s` entry to the
> archive — for this package, 8,690 bytes became 21,766. An attestation binds a *digest*, so
> rewriting the archive breaks the match by design. Measured, not assumed: `gh attestation verify`
> exits 0 against the built artifact and 1 against the same version fetched from nuget.org.
>
> So these attestations are evidence about **what this repository built**, verifiable up to the
> moment of upload. They are not a signal a consumer can check against the copy the registry
> serves. For that, the in-ecosystem mechanisms are nuget.org's repository signature — automatic,
> and the reason the bytes differ — and an author signature, which needs a certificate. See
> [Code signing](#code-signing-what-is-and-is-not-covered).
>
> The rule that pack, attest and push stay in one run still holds: it keeps the attested artifact
> identical to the one *submitted*, so the chain from commit to upload is unbroken. Only the final
> server-side signing hop is outside it.

Because of that, `publish.yml` also creates a **GitHub Release** for the tag and attaches the
`.nupkg` and `.snupkg` *as built*. Those assets are byte-identical to what was attested, which
makes the Release the one public place the attestations can be checked:

```sh
gh release download v0.1.18-alpha --pattern '*.nupkg'
gh attestation verify mxwlf.net.library.example.0.1.18-alpha.nupkg --repo mxwlf/dotnet-template
```

The release step runs **last**, after the push. Nothing may stand between a built package and
nuget.org, so if creating the Release fails the worst case is a missing Release rather than a
missing package. It is idempotent — a re-run replaces the assets rather than failing with HTTP 422
`already_exists`, the documented trap with release actions — and it marks tags containing a hyphen
(`0.1.18-alpha`) as prereleases so they do not display as the latest stable version.

This is also why the publish job holds `contents: write` while the CI workflow stays
`contents: read`: creating a Release requires it, and this job only ever runs on a tag, which
already requires push access. The untrusted-input concern that justified giving CI's attestation
step its own job does not apply here.

> **The digest is the whole point, and it is easy to invalidate.** An attestation is a
> claim about exact bytes. The `attest` job therefore downloads the artifact the build
> already uploaded rather than re-packing, and **a publish step must push that same
> file.** A release that runs its own `dotnet pack` produces a different digest, and
> every attestation silently stops applying to the thing people actually install.

What attestations are *not*: a NuGet signature. NuGet clients do not read them, and they
do not make a package show as author-signed. nuget.org applies its own **repository**
signature to everything on upload, independently. An **author** signature is a separate
mechanism needing a code-signing certificate — see
[Code signing](#code-signing-what-is-and-is-not-covered).

> **Private repositories skip this.** Artifact attestations are not available for user-owned
> private repositories, and attempting one there does not warn — it fails the job:
>
> ```
> Error: Failed to persist attestation: Feature not available for user-owned private repositories.
> ```
>
> Both workflows therefore condition their attestation on `!github.event.repository.private`, so a
> repository that goes private does not need its CI edited and one that goes public starts
> attesting again on its own. On GitHub Enterprise Cloud, where attestations *do* work for private
> repositories, drop that condition. Note the knock-on: with no attestations, the `.nupkg` attached
> to a GitHub Release is still the artifact that was built, but there is nothing to verify it
> against.

Two implementation notes, both deliberate:

- **The attestations live in their own job.** They need `id-token: write` and
  `attestations: write`; the build job is held at `contents: read` and runs on every pull
  request, including untrusted ones. Splitting them keeps the build at least privilege
  and grants the elevated token only to a job that runs afterwards and does nothing but
  make claims about what the build already produced.
- **Only pushes to `main` are attested.** An attestation is a permanent public record
  about a digest; pull-request builds produce packages nobody can install, so attesting
  them would assert things about artifacts that exist nowhere. Release tags should be
  added to that condition when a release workflow exists.

### Code signing: what is and is not covered

| | Status |
| --- | --- |
| Commit and tag signing | **In use** — commits are GPG-signed and GitHub-verified, and `main` requires it (`required_signatures`) |
| Build provenance attestation | **In use** — see above |
| SBOM attestation | **In use** — see above |
| nuget.org repository signature | **Automatic** on upload, nothing to configure |
| NuGet **author** signature | **Not configured** — needs a code-signing certificate |
| Authenticode on the assemblies | **Not configured** — needs a code-signing certificate |
| Strong naming (`SignAssembly`) | **Not configured, and deliberately so.** It is an assembly *identity* mechanism, not a security one, and the key cannot be kept secret in a public repository. Add it only if a consumer requires it |

The two certificate-based rows are unconfigured because a certificate is the hard part:
since the CA/Browser Forum baseline changed in June 2023, code-signing private keys must
live on FIPS 140-2 Level 2+ hardware, so there is no `.pfx` to drop into a CI secret. The
realistic routes are a managed service (Azure Trusted Signing), a free OSS programme
(SignPath Foundation, which requires a clear OSS license), or a commercial certificate
with your own token or cloud HSM.

If you add author signing, it belongs in the release workflow rather than `make ci` —
`make ci` must run identically on a laptop with no secrets — and the signature should be
timestamped, or it stops verifying once the certificate expires.

### The SBOM

`make sbom` writes an SPDX 2.2 document to
`artifacts/sbom/_manifest/spdx_2.2/manifest.spdx.json` using
[Microsoft's sbom-tool](https://github.com/microsoft/sbom-tool), pinned in
[`.config/dotnet-tools.json`](.config/dotnet-tools.json) like every other local
tool. It runs as part of `make ci`, so every pull request produces one and a
dependency added without its lockfile updated shows up immediately. The document's
version comes from `nbgv`, so it is derived from the commit rather than hand-set.

**It describes the packages, not the build tree.** `sbom` depends on `pack`, so its
file inventory is the `.nupkg` and `.snupkg` in `artifacts/package/<configuration>/`
— two files — and its dependencies are scoped to `$(SBOM_PROJECT)`, the project that
ships. A solution with several packable projects wants one SBOM per package:
override `SBOM_PROJECT` and `SBOM_BUILD_DROP` per invocation.

That scoping is the whole point, and it is worth seeing what the alternative looked
like. Pointed at the build tree instead, the same SBOM inventoried **336 files, 328
of them the test project's output**, and 46 packages including every test-only
dependency. The shipped library accounted for 8 files and 12 packages.

The shipped package appears as a component of its own SBOM, listed twice — once
detected from the `.nupkg` and once from the `.snupkg`, since both carry the same
package identity. Same name and version, so it is noise rather than a contradiction.

One characteristic remains, and it is a property of how .NET SBOMs are built rather
than a bug to file: **analyzers and other build-only packages are listed.**
SonarAnalyzer, Roslynator, SourceLink and `nbgv` are real `PackageReference`s in
`project.assets.json`, and detection cannot distinguish a compile-time dependency
from one that ships. Read the result as *what this build consumed*, which is
accurate, rather than *what the package contains*.

Note that `SBOM_COMPONENT_PATH` must resolve under `artifacts/obj` and not `src/`.
`UseArtifactsOutput` relocates `obj/` out of the project directories, and a
component path with no `assets.json` under it produces a **valid SBOM containing
zero packages** rather than an error — a quiet failure worth knowing about.

License enrichment (`-li`) is deliberately not enabled: it calls the
ClearlyDefined API, which would put a third-party service on the critical path of
a green build.

An SBOM is an inventory, not a vulnerability scan. It is what makes scanning
possible; CodeQL and Dependabot are the scanning layer.

### Determinism

- `pre-commit` is pinned in [`requirements-dev.txt`](requirements-dev.txt) and
  installed into an isolated `.venv/`, so CI and laptops run the same version
  regardless of what is installed globally.
- Hook versions are pinned via `rev` in
  [`.pre-commit-config.yaml`](.pre-commit-config.yaml).
- Both CI stubs pin the interpreter used to build `.venv` (currently 3.14) for
  reproducible runs. Any `>= 3.10` works; the pin is not a hook requirement.
- Both stubs pin the runner/agent image (currently `ubuntu-24.04`) instead of
  using `ubuntu-latest`. That label is remapped to a new Ubuntu release
  periodically, which would move the build environment on the platform's
  schedule rather than yours.
- Both stubs cache pre-commit hook environments keyed on the config file, so
  unchanged hooks are not rebuilt.

Bump these versions deliberately when you want to upgrade.

## Make targets

| Target | Description |
| --- | --- |
| `make help` | Show available targets (default when running `make`). |
| `make setup` | Create `.venv`, then configure the repo to use the shared git config and pre-commit hooks. |
| `make venv` | Create/update `.venv` from `requirements-dev.txt` (no-op when up to date). |
| `make ci` | Run the full CI check suite — the single command CI/CD pipelines invoke. Runs identically locally. |
| `make lint` | Run all pre-commit hooks against all files. |
| `make build` | Build every project with analyzers enforced. |
| `make prune-stale-output` | Remove `artifacts/` output for projects that no longer exist. A dependency of `build`: output outlives a deleted project, and `make test` discovers executables by glob, so an orphaned test assembly would keep running and inflating coverage. |
| `make test` | Run every test project, writing the TRX report and Cobertura coverage into `artifacts/test-results/`. On GitHub Actions, also emits the test report (log groups, failure annotations, job summary) and updates the history snapshot in `artifacts/test-history/`. |
| `make coverage` | Merge every module's Cobertura file into one report in `artifacts/coverage/` (Cobertura, markdown, text, HTML) and fail below `COVERAGE_MIN_LINE`. On GitHub Actions, appends the merged figures to the job summary. |
| `make pack` | Produce the NuGet packages (`.nupkg` + `.snupkg`) into `artifacts/package/`. Part of `make ci`. |
| `make sbom` | Generate an SPDX 2.2 SBOM for those packages into `artifacts/sbom/`. Part of `make ci`. |
| `make tools` | Restore the pinned local .NET tools from `.config/dotnet-tools.json`. |
| — | Versions come from [`version.json`](version.json) + git history; there is no version make variable. See [Versioning](#versioning). |
| `make clean` | Remove `.venv` (rebuild with `make setup`). |
| `make check-python` | Verify the interpreter used to build `.venv` is `>= 3.10`. |
| `make rulesets-apply` | Create/update this repo's GitHub ruleset from `.github/rulesets/`. Needs `gh`. |
| `make rulesets-diff` | Report drift between `.github/rulesets/` and the live ruleset. Needs `gh`. |
| `make rulesets-export` | Overwrite `.github/rulesets/` with the live ruleset. Needs `gh`. |

Override the interpreter for any of these with `PYTHON=...`, e.g.
`make setup PYTHON=python3.12`.

## Troubleshooting

- **`` `pre-commit` was not found in this repository's .venv/ ``** — the venv is
  missing (fresh clone, new worktree, or `make clean`). Run `make setup` from
  the repository root.
- **`Error: python3 ... but >= 3.10 is required`** — the interpreter make would
  use to build `.venv` is too old. Install any newer Python and either put it on
  your `PATH` as `python3` or pass it explicitly: `make setup PYTHON=python3.12`.
  You do **not** need to change your system default.
- **commitizen / sync-pre-commit-deps fails to build / `requires a different Python`** —
  the hooks are being run by a pre-commit *outside* `.venv/` (a global install
  under an old interpreter). Confirm `make setup` has been run and that
  `git config --get core.hooksPath` prints `.githooks/`; if a stray
  `pre-commit install` overwrote `.git/hooks/`, delete those generated files so
  `core.hooksPath` takes effect again. See
  [Requirements](#requirements) for why the interpreter is chosen this way.
- **`.venv` broke after a Python upgrade** (e.g. Homebrew replaced the
  interpreter it was built from) — recreate it: `make clean && make setup`.
- **Hook is ignored / not running** — confirm `make setup` has been run
  (`git config --get include.path` should print `../.gitconfig`) and that the
  hook scripts in `.githooks/` are executable.

## License

[MIT](LICENSE).

The packages declare it with `PackageLicenseExpression` in
[`Directory.Build.props`](Directory.Build.props), which embeds the SPDX identifier and a
`licenseUrl` in the nuspec. That is why the `LICENSE` file itself is not packed: an
expression is self-describing, and `PackageLicenseFile` is only needed for a licence NuGet
cannot name.
