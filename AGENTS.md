# Inventory Kamera Development Instructions

## Project Goal

This repository is a maintained and modernized fork of Inventory Kamera.

The application must remain an external, screen-based Genshin Impact inventory scanner.

The primary goals are:

- improve reliability across game updates
- reduce brittle UI assumptions
- improve maintainability and testability
- improve scan recovery, diagnostics, and quality-of-life features
- preserve compatibility with existing GOOD exports and expected scanner behavior unless explicitly changed

---

## Safety Boundary

The project must remain non-invasive toward Genshin Impact.

Never introduce or suggest:

- reading Genshin Impact process memory
- writing to Genshin Impact process memory
- DLL injection
- code injection
- DirectX hooking
- game API hooking
- anti-cheat bypassing
- modifying Genshin Impact executable files
- modifying Genshin Impact game data files
- packet interception or manipulation
- kernel-level interaction with the game process

Allowed interaction is limited to external mechanisms such as:

- screen capture
- OCR
- computer vision
- normal keyboard input
- normal mouse input
- virtual or physical controller input

If a requested implementation would violate this boundary, stop and report the concern instead of implementing it.

---

## Architecture Rules

Prefer dependency injection and explicit ownership of mutable state.

Do not introduce new mutable global or static state unless there is a strong technical reason.

New navigation logic should eventually go through dedicated abstractions rather than directly depending on a specific input backend.

Scanner logic should not directly depend on ViGEm, InputSimulator, or another specific input implementation when an abstraction can reasonably be used.

External game-data sources should eventually be isolated behind provider abstractions.

Keep OCR, image processing, navigation, game-data access, scan state, and export logic separated where practical.

Do not perform unrelated architectural rewrites while implementing narrowly scoped tasks.

---

## Behavior Preservation

Unless a task explicitly changes behavior:

- preserve existing user-facing behavior
- preserve existing export behavior
- preserve existing settings
- preserve existing GOOD output semantics
- preserve existing scan flow
- preserve supported resolutions and aspect ratios

Do not silently change OCR thresholds, timing values, navigation behavior, or filtering logic.

If behavior must change to fix a bug, document exactly what changed and why.

---

## Testing Rules

Before modifying behavior, understand the existing implementation and relevant tests.

After implementation:

- build the complete solution
- run the complete test suite
- add regression tests for bug fixes when practical
- add unit tests for new isolated logic when practical

Do not delete or weaken existing tests simply to make a change pass.

For OCR, image processing, navigation, or screenshot recognition changes, prefer deterministic regression tests where possible.

Automated tests do not replace live testing against the actual game for scan/navigation changes.

---

## WinForms Rules

Be careful when modifying WinForms Designer-generated code.

Avoid modifying:

- `MainForm.Designer.cs`
- generated settings files
- `.resx` files

unless the task actually requires it.

Prefer code-based wiring when practical if it avoids unnecessary Designer regeneration.

The existing project has previously experienced Designer regeneration problems involving settings bindings and namespace qualification.

---

## Git Rules

Do not commit unless explicitly instructed.

Do not push unless explicitly instructed.

Do not create or delete branches unless explicitly instructed.

Do not modify the protected baseline branch:

`next/baseline-v2-alpha`

Development work should happen on the currently checked-out development branch.

Keep changes small, focused, and reversible.

Do not include unrelated formatting changes or cleanup in feature/bug-fix work.

---

## Generated and Local Files

Do not commit runtime-generated game lookup data from:

`inventorylists/`

Do not commit build output, logs, temporary screenshots, user settings, or machine-specific files.

---

## Current Technical Context

The current modernization baseline is based on:

`v2.0.0-alpha`

Baseline commit:

`ba20280`

Current target framework:

`net8.0-windows7.0`

The repository currently builds with known baseline warnings including:

- legacy `InputSimulator 1.0.4` compatibility warning
- obsolete `WebClient` usage
- un-awaited calls in `GameScanner`
- unused field warning in `Character`

Do not treat these existing warnings as part of unrelated tasks unless explicitly requested.

---

## Implementation Workflow

For every implementation task:

1. inspect the relevant code before editing
2. explain the intended approach internally before modifying files
3. make the smallest reasonable change
4. add or update tests where appropriate
5. build the solution
6. run the full test suite
7. review the final diff
8. report:
   - files changed
   - behavior changed
   - tests added or modified
   - build result
   - test result
   - remaining risks or follow-up work

Do not commit or push after completing a task unless explicitly instructed.