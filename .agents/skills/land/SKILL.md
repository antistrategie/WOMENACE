---
name: land
description: >-
  Land WOMENACE changes on the thread's current branch and the matching branch
  on origin. Invoke only when the user explicitly requests landing, including
  Land Changes or /land. Do not invoke for review, preparation, passing checks,
  or skill installation alone.
disable-model-invocation: true
metadata:
  delta-action: land
---

# Land WOMENACE changes

## Intent and destination

An explicit invocation authorises the complete landing workflow below. Proceed
without asking again whether to commit or push. A visible `/land` invocation is
an explicit landing request, not a reason to wait for another request.

This skill applies only to WOMENACE. Work in the attached Delta checkout. Capture
its current branch with `git symbolic-ref --quiet --short HEAD`. That branch is
the destination throughout the operation, both locally and on `origin`. Do not
substitute `main`, the remote default branch, an upstream with a different branch
name, or the branch checked out in the user's primary checkout.

The publishing remote is `origin`, currently
`git@github.com:antistrategie/WOMENACE.git`. Verify its fetch and push URLs before
publication. `local` is Delta's backlink to the primary checkout, not a publishing
destination. Do not change the primary checkout or its branches.

Stop for detached HEAD, a missing or unexpected publishing remote, an existing
unfinished Git operation, or genuinely unclear change scope. Do not guess a
destination. If the matching branch does not yet exist on `origin`, creating that
same remote branch is the landing operation.

## Preflight and scope

1. Read the applicable `AGENTS.md` and any nested instructions for the changed
   paths. Inspect any contribution or submission policy present in the current
   revision. Honour applicable signing, authorship, documentation and test
   requirements without inventing requirements that the repository does not have.
2. Inspect `git --no-optional-locks status --short`, staged and unstaged diffs,
   untracked files, recent commits, and the outgoing commit range. Identify the
   exact thread changes to land. Preserve unrelated edits and commits. If they
   cannot be separated safely, ask rather than including, stashing or discarding
   them.
3. Check that Git identity and publication authentication are available. Use the
   installed Git and GitHub CLI directly. Never read or print credential values.
   Honour configured signing, and stop if required signing cannot succeed.
4. Check the destination's current push restrictions. For this GitHub repository,
   inspect active branch rules and classic branch protection using `gh api` under
   `repos/antistrategie/WOMENACE`. URL-encode the branch name as one path component
   when addressing branch endpoints. A confirmed unprotected branch is different
   from an authentication error or an unverifiable policy.
5. This is a direct-push, locally verified workflow. Do not create pull requests,
   request reviews, or start or wait on CI. No CI or review requirements were
   configured when this skill was installed. If such requirements are introduced,
   report that this workflow is blocked rather than bypassing them or silently
   changing to a pull-request workflow.

Do not deploy the mod, launch MENACE, publish releases or packages, modify branch
protections, force-push, or rewrite shared history. Missing tools or game assets
are blockers, not permission to install software or modify another checkout.

## Prepare and integrate

1. Record the current branch and starting commit. Stage only reviewed paths for
   the intended change, including intentional new files and deletions. Do not use
   a blanket add when unrelated files are present. Check the staged diff for
   secrets, generated caches and accidental changes.
2. Commit the intended work with a concise, one-line conventional message.
   Use `GIT_EDITOR=true git commit -m` with the chosen message supplied explicitly.
   Do not add AI attribution or `Co-authored-by` trailers. If the intended changes
   are already committed, inspect and reuse those commits.
3. Determine whether `refs/heads/$branch` exists using
   `git ls-remote --exit-code --heads origin "refs/heads/$branch"`. Distinguish
   exit status 2, meaning no matching ref, from connection or authentication
   failures. For an existing branch, fetch that exact ref with
   `git fetch --no-tags origin "refs/heads/$branch"` and record `FETCH_HEAD`.
4. Integrate the fetched commit into the current branch with a normal merge:
   `GIT_EDITOR=true git merge --no-edit -m "chore: integrate remote $branch" "$remote_tip"`.
   This fast-forwards when possible and preserves existing history when the
   branches have diverged. Do not rebase or amend published commits.
5. Resolve clear-cut conflicts automatically by understanding both changes and
   preserving their intended behaviour. Do not use blanket ours/theirs choices.
   Stage explicit resolved paths and complete the merge with an explicit commit
   message and `GIT_EDITOR=true`. Pause only when intent is ambiguous, a
   resolution would discard unrelated work, or the result cannot be verified.
6. Inspect the complete outgoing range. Do not publish unrelated local commits
   merely because they are already on the branch. Verification must run against
   the integrated candidate, not a working tree containing unrelated source edits.
   If unrelated edits prevent that, stop and ask how to isolate them.

## Verify the final candidate

All applicable required checks must pass before landing. Pending, failed,
missing or unverifiable checks are not success. Run the applicable local checks
after integration and conflict resolution. Re-run affected checks after any
further code or content change, including changes needed to fix verification.
Recorded evidence must apply to the final relevant code and assets, not an
earlier implementation.

Use verification in proportion to the change, as required by `AGENTS.md:64-72`.

| Change | Verification | Source of the command or requirement |
| --- | --- | --- |
| Every change | Check the complete outgoing diff with `git diff --check` using the recorded remote tip as its base. For a new remote branch, use the recorded starting commit for the thread's changes and inspect the history being published. | Git whitespace validation |
| Documentation or skill only | Validate changed links, instructions and skill frontmatter. No game build is needed solely for prose or skill metadata. | `AGENTS.md:72` |
| Preparation hook or its tests | Run `python3 -m unittest discover -s tests -p test_prepare.py -v`. For changes to cache selection or build setup, also exercise preparation and a compile in a fresh managed checkout, or use recorded evidence for the unchanged final hook implementation. | `tests/test_prepare.py:18-221` implements the unittest suite, `.agents/prepare:125-149` is the executable entry point, `.agents/README.md:52-60` describes the workflow check |
| KDL, C#, prefabs, shaders or asset references | Run `mise run --skip-tools compile`. | `mise.toml:1-3`, `AGENTS.md:72` |
| C# code or C# tests | Run `mise run --skip-tools lint`. Run `mise run --skip-tools test` when Procurement or ShopRendering behaviour is affected. The test task includes compilation, so a separate compile is unnecessary when it runs. | `mise.toml:19-21` and `mise.toml:27-35`, `tests/Procurement/Program.cs` accepts `--rates`, `tests/ShopRendering/Program.cs:5-12` accepts the two assembly paths supplied by the task |
| Runtime patches or UI changes | Require evidence that the affected in-game path was exercised. Reuse relevant verification recorded during development. If evidence is missing or invalidated by the final changes, ask the user to verify manually and wait for the result. Do not claim that compilation or a bridge inspection substitutes for that test. | `AGENTS.md:72` |

For the preparation workflow check, use the existing attached checkout when it
is still suitable. If a genuinely fresh checkout is needed, ask for it to be
attached rather than editing an unregistered checkout. Run `.agents/prepare`
only while both its cache source and destination are idle. Its implementation
and `.agents/README.md:25-36` define the copy and local configuration behaviour.
Do not copy caches by another mechanism or share writable Unity state.

### Local build prerequisites

- The mise tasks use the Jiangyu CLI under
  `${JIANGYU_DIR:-../jiangyu}/src/Jiangyu.Cli/bin/${JIANGYU_BUILD:-Debug}/net10.0/jiangyu.dll`.
  Preserve explicit overrides and use the existing local build. The definition
  is `mise.toml:1-7`. A nested Delta checkout can use `.agents/prepare` to populate
  ignored local configuration, as implemented by
  `.agents/prepare:109-122`. Do not edit or rebuild the external Jiangyu checkout
  as an implicit landing step.
- The CLI and test executables require .NET 10. The test targets are declared in
  `tests/Procurement/Procurement.Tests.csproj` and
  `tests/ShopRendering/ShopRendering.Tests.csproj`. WOMENACE runtime code targets
  .NET 6 through `code/Directory.Build.props:3-10`, which does not make the tests
  .NET 6 executables.
- Build and lint paths depend on the local game and SDK configuration described
  in `code/Directory.Build.props:12-31` and checked by
  `code/Directory.Build.targets`. Confirm the required assemblies and CLI build
  exist. If they do not, report the missing prerequisite instead of skipping a
  required check.
- Unity's editor version is pinned by `unity/ProjectSettings/ProjectVersion.txt`.
  Use the matching editor and the configured Jiangyu build. Treat editor-script
  drift and loader/CLI mismatches according to `AGENTS.md:94-96`.
- Keep terminal commands non-interactive. The installed mise supports
  `--skip-tools` to prevent automatic tool installation. After reviewing the
  checkout's mise configuration, use `MISE_TRUSTED_CONFIG_PATHS="$PWD"` for the
  individual mise command if local trust would otherwise prompt. Do not change
  global trust settings. The task bodies remain those in `mise.toml`.
- Do not run formatting automatically as part of landing. If verification
  requires a fix, make a scoped change, inspect it, commit it and repeat the
  relevant checks. Do not hide failures by excluding files or weakening checks.

## Push and confirm

1. Once verification passes, record the exact candidate SHA. Confirm that the
   current branch still matches the captured branch, the working tree has no
   uncommitted intended changes, and the publishing URL has not changed.
2. Read the destination ref again. If it has advanced since integration, fetch
   and integrate the new tip, then repeat affected verification before pushing.
   If the remote history was rewritten or its scope becomes unclear, stop rather
   than restoring old history unintentionally.
3. Push with `git push origin "HEAD:refs/heads/$branch"`. Never add a force flag.
   A non-fast-forward rejection means the destination changed. Safely integrate
   and reverify, then retry. Authentication failures or policy rejections are
   blockers, not permission to weaken controls.
4. Verify the actual destination with
   `git ls-remote --heads origin "refs/heads/$branch"`. It must equal the candidate
   SHA, or a fresh fetch must establish with `git merge-base --is-ancestor` that
   a newer destination tip contains the candidate. A prepared commit, a push to
   a different topic branch or a passing test run is not landing success.
5. Obtain the verified commit URL, when available, from
   `gh api "repos/antistrategie/WOMENACE/commits/$candidate" --jq .html_url`.
   Do not invent URLs, delete branches or change the user's primary checkout
   as cleanup.

## Report the outcome

When running in a subthread and `report_subthread_status` is available, report
the final landing result to the parent with that tool. Otherwise report it in
the current conversation.

- Use `status: "success"` only after verifying that the candidate reached the
  intended branch on `origin`. Keep the title to a few sentence-case words,
  such as `Landed on v0.11`. Keep the description to one short line containing
  the short SHA linked to its verified commit URL and a truthful local-check
  result.
- Use `status: "failure"` for an unsuccessful attempt or genuine blocker.
  State that the changes have not landed and identify the blocker. Ask any
  question in the conversation, not in the status event.
- There is no CI workflow to link by default. Do not label local verification
  as CI. Include a CI result link only if an actual relevant result exists and
  its URL and revision have been verified.
- Skill installation, prepared commits and intermediate checks are not landing
  success. Do not send an outcome event for installation or routine progress.
- Failure is not terminal. Continue safe recovery when permitted, and report an
  updated result after verifying the outcome.
