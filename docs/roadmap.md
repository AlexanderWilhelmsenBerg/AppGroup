# AppGroup Fork Roadmap — Modern Windows 11 Bins-Style Taskbar Groups

## Purpose

This fork evolves AppGroup into a modern Windows 11 taskbar-grouping utility inspired by the interaction model of 1UP Industries Bins.

The goal is not to reproduce old Explorer internals. The goal is to preserve the experience that made Bins useful:

- a pinned taskbar group behaves like a natural part of Windows;
- the group popup appears immediately and in the correct place;
- click is reliable and hover can optionally open the group;
- running applications are visible inside the group;
- clicking a running application can activate its existing window instead of blindly launching another instance;
- the utility remains stable across Windows updates by avoiding Explorer injection or patching.

The guiding priority is:

**speed → reliability → predictable interaction → native feel → visual polish → experimental shell tricks**

---

## Product principles

1. **Do not inject into `explorer.exe`.**
2. **Do not patch or replace the Windows taskbar.**
3. Prefer documented Windows / Windows App SDK APIs where practical.
4. UI Automation may be used read-only for taskbar discovery if required.
5. Keep normal click activation fully functional even if hover integration fails on a future Windows build.
6. Use stable IDs for groups and items; names and paths are presentation/data, not identity.
7. Keep the resident process lightweight and make warm activation effectively instantaneous.
8. Preserve compatibility with existing AppGroup users and configurations where reasonably possible.
9. Keep upstream-compatible improvements modular so they can be proposed back to `iandiv/AppGroup` where appropriate.

See [architecture.md](architecture.md) for the architectural guardrails.

---

# Delivery roadmap

## Foundation milestone

The first milestone is intentionally conservative. It should improve the existing application substantially before any experimental hover/taskbar work begins.

### PR 0 — Fork baseline, documentation and guardrails

**Status:** In progress

**Goal:** Establish this fork as an intentional project without changing application behavior.

Deliverables:

- [x] Preserve upstream repository history and MIT license.
- [x] Add `docs/roadmap.md`.
- [x] Add `docs/architecture.md`.
- [x] Add `docs/testing.md`.
- [x] Add `docs/upstream.md`.
- [ ] Add/verify basic build CI for the existing solution.
- [ ] Establish a test project before major state/data migrations.
- [ ] Record the untouched-upstream build/runtime baseline.

Acceptance gate:

- The fork builds and runs without behavioral changes.
- Existing AppGroup functionality remains intact.

---

## PR 1 — Stable group and item identity

**Complexity:** Medium

**Why first:** AppGroup currently uses group names in activation and lookup paths. Upstream issue #69 demonstrates why that is unsafe: identically named groups can resolve incorrectly.

Upstream reference: https://github.com/iandiv/AppGroup/issues/69

### Target model

Groups receive an immutable identifier independent of display name.

Conceptually:

```text
Group
  Id
  Name
  Icon
  Appearance
  Items[]
```

Items should also gain stable identity:

```text
GroupItem
  Id
  Type
  DisplayName
  Target
  Arguments
  WorkingDirectory
  Icon
  RunAsAdministrator
```

### Requirements

- [ ] New groups receive stable unique IDs.
- [ ] Existing groups migrate automatically.
- [ ] Group rename does not alter identity.
- [ ] Duplicate group names are supported.
- [ ] Group shortcuts use ID-based activation.
- [ ] Existing name-based shortcuts remain temporarily compatible when resolution is unambiguous.
- [ ] Subgroups reference stable identity rather than names where possible.
- [ ] Import/export preserves IDs safely without creating collisions.
- [ ] Broken target paths do not destroy group identity.

### Tests

- migration from current AppGroup config;
- duplicate names;
- rename group;
- subgroup reference after rename;
- export/import;
- duplicate target paths;
- deleted/broken target;
- old shortcut compatibility.

### Acceptance gate

Two groups with the same display name must remain completely independent through editing, activation, export/import and taskbar shortcuts.

---

## PR 2 — Resident host and instant activation

**Complexity:** Medium–Large

**Goal:** Make stack activation feel native and remove avoidable startup latency.

The current application already uses a persistent popup/background pattern, but the activation path still relies heavily on window-title discovery, inter-process messages and deliberate delays.

### Direction

Prefer Windows App SDK `AppInstance` activation registration/redirection for the primary single-instance boundary.

### Requirements

- [ ] One resident application host owns configuration and popups.
- [ ] Secondary activations redirect to the resident host.
- [ ] Remove arbitrary delay from the warm popup hot path.
- [ ] Do not rebuild Jump Lists on every popup activation.
- [ ] Keep configuration in memory and update it when changed.
- [ ] Keep popup window/resources warm where practical.
- [ ] Avoid unnecessary creation of main/edit windows during group activation.
- [ ] Preserve explicit startup, editor and tray behavior.
- [ ] Ensure clean shutdown rather than process-family termination as a normal lifecycle mechanism.

### Performance acceptance

Warm activation should feel immediate. Measure rather than guess.

Suggested telemetry/debug timing points:

```text
activation received
→ group resolved
→ content bound
→ geometry resolved
→ first visible frame
```

### Manual test

Rapidly alternate:

```text
Stack A → Stack B → Stack A → Stack B
```

Expected:

- no spinner;
- no stale content;
- no popup from the previous group;
- no surviving duplicate process;
- second click behavior remains deterministic.

Upstream latency reference: https://github.com/iandiv/AppGroup/issues/6

---

## PR 3 — Popup geometry, DPI and multi-monitor foundation

**Complexity:** Large

**Goal:** Make popup placement a dedicated, testable subsystem before hover support depends on it.

### Introduce

`PopupGeometryService`

Inputs:

- cursor position;
- originating taskbar-button rectangle when known;
- monitor bounds;
- work area;
- monitor DPI;
- taskbar edge;
- taskbar autohide state;
- popup desired size.

Output:

```text
PopupPlacement
  X
  Y
  Width
  Height
  OpenDirection
  MonitorId
```

### Requirements

- [ ] Bottom taskbar.
- [ ] Top taskbar where environment supports it.
- [ ] Left/right taskbar where environment supports it.
- [ ] Secondary monitors.
- [ ] Mixed DPI.
- [ ] Negative desktop coordinates.
- [ ] Secondary monitor above primary.
- [ ] Taskbar on a non-primary display.
- [ ] Autohide taskbar.
- [ ] Monitor disconnect/reconnect.
- [ ] Prefer the actual triggering stack-button rectangle over cursor approximation.
- [ ] Cursor positioning remains a safe fallback.

Upstream multi-monitor reference: https://github.com/iandiv/AppGroup/issues/84

### Automated geometry cases

Examples:

```text
[1920×1080 @100%] [3840×2160 @150%]
```

```text
                [2560×1440 @125%]
[1920×1080 @100%]
```

### Acceptance gate

The popup must not unexpectedly:

- cross monitors;
- clip offscreen;
- appear behind the taskbar;
- jump to the primary monitor;
- use the wrong DPI scale.

---

## PR 4 — Popup interaction state machine

**Complexity:** Medium

**Goal:** Make click behavior perfect before adding hover.

### Explicit states

```text
Closed
Opening
Open
SwitchingGroup
Closing
```

### Required behavior

- [ ] Click a closed stack → open.
- [ ] Click the same stack again → close.
- [ ] Click another stack while open → switch content and reposition.
- [ ] Click outside → close.
- [ ] Press Escape → close.
- [ ] Launch/activate an item → close unless a deliberate setting says otherwise.
- [ ] Enter a subgroup → remain in popup interaction flow.
- [ ] Rapid consecutive clicks cannot leave conflicting animations/states.
- [ ] Popup should avoid stealing foreground focus unnecessarily.

Upstream interaction reference: https://github.com/iandiv/AppGroup/issues/72

### Foundation milestone acceptance

PR 0–4 form the first major milestone. At this point the application should already be a noticeably faster and more reliable AppGroup fork, before any hover/taskbar experiments are introduced.

---

# Bins interaction milestone

## PR 5 — Optional Bins-style hover-to-open

**Complexity:** Large / experimental

**Goal:** Recreate the defining Bins interaction while keeping click as a safe fallback.

### User setting

```text
Open stacks:
○ Click
○ Hover
○ Click or hover
```

Initial default remains **Click** until hover has broad Windows build coverage.

### Proposed architecture

Create a dedicated `TaskbarLocator` and `HoverController`.

Do not turn the existing low-level mouse hook into a high-frequency global `WM_MOUSEMOVE` workload.

Preferred approach:

1. Track pointer position cheaply at a modest cadence.
2. Do no expensive work while the pointer is outside taskbar bounds.
3. On taskbar entry, resolve the UI element beneath/near the cursor.
4. Determine whether it corresponds to one of this app's pinned stack shortcuts.
5. Enter a small hover/dwell state machine.

Read-only Windows UI Automation is an acceptable discovery mechanism if required.

### Suggested interaction timing

Defaults to tune through testing:

- stack dwell: 150–250 ms;
- leave grace: 200–350 ms;
- crossing between stack button and popup keeps the popup open;
- racing across several stack buttons must not flash every group.

### Acceptance gate

- Hover must never break click activation.
- Mouse-away closure must feel forgiving, not twitchy.
- High polling-rate mice must not cause CPU spikes or stutter.
- If taskbar discovery fails, the application quietly falls back to normal click behavior.

---

# Application-awareness milestone

## PR 6 — Normalize the launch model

**Complexity:** Medium

**Goal:** Create a predictable abstraction for what a group item represents and how it launches.

Explicit item types should cover:

- Win32 executable;
- `.lnk` shortcut;
- UWP/MSIX application;
- PWA;
- URL;
- Steam URL;
- folder;
- document/file;
- subgroup.

### Store where applicable

- target;
- arguments;
- working directory;
- elevation preference;
- display name;
- icon source;
- application identity/AUMID where available.

### Acceptance gate

Launching through AppGroup must preserve the behavior of the source shortcut as closely as practical, including working directory and arguments.

Related upstream references:

- https://github.com/iandiv/AppGroup/issues/79
- https://github.com/iandiv/AppGroup/issues/1

---

## PR 7 — Running application/window detection

**Complexity:** Large

**Goal:** Show which applications in a stack are already running.

Upstream users already request this directly:
https://github.com/iandiv/AppGroup/issues/67

### Introduce

`WindowTracker`

Track useful top-level window/application state such as:

- HWND;
- process ID;
- executable identity/path where accessible;
- AppUserModelID/AUMID where available;
- window title;
- visibility;
- minimized state;
- foreground state.

Prefer event-driven `SetWinEventHook(..., WINEVENT_OUTOFCONTEXT, ...)` style tracking over constant full-desktop enumeration. Use periodic reconciliation as a safety net.

### UI

A running item gets a subtle native-style indicator.

Example:

```text
Visual Studio      ●
VS Code            ●
Docker
GitHub Desktop     ●
```

### Acceptance gate

Running state updates promptly when applications start, close, minimize/restore and create/destroy top-level windows without intrusive injection.

---

## PR 8 — Launch-or-activate behavior

**Complexity:** Medium–Large

**Goal:** Make a stack an application switcher as well as a launcher.

### Per-item behavior

```text
When already running:
○ Activate existing window
○ Always launch new instance
○ Ask/select when multiple windows exist
```

Proposed default:

**Activate existing window**

### Required behavior

- not running → launch;
- one matching window → restore/activate;
- multiple matching windows → use PR 9 chooser or a clearly defined most-recent policy until PR 9 lands;
- user can explicitly request a new instance.

---

## PR 9 — Running-window flyout

**Complexity:** Large

**Goal:** Let one grouped application expose its open windows.

Example:

```text
Visual Studio
 ├─ BookWave
 ├─ Outlook Aligner
 └─ AppGroup
```

Initial implementation should show:

- application icon;
- window title;
- active/minimized indicator.

Live DWM thumbnails can be evaluated later rather than blocking the first working chooser.

---

# Product-quality milestone

## PR 10 — Modern compact visual modes

**Complexity:** Medium

**Goal:** Make the popup feel intentionally native rather than like a full application window.

Presets:

### Compact
Closest to classic Bins behavior.

### Fluent
Windows 11-style compact popup with Acrylic/Mica-compatible design choices where appropriate.

### Card
Retain a form of current AppGroup presentation.

### Design principles

- small visual footprint;
- taskbar-scale icon weight;
- subtle running indicators;
- Windows corner radius and spacing;
- theme-aware border/background;
- optional labels;
- accent integration without sacrificing readability.

Visual polish must not precede interaction reliability.

---

## PR 11 — Keyboard and accessibility

**Complexity:** Medium

### Keyboard

- [ ] Escape closes popup.
- [ ] Arrow keys navigate.
- [ ] Enter launches/activates.
- [ ] Shift+Enter can force a new instance if retained as a product behavior.
- [ ] Optional type-to-filter/search for larger stacks.

### Accessibility

- [ ] Correct accessible names/control types.
- [ ] Predictable focus behavior.
- [ ] Screen-reader semantics.
- [ ] High-contrast support.
- [ ] Respect Windows reduced-motion preferences.
- [ ] No essential information conveyed only through color.

---

## PR 12 — Release hardening

**Complexity:** Medium

### Packaging

- [ ] Signed installer path.
- [ ] Portable build remains viable if intentionally supported.
- [ ] x64.
- [ ] ARM64 where practical.
- [ ] Clean uninstall.
- [ ] Upgrade preserves user state.
- [ ] Startup registration is removed cleanly on uninstall.

### Release verification

- [ ] Clean Windows VM install.
- [ ] Upgrade from previous release.
- [ ] Uninstall/reinstall.
- [ ] Settings/group migration.
- [ ] Explorer restart.
- [ ] Sign-out/sign-in.
- [ ] SmartScreen/signing sanity.
- [ ] Defender/VirusTotal sanity checks where release process permits.

Related upstream uninstall reference:
https://github.com/iandiv/AppGroup/issues/90

---

# Post-1.0 research

These features are deliberately excluded from the core dependency path.

## Native taskbar running indicator

Research whether the pinned stack representation can expose a running/badge/overlay state through supported taskbar APIs without fake or intrusive window manipulation.

Do not block 1.0 on this.

## Native taskbar window grouping

Research AppUserModelID capabilities separately.

Do not modify unrelated applications' taskbar identity as part of the normal product unless a future design is demonstrably safe, supported and reversible.

## Drag directly onto a pinned taskbar stack

Windows owns taskbar drag/drop behavior, so treat this as experimental.

Drag/drop into an already-open stack popup/editor should be implemented first because that is fully under this application's control.

## Dynamic folder-backed stacks

A group may eventually point at a folder and update automatically as shortcuts/files appear or disappear.

Useful, but not required for the core Bins successor experience.

---

# MVP / 1.0 definition

A genuine modern Bins-style successor requires:

| Capability | 1.0 requirement |
| --- | --- |
| Pin stacks to taskbar | Yes |
| Fast click popup | Yes |
| Reliable popup placement | Yes |
| Multi-monitor / mixed DPI | Yes |
| Optional hover-to-open | Yes |
| Forgiving mouse-away close | Yes |
| Stable unique group identity | Yes |
| App/file/folder/URL targets | Yes |
| Running-app indication | Yes |
| Launch-or-activate | Yes |
| No Explorer injection | Yes |
| Keyboard accessibility | Yes |
| Compact Fluent visual mode | Yes |
| Existing AppGroup migration | Yes |
| Running-window chooser | Target for 1.0 |
| Native taskbar running indicator | Post-1.0 research |
| Native Windows taskbar regrouping | Research only |

---

# Dependency sequence

```text
PR 0  Fork / docs / CI
 │
 ▼
PR 1  Stable identity + migration
 │
 ▼
PR 2  Resident host + fast activation
 │
 ▼
PR 3  Geometry / DPI / monitors
 │
 ▼
PR 4  Popup interaction state machine
 │
 ▼
PR 5  Hover
```

Parallel application-awareness path after identity is stable:

```text
PR 1
 │
 ▼
PR 6  Launch model
 │
 ▼
PR 7  Running-window tracker
 │
 ▼
PR 8  Launch or activate
 │
 ▼
PR 9  Window chooser
```

Convergence:

```text
PR 5 ───────────┐
PR 9 ───────────┤
                ▼
PR 10 Visual polish
                │
                ▼
PR 11 Accessibility
                │
                ▼
PR 12 Release hardening
```

---

# Current next action

Finish **PR 0** by establishing CI/test scaffolding and recording the untouched fork baseline. Then begin **PR 1 — stable group and item identity**.

Do not begin hover or running-window work until the identity and activation foundations are complete.