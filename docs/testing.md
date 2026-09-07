# Testing Strategy

## Purpose

This project is a Windows shell-adjacent utility. The highest-risk failures are not ordinary button-click bugs; they are interaction, DPI, monitor, taskbar, lifecycle and shell-integration edge cases.

Every roadmap PR should define the smallest relevant subset of this matrix and record manual results where automation cannot reasonably cover Windows shell behavior.

---

# Test layers

## 1. Unit tests

Prioritize logic that can be separated from Windows UI:

- configuration/schema migration;
- stable ID generation and lookup;
- duplicate-name handling;
- import/export identity behavior;
- activation command parsing;
- popup state transitions;
- popup geometry calculations;
- monitor selection;
- launch-model resolution;
- running-window matching heuristics where inputs can be modeled;
- settings serialization.

## 2. Integration tests

Where practical, cover:

- shortcut parsing;
- `.lnk` arguments and working directory;
- config persistence/atomic replacement;
- old config fixture migration;
- single-instance activation routing;
- icon cache behavior;
- process/window matching against controlled test applications.

## 3. Manual Windows acceptance tests

Required for behavior that depends on Explorer/taskbar/desktop composition.

Record:

- Windows version/build;
- AppGroup commit/release;
- monitor topology;
- scaling settings;
- taskbar position/alignment/autohide;
- pass/fail notes.

---

# Baseline environment

At minimum, maintain a documented primary development/test environment running a currently supported Windows 11 build.

Before major releases, test against more than one Windows 11 release/build where practical.

Do not claim general Windows 10 support unless it is intentionally supported and tested.

---

# Core regression suite

These tests apply to nearly every meaningful release.

## Group management

- [ ] Create group.
- [ ] Edit group.
- [ ] Delete group.
- [ ] Duplicate group.
- [ ] Rename group.
- [ ] Two groups may share the same display name without mixing state.
- [ ] Add/reorder/remove items.
- [ ] Subgroups continue to resolve after parent/child rename.
- [ ] Broken item target does not hide/corrupt the group.
- [ ] Restart preserves all group state.

## Taskbar activation

- [ ] Pin group to taskbar.
- [ ] Click opens the correct group.
- [ ] Repeated click behaves deterministically.
- [ ] Rapid Stack A → Stack B switching shows correct content.
- [ ] Renaming a group does not make its stable-ID shortcut point to another group.
- [ ] Duplicate group names remain independent.
- [ ] Explorer restart does not permanently break group shortcuts.

## Popup lifecycle

- [ ] Open from taskbar.
- [ ] Close by second click where designed.
- [ ] Close by outside click.
- [ ] Close with Escape.
- [ ] Launching item closes according to current product rule.
- [ ] Entering subgroup does not create an orphan popup.
- [ ] No popup remains offscreen after display configuration changes.
- [ ] No hidden popup incorrectly captures input/focus.

## Application launch

- [ ] Win32 `.exe`.
- [ ] `.lnk` shortcut.
- [ ] Arguments preserved.
- [ ] Working directory preserved.
- [ ] Run-as-administrator behavior.
- [ ] Folder.
- [ ] Document/file.
- [ ] URL.
- [ ] Steam `.url` where supported.
- [ ] UWP/MSIX/PWA route where supported.

---

# Performance acceptance

## Warm taskbar activation

Instrument/debug-measure:

```text
activation received
→ group resolved
→ content ready
→ placement resolved
→ first visible frame
```

Acceptance principle: no arbitrary sleep should be part of the normal warm hot path.

Manual feel test:

- [ ] 10 repeated opens feel immediate and consistent.
- [ ] No spinner on normal warm activation.
- [ ] No first-group stale-content flash.
- [ ] No progressive slowdown after repeated use.

## Background impact

When idle:

- [ ] CPU usage should be effectively negligible.
- [ ] No high-frequency global window enumeration.
- [ ] Hover mode, when enabled, does not materially increase idle CPU when pointer is away from taskbar.
- [ ] High polling-rate mouse does not cause event storms or UI stutter.

