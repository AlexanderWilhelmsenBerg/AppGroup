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

The corrected PR0 head `2d00b855c5e08d7e418a95a9de61afc0ec272d42` passed the complete CI lane, including upstream-tag fetch, application build, explicit version verification, tests, and artifact upload.

## Inherited compiler-warning baseline

The first CI baseline exposed 10 nullable-reference warnings in existing upstream AppGroup application source. PR0 does not modify those files, so the warnings are inherited baseline debt rather than regressions caused by this fork.

They are tracked separately in GitHub issue #2, `Baseline cleanup: eliminate inherited nullable-reference warnings`, with the goal of reaching a zero-warning build without changing runtime behavior. The warning cleanup is intentionally kept out of PR0 so the first Windows 11 runtime smoke test remains an unchanged-upstream application baseline.

## Manual Windows 11 smoke baseline

The user performed an interactive Windows 11 smoke test using a PR0 artifact produced before the version-stamping correction.

Observed result:

- [x] AppGroup started normally from the extracted CI artifact.
- [x] Main configuration window opened and the application was usable.
- [x] Group/taskbar popup behavior worked during the smoke test.
- [x] Launching from the group worked during the smoke test.
- [x] No unexpected `explorer.exe` restart or crash was reported.
- [ ] The artifact reported the expected upstream release line.
- [ ] No false update prompt appeared.

The two unchecked items were caused by the known PR0 version-stamping defect: the tested artifact identified itself as `1.0.0` and therefore offered `v1.5.0` as an update. That defect was subsequently corrected in CI. The corrected head now verifies the produced EXE as `1.5.0.x` automatically before artifact upload.

### Manual result

- Windows version/build: Windows 11; exact build not recorded.
- Tested commit: pre-version-fix PR0 artifact; exact artifact commit was not independently confirmed.
- Result: **Functional pass with version-stamping defect**.
- Notes: User reported the app was working; the only observed issue was the false `v1.5.0` update notice caused by the artifact being stamped `1.0.0`.

No additional manual retest is required before the warning-cleanup slice because the corrected version is now an explicit CI gate and the runtime behavior was already exercised successfully.

## What PR0 does not validate

PR0 does not claim broad behavioral compatibility across monitor layouts, mixed DPI, taskbar positions, autohide, UWP/PWA shortcuts, or nested groups. Those cases are tracked in `docs/testing.md` and become required as the relevant roadmap slices are implemented.
