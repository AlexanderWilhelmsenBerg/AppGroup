# Project Documentation

This folder is the source of truth for the direction of this AppGroup fork.

## Start here

- [Roadmap](roadmap.md) — dependency-aware implementation sequence from foundation through a modern Bins-style Windows 11 experience.
- [Architecture](architecture.md) — component boundaries and non-negotiable technical guardrails.
- [Testing](testing.md) — Windows/taskbar/DPI/multi-monitor/lifecycle regression matrix.
- [Upstream strategy](upstream.md) — how this fork should continue absorbing and contributing generic AppGroup improvements.
- [ADR-0001: shell integration boundary](decisions/0001-shell-integration-boundary.md) — accepted decision prohibiting Explorer injection/taskbar patching.

## Current phase

**PR 0 — fork baseline, documentation, CI and test scaffolding.**

After the untouched fork baseline is build-verified and basic CI/test scaffolding exists, implementation moves to **PR 1 — stable group and item identity**.

Do not skip ahead to hover or running-window integration before the identity, activation, geometry and popup-state foundations are complete.