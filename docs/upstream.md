# Upstream Strategy

## Upstream repository

Primary upstream:

https://github.com/iandiv/AppGroup

This fork intentionally preserves the upstream Git history and MIT license.

The goal is to evolve a Bins-style Windows 11 product while retaining the ability to absorb upstream AppGroup improvements without turning every sync into a manual rewrite.

---

# Repository roles

Conceptually maintain:

```text
upstream/master  -> iandiv/AppGroup
origin/master    -> AlexanderWilhelmsenBerg/AppGroup stable branch
feature/*        -> one implementation slice per PR
```

Do not develop directly against a copied source snapshot. Keep the fork relationship and upstream history intact.

---

# General rule

Prefer small, dependency-aware PRs over broad rewrites.

A change that modifies both generic AppGroup behavior and Bins-specific behavior should be split where practical so the generic portion remains easy to compare with or propose upstream.

---

# Good candidates to upstream

Improvements that are broadly useful to AppGroup users should be kept modular and may be proposed back to upstream, for example:

- duplicate-name/stable identity fixes;
- configuration migration reliability;
- activation latency reductions;
- correct `.lnk` working directory/arguments behavior;
- DPI and multi-monitor popup placement fixes;
- icon cache correctness;
- testability refactors;
- lifecycle/clean shutdown fixes;
- accessibility fixes.

Whether a patch is actually submitted upstream is a separate decision; design it so submission remains possible.

---

# Fork-specific features

Keep these behind clear service/component boundaries because they may not match upstream product direction:

- Bins-style hover controller;
- taskbar UI Automation discovery;
- running-window tracker;
- launch-or-activate policy;
- window chooser;
- compact Bins-inspired visual presets;
- experimental taskbar integration.

These should not force broad modifications throughout unrelated AppGroup code.

---

# Sync procedure

Before starting a major roadmap PR:

1. Check upstream for meaningful new commits/releases.
2. Review upstream open/merged PRs that touch the same subsystem.
3. If upstream contains relevant fixes, sync them before building competing logic when reasonable.
4. Re-run baseline build/tests after the sync.
5. Only then begin the feature PR.

When syncing upstream into this fork:

- prefer a normal merge/rebase workflow that preserves understandable history;
- do not silently overwrite fork-specific behavior;
- resolve conflicts with architecture/roadmap documents in mind;
- run the relevant Windows regression suite after any shell/popup/activation conflict.

---

# Conflict priority

When upstream changes conflict with this fork:

1. Preserve user data and migration correctness.
2. Preserve the no-Explorer-injection architectural boundary.
3. Preserve reliable click activation.
4. Preserve Windows/DPI correctness.
5. Re-evaluate fork-specific hover/running-window behavior against the new upstream implementation.
6. Prefer adopting a better upstream mechanism over retaining custom code merely because it already exists here.

---

# Documentation during sync

If an upstream sync materially changes the roadmap assumptions:

- update `docs/roadmap.md`;
- update `docs/architecture.md` if a boundary changes;
- update `docs/testing.md` when new regression cases are discovered.

Do not leave the roadmap describing code that no longer exists.

---

# Branding and attribution

Until a separate branding decision is made, keep AppGroup naming in the source and documentation where changing it would create unnecessary divergence.

Any future rebrand must:

- preserve the MIT license;
- retain legally required copyright/license notices;
- clearly acknowledge the AppGroup upstream foundation;
- avoid presenting the fork as the original 1UP Industries Bins product.

'Bins' is used in planning documents only to describe the desired interaction style/product inspiration, not as a claim of ownership or continuation of the original product.