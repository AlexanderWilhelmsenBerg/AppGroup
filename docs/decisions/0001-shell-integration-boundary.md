# ADR-0001: Windows shell integration boundary

**Status:** Accepted

## Context

The product goal is to recreate the useful interaction model of 1UP Industries Bins on modern Windows 11: taskbar-pinned groups, immediate compact popups, optional hover-to-open, and eventually running-application awareness.

The original style of deep taskbar integration is tempting, but Windows 11 Explorer/taskbar internals are not a stable public extension contract. Depending on undocumented memory structures, private taskbar internals or Explorer injection would make routine Windows updates a product reliability risk.

## Decision

The application will not inject into, patch or replace `explorer.exe` or the Windows taskbar.

The project will prefer documented Windows / Windows App SDK APIs. Where no stronger public taskbar discovery API exists, read-only UI Automation may be used behind an isolated optional capability boundary.

Normal click activation must remain functional even if hover/taskbar discovery is unavailable on a particular Windows build.

Running-window observation must use non-intrusive mechanisms such as out-of-context WinEvent hooks and documented window/process metadata queries.

## Consequences

### Positive

- Better resilience across Windows updates.
- Failures can degrade to normal click behavior instead of breaking the application.
- Windows integration code can be tested and replaced independently.
- The product can remain a normal user-mode desktop utility.

### Negative

- Some behaviors may not be pixel-for-pixel identical to native taskbar functionality.
- Hovering a specific pinned taskbar item may require UI Automation or another discovery layer.
- Truly regrouping third-party application taskbar buttons is intentionally outside the initial architecture.

## Follow-up

Any future proposal requiring Explorer injection, undocumented taskbar structures, altering third-party taskbar identity, or another privileged/invasive shell mechanism requires a new ADR before implementation.