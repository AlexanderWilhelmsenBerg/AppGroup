# PR0 Baseline Record

## Upstream snapshot

This fork was created from `iandiv/AppGroup` on 2026-09-07.

The upstream `master` commit at the fork point was:

- `00ec0af70810e84fdcc3899c678e5f1dbd106680` — `Prevent duplicate mouse hook initialization`

PR0 intentionally does not change application behavior. Its code-affecting changes are limited to build/test infrastructure and solution metadata required to include the test project.

## Automated baseline

The CI contract for PR0 is:

1. run on a GitHub-hosted Windows runner;
2. install .NET 8;
3. restore the existing AppGroup WinUI project for `win-x64`;
4. build AppGroup in `Release` / `x64`;
5. restore the independent test project;
6. execute the test suite.

A passing `CI / Windows x64 build and tests` check establishes the automated baseline for the fork.

## Manual Windows 11 smoke baseline

A GUI/runtime smoke check cannot be treated as meaningful on a headless GitHub runner. Before PR1 changes configuration identity, verify the current build once on an interactive Windows 11 desktop.

Minimum smoke check:

- [ ] AppGroup starts normally.
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
