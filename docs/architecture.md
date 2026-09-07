# Architecture Guardrails

## Scope

This document defines the architectural boundaries for evolving this AppGroup fork into a modern Windows 11 Bins-style taskbar grouping utility.

The project should feel deeply integrated with Windows while remaining as independent as possible from undocumented Explorer internals.

---

## Core architectural decisions

### 1. No Explorer injection or patching

The application must not:

- inject DLLs into `explorer.exe`;
- patch taskbar memory or private Explorer structures;
- replace the Windows taskbar;
- depend on undocumented offsets or implementation details that are expected to break on routine Windows updates.

Reason: the original Bins-style experience is valuable, but reproducing its old shell-internal integration would make the new utility fragile and expensive to maintain.

### 2. Supported Windows APIs first

Prefer, where practical:

- Windows App SDK / WinUI 3;
- documented Win32 APIs;
- AppInstance activation redirection;
- AppUserModelID APIs;
- DWM APIs;
- WinEvent hooks using out-of-context callbacks;
- documented shell/shortcut APIs;
- read-only UI Automation for taskbar discovery when no stronger supported API is available.

If a feature requires undocumented behavior, isolate it behind an optional capability boundary so normal click-based operation remains unaffected.

### 3. Stable identity is mandatory

Display names and filesystem paths are mutable and must not be used as primary identity.

Every group should have a stable immutable ID. Every group item should also have a stable ID once the data model is migrated.

This allows:

- duplicate group names;
- safe rename;
- reliable subgroups;
- correct taskbar shortcuts;
- future running-app state;
- import/export without accidental merges.

### 4. One resident host owns runtime state

A lightweight resident process should own:

- group configuration cache;
- popup lifecycle;
- taskbar geometry cache;
- running-window tracker;
- icon cache coordination;
- settings;
- activation routing.

Taskbar shortcut activations should redirect into this process rather than requiring a full new UI startup for each group.

### 5. Taskbar integration is isolated

Taskbar-specific behavior must not be scattered throughout popup UI code.

Target services:

```text
TaskbarLocator
PopupGeometryService
HoverController
ActivationRouter
```

If a future Windows build breaks taskbar discovery, these components should fail gracefully while normal group configuration and click activation continue to work.

### 6. Window tracking is read-only and event-driven

Running application/window detection should be implemented without injecting into target applications.

Prefer:

- `SetWinEventHook` with `WINEVENT_OUTOFCONTEXT`;
- top-level window enumeration for initial/reconciliation state;
- process/window metadata queried through documented APIs.

Avoid high-frequency polling of all desktop windows.

### 7. Keep the UI layer thin

`PopupWindow` and other WinUI views should not own most of the shell integration logic.

The UI should consume state from services/models and issue commands such as:

```text
OpenGroup(groupId)
ClosePopup()
LaunchItem(itemId)
ActivateWindow(windowId)
```

rather than directly performing low-level taskbar/process/geometry operations throughout code-behind.

---

# Target component model

## AppHost

Owns application lifetime and service initialization.

Responsibilities:

- initialize resident process;
- register single-instance identity;
- route redirected activations;
- coordinate orderly shutdown.

Must not contain group-specific UI logic.

## ActivationRouter

Transforms external activation into a typed internal command.

Examples:

```text
OpenGroup(groupId)
EditGroup(groupId)
LaunchAll(groupId)
ShowMainWindow
```

Responsibilities:

- validate arguments;
- resolve legacy name-based activation during migration;
- never treat group display name as canonical identity after migration.

## GroupRepository

Owns persistent group data.

Responsibilities:

- load current schema;
- migrate older schemas;
- write atomically;
- preserve user state;
- provide stable ID lookups;
- prevent accidental identity collisions during import.

The repository should expose typed models instead of leaking JSON dictionaries into UI code.

## PopupCoordinator

Owns popup interaction state.

State model:

```text
Closed
Opening
Open
SwitchingGroup
Closing
```

Responsibilities:

- coordinate show/hide/switch transitions;
- prevent conflicting animations;
- manage outside-click and Escape behavior;
- integrate click and hover triggers through one state machine.

## PopupGeometryService

Pure/mostly pure geometry service.

Inputs:

- triggering taskbar rect if available;
- cursor position;
- monitor bounds/work area;
- taskbar edge;
- DPI;
- popup desired size;
- autohide state.

Output:

```text
PopupPlacement
```

The placement calculation should be unit-testable without creating WinUI windows.

## TaskbarLocator

Read-only adapter responsible for identifying relevant taskbar surfaces and, when possible, the rectangle associated with a pinned stack button.

