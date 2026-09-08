# PR0 Baseline Record

## Upstream snapshot

This fork was created from `iandiv/AppGroup` on 2026-09-07.

The upstream `master` commit at the fork point was:

- `00ec0af70810e84fdcc3899c678e5f1dbd106680` — `Prevent duplicate mouse hook initialization`

The latest stable upstream release at the fork baseline is `v1.5.0`. The selected upstream `master` snapshot is 11 commits ahead of that release, so the fork deliberately starts from current upstream source rather than downgrading to the older release commit.

Upstream derives the application version from the nearest Git tag. GitHub forks do not automatically expose upstream tags to CI/local clones, which initially caused this newer source to be mislabeled as `1.0.0` and made AppGroup incorrectly offer `v1.5.0` as an update. PR0 therefore fetches upstream release tags in CI, verifies the built EXE matches the nearest upstream release line, and provides a fork-safe `1.5.0` fallback when tags are unavailable.

PR0 intentionally does not change application runtime behavior. Its code-affecting changes are limited to build/test/version metadata infrastructure and solution metadata required to include the test project.

## Automated baseline

The CI contract for PR0 is:

1. run on a GitHub-hosted Windows runner;
2. fetch upstream AppGroup release tags;
3. install .NET 8;
4. restore the existing AppGroup WinUI project for `win-x64`;
5. build AppGroup in `Release` / `x64`;
6. verify the produced EXE reports the nearest upstream release version;
7. stage the exact Release `win-x64` output as a smoke-test bundle;
8. restore the independent test project;
9. execute the test suite;
10. upload the staged build as the `AppGroup-win-x64` workflow artifact.

A passing `CI / Windows x64 build and tests` check establishes the automated baseline for the fork. The uploaded artifact is retained for 14 days and is the preferred build for the manual PR0 smoke test so the interactive test exercises the same commit and build output that CI validated.

## Inherited compiler-warning baseline

The first CI baseline exposed 10 nullable-reference warnings in existing upstream AppGroup application source. PR0 does not modify those files, so the warnings are inherited baseline debt rather than regressions caused by this fork.

They are tracked separately in GitHub issue #2, `Baseline cleanup: eliminate inherited nullable-reference warnings`, with the goal of reaching a zero-warning build without changing runtime behavior. The warning cleanup is intentionally kept out of PR0 so the first Windows 11 runtime smoke test remains an unchanged-upstream application baseline.

## Manual Windows 11 smoke baseline

A GUI/runtime smoke check cannot be treated as meaningful on a headless GitHub runner. Before PR1 changes configuration identity, download the `AppGroup-win-x64` artifact from the successful PR0 workflow run, extract it, and verify the current build once on an interactive Windows 11 desktop.

Minimum smoke check:

- [ ] AppGroup starts normally from the extracted CI artifact.
- [ ] The app reports the expected upstream release line (`1.5.0` at the PR0 baseline), not the obsolete `1.0.0` fallback.
- [ ] No false update prompt to `v1.5.0` appears.
- [ ] Main configuration window opens.
- [ ] An existing group can be opened from its taskbar shortcut.
- [ ] Popup appears adjacent to the taskbar on the primary monitor.
- [ ] Launching one normal Win32 app from a group still works.
- [ ] Closing the popup/app behaves as before the fork.
- [ ] No unexpected `explorer.exe` restart or crash occurs.

Record the Windows version/build and result below when performed.

### Manual result

- Windows version/build: _pending_
- Tested commit: _pending_
- Result: _pending_
- Notes: _pending_

## What PR0 does not validate

PR0 does not claim broad behavioral compatibility across monitor layouts, mixed DPI, taskbar positions, autohide, UWP/PWA shortcuts, or nested groups. Those cases are tracked in `docs/testing.md` and become required as the relevant roadmap slices are implemented.