---

# Display and DPI matrix

## Single monitor

- [ ] 1920×1080 @ 100%.
- [ ] 2560×1440 @ 100% or 125%.
- [ ] High-DPI display @ 150% or greater.

## Dual monitor — same DPI

- [ ] Secondary right of primary.
- [ ] Secondary left of primary (negative X coordinates).
- [ ] Secondary above primary (negative Y possible).

## Dual monitor — mixed DPI

Examples:

```text
Primary:   1920×1080 @100%
Secondary: 3840×2160 @150%
```

and reverse which display is primary.

Verify:

- [ ] popup size is correct on each display;
- [ ] position uses triggering display;
- [ ] popup does not jump to primary;
- [ ] pointer-to-popup relationship is visually correct;
- [ ] moving between displays and reopening updates DPI correctly.

## Runtime display changes

- [ ] Disconnect secondary display while app is running.
- [ ] Reconnect secondary display.
- [ ] Change primary monitor.
- [ ] Change scaling and sign in/restart as required by Windows.
- [ ] Sleep/resume with multiple monitors/dock.

---

# Taskbar matrix

Test configurations supported by the Windows environment/product.

## Alignment

- [ ] Centered Windows 11 taskbar icons.
- [ ] Left-aligned taskbar icons.

## Taskbar behavior

- [ ] Normal visible taskbar.
- [ ] Autohide enabled.
- [ ] Autohide reveal/open interaction.
- [ ] Explorer restart.

## Edge/location

Where the environment or supported shell configuration allows:

- [ ] Bottom.
- [ ] Top.
- [ ] Left.
- [ ] Right.

Third-party taskbar replacements/custom bars are compatibility targets only when explicitly accepted; they must not silently redefine core geometry rules.

---

# Hover-mode matrix

Applies once PR 5 lands.

## Basic dwell

- [ ] Enter stack button and remain → opens after configured dwell.
- [ ] Cross stack quickly → does not open unintentionally.
- [ ] Enter stack then move away before dwell → remains closed.

## Stack-to-popup bridge

- [ ] Move from taskbar button into popup → popup stays open.
- [ ] Move from popup back to triggering button → popup stays open.
- [ ] Leave both regions → popup closes after grace period.
- [ ] Small imperfect diagonal movement does not close popup immediately.

## Adjacent stacks

- [ ] Move Stack A → Stack B slowly → controlled switch.
- [ ] Sweep across A/B/C quickly → no flash cascade.
- [ ] Hover detection never prevents click.

## Failure fallback

Simulate/force unavailable taskbar element discovery where practical:

- [ ] hover quietly becomes unavailable;
- [ ] normal taskbar click still works;
- [ ] no repeated error dialogs/log spam.

## Mouse hardware

- [ ] Standard mouse.
- [ ] Touchpad.
- [ ] High polling-rate gaming mouse if available (1000 Hz+; ideally test 8000 Hz where hardware exists).

---

# Running-window tracking matrix

Applies once PR 7 lands.

## Process/window lifecycle

- [ ] Launch app externally → running indicator appears.
- [ ] Launch app from group → indicator appears.
- [ ] Close last window → indicator clears.
- [ ] App with background process but no meaningful top-level window is handled according to matching policy.
- [ ] New secondary window appears → tracker updates.
- [ ] Window destroyed → tracker updates.
- [ ] Minimize/restore updates state if represented.
- [ ] Foreground window updates active state if represented.

## Matching

Test:

- [ ] one process / one window;
- [ ] one process / multiple windows;
- [ ] multiple processes / same executable;
- [ ] browser/PWA-style identity where available;
- [ ] UWP/MSIX identity;
- [ ] same executable represented by two group items with different arguments.

False-positive rule: if matching is uncertain, prefer launching normally or presenting ambiguity over activating an unrelated application window.

---

# Launch-or-activate matrix

Applies once PR 8 lands.

- [ ] Not running → launch.
- [ ] One matching normal window → activate.
- [ ] Matching window minimized → restore and activate.
- [ ] Multiple matching windows → follow defined chooser/MRU policy.
- [ ] Explicit force-new-instance bypasses activation behavior.
- [ ] App rejects multiple instances gracefully.
- [ ] Elevation boundary does not cause uncontrolled focus tricks or crashes.

---

# Theme / visual / animation matrix

- [ ] Windows light theme.
- [ ] Windows dark theme.
- [ ] App-specific light/dark preference if retained.
- [ ] Accent color change.
- [ ] Wallpaper change when accent follows wallpaper.
- [ ] High contrast.
- [ ] Reduced motion / animations disabled.
- [ ] Content animation enabled.
- [ ] Content animation disabled.

Watch specifically for:

- flicker;
- blurry moving text;
- incorrect layout after disabling animation;
- CPU/mouse stutter on wallpaper/accent changes.

Relevant upstream issue families include #61, #75, #77 and #89.

---

# Lifecycle matrix

## Startup

- [ ] Start application normally.
- [ ] Start at Windows sign-in if enabled.
- [ ] No duplicate resident hosts.
- [ ] First stack activation after sign-in works.

## Explorer

- [ ] Restart `explorer.exe` while AppGroup remains running.
- [ ] Taskbar returns.
- [ ] Stack shortcuts remain/recover as expected.
- [ ] Hover/taskbar locator refreshes its handles/elements.
- [ ] Exiting AppGroup must not terminate Explorer.

## Sleep/resume

- [ ] Sleep.
- [ ] Resume.
- [ ] Group activation works.
- [ ] Window tracking resumes/reconciles.
- [ ] Display topology refreshes.

## Shutdown/uninstall

- [ ] Exit app cleanly.
- [ ] No child/background process remains unexpectedly.
- [ ] Uninstall removes application files intended to be removed.
- [ ] Startup registration is removed.
- [ ] User data retention/deletion follows documented policy.

---

# Accessibility matrix

- [ ] Keyboard-only navigation.
- [ ] Escape closes popup.
- [ ] Arrow-key navigation is logical.
- [ ] Enter activates selected item.
- [ ] Focus does not disappear behind popup.
- [ ] Screen reader exposes meaningful names.
- [ ] Running state is available accessibly, not only as a colored dot.
- [ ] High contrast remains usable.
- [ ] Text scaling does not clip essential content.
- [ ] Reduced-motion preference is honored.

---

# Migration tests

Every persistent schema migration PR must include fixture-driven tests.

For each supported old fixture:

1. retain an unmodified historical config sample in test resources;
2. load through migration;
3. assert group/item counts;
4. assert names/icons/settings preserved;
5. assert stable IDs assigned/preserved correctly;
6. assert subgroups still resolve;
7. save and reload migrated state;
8. confirm a second load does not migrate again or mutate identity.

Never test migration only against a handcrafted representation of the new schema.

---

# PR acceptance template

Implementation PR descriptions should include:

```text
Automated tests:
- ...

Manual Windows tests:
- Windows build:
- Monitor/DPI configuration:
- Taskbar configuration:
- Results:

Known limitations / deferred cases:
- ...
```

A PR that changes taskbar, activation, popup, DPI, lifecycle or persistent data behavior is not complete merely because the solution compiles.

---

# Release gate

Before a tagged stable release:

- [ ] CI green.
- [ ] Relevant unit/integration suite green.
- [ ] Primary Windows 11 acceptance suite green.
- [ ] Secondary/mixed-DPI tests completed.
- [ ] Explorer restart test completed.
- [ ] Startup/sign-in test completed.
- [ ] Upgrade/migration test completed.
- [ ] Uninstall test completed.
- [ ] Known shell-integration limitations documented.