Possible implementation tools:

- taskbar HWND discovery;
- shell/taskbar geometry APIs;
- UI Automation for element-at-point or taskbar child discovery.

It must not mutate Explorer.

## HoverController

Optional Bins-style hover behavior.

Responsibilities:

- detect stack dwell;
- apply open delay;
- apply leave grace period;
- understand the bridge from taskbar button to popup;
- avoid rapid flicker while crossing adjacent buttons;
- fall back silently if taskbar discovery is unavailable.

Hover must remain optional and must not be required for core product operation.

## LaunchService

Owns launching items.

Supported item categories should be explicit rather than inferred ad hoc in UI code.

Responsibilities may include:

- Win32 executable launch;
- `.lnk` behavior;
- UWP/MSIX/AUMID activation;
- PWA/URL launch;
- Steam URL launch;
- folder/document launch;
- arguments;
- working directory;
- run-as-administrator behavior.

## WindowTracker

Maintains current top-level application/window state.

Responsibilities:

- initial enumeration;
- event-driven updates;
- reconciliation;
- application matching;
- expose observable running state to the popup.

It does not activate windows itself.

## WindowActivator

Responsible for restoring/focusing an already-running window.

Keep foreground-window workarounds isolated here rather than scattered through launch/popup code.

## IconService

Build on AppGroup's existing icon extraction/cache work.

Responsibilities:

- icon identity/cache keying;
- asynchronous loading;
- fallback icons;
- generated group/grid icons;
- cache invalidation.

## SettingsService

Owns global and per-group settings, including future options such as:

```text
OpenMode = Click | Hover | ClickOrHover
HoverDelay
LeaveGrace
AlreadyRunningBehavior
PopupAppearance
Animations
```

---

# Data architecture

## Group identity

Target conceptual schema:

```text
Group
  Id: stable GUID/string
  Name: display name
  Icon
  Appearance
  Items[]
```

## Group item identity

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
  AppIdentity/AUMID (optional)
```

Do not make the path the item ID; two items can legitimately target the same executable with different arguments, working directories or semantics.

## Schema migration

Every breaking persistent-data change requires:

1. explicit schema version;
2. deterministic migration;
3. backup/rollback consideration;
4. tests using real older fixtures;
5. no silent destruction of unknown fields without a deliberate decision.

---

# Performance architecture

The popup is a high-frequency utility surface. Users will notice delays that are acceptable in ordinary applications.

## Hot-path rules

Warm activation should not perform avoidable:

- process startup;
- full config disk reads;
- full icon extraction;
- Jump List regeneration;
- arbitrary sleeps/delays;
- global desktop enumeration;
- expensive UI Automation traversal.

Preload/cache only what creates measurable value; do not turn the resident process into a heavy background service.

Instrument the activation path in debug builds so regressions are visible.

---

# Threading and hooks

## UI Automation

If UI Automation is used for taskbar discovery, perform potentially blocking client calls away from the WinUI UI thread.

## WinEvent hooks

Use out-of-context event hooks where practical to avoid code injection into observed applications.

Callbacks should do minimal work and enqueue structured events for processing.

## Mouse handling

Avoid processing global high-frequency mouse-move messages directly through a low-level hook.

High polling-rate mice can generate thousands of events per second. Hover detection should use bounded/coalesced sampling and only perform taskbar discovery when the pointer is relevant.

---

# Failure behavior

A Windows shell utility must degrade gracefully.

Examples:

- taskbar button rect unavailable → position using cursor/taskbar geometry;
- hover discovery unavailable → click still works;
- app identity cannot be resolved → item remains launchable;
- running-window match uncertain → do not incorrectly activate an unrelated window;
- icon extraction fails → fallback icon, not group corruption;
- broken shortcut → display a recoverable broken item, not disappearance of the whole group.

---

# Upstream compatibility

General fixes that benefit AppGroup itself should be designed so they can potentially be proposed upstream, especially:

- stable identity fixes;
- DPI/multi-monitor positioning;
- working-directory correctness;
- activation latency reduction;
- configuration migration reliability;
- testability refactors.

Product-specific Bins behavior should stay isolated enough that upstream merges remain manageable.

See [upstream.md](upstream.md).

---

# Architecture review triggers

Revisit this document before implementing any change that would:

- inject into another process;
- require an undocumented Explorer structure;
- change persistent schema;
- alter taskbar identity of third-party application windows;
- create a service/elevated background component;
- add always-on high-frequency polling;
- make hover a prerequisite for normal click operation;
- substantially diverge from upstream AppGroup architecture.

Such changes need an explicit architectural decision before implementation.