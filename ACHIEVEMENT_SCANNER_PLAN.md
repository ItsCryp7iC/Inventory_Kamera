# Inventory Kamera — Achievement Scanner Plan

> Status: **Approved implementation plan**
>
> Feature track: **Phase 6 — Achievement Scanner**
>
> Development branch: `next/phase-6-achievement-scanner`
>
> This document is the authoritative implementation plan for the Achievement Scanner. Agents working on this feature must follow this plan unless the user explicitly changes the scope. Do not silently redesign the feature, skip safety gates, combine phases, or broaden the work.

---

## 1. Purpose

Add a reliable, external, screen-based Genshin Impact Achievement Scanner to Inventory Kamera.

The scanner must:

- operate only through screenshots, OCR/computer vision, and normal input;
- reuse Inventory Kamera's hardened capture, diagnostics, cancellation, and export infrastructure while keeping Achievement input independent of the legacy controller lifecycle;
- identify completed achievements using positive evidence only;
- map results to canonical achievement IDs compatible with Traveler's Toolkit;
- fail closed when search results, achievement identity, or completion state are ambiguous;
- preserve resumability and future maintainability.

The scanner must **not** read or modify Genshin process memory, inject code, hook rendering/input, modify game files, intercept packets, bypass anti-cheat, or use kernel-level interaction.

---

## 2. Reference implementation studied

The historical reference is:

- `xenesty/AkashaScanner`

AkashaScanner's useful design ideas are:

1. load a canonical achievement catalog;
2. search for each known achievement by name instead of scrolling every achievement row;
3. detect completion from fixed-position UI evidence;
4. support previous scan data so already-completed achievements can be skipped;
5. export canonical achievement IDs.

Inventory Kamera must **not** copy AkashaScanner blindly.

Important differences in the current Genshin UI and this project:

- current search results expose explicit positive completion signals such as `Claim`, `Completed`, completion dates, and progress counters;
- Inventory Kamera already has a capture guard, failure diagnostics, and scan session lifecycle; its controller abstraction and recovery workflow remain specific to legacy inventory scans;
- Traveler's Toolkit already has a canonical achievement catalog and import/export logic;
- ambiguous results must be handled with stronger verification than the historical scanner used.

---

## 3. Confirmed current Genshin UI observations

The implementation may rely on the following currently observed behaviors, but each behavior must still be represented through explicit screen detection rather than assumed blindly:

### Achievement screen

The screen contains:

- an achievement search field in the upper-left area;
- achievement category list on the left;
- achievement results/content panel on the right;
- total achievements-completed count near the top-right;
- a back/return control.

### Search mode

After entering search text:

- a `Match(es): N` count appears in the result panel;
- result pages show a page counter such as `1/1`;
- matching title substrings may be highlighted;
- exact-title searches commonly return one result;
- partial searches may return multiple results.

### Completion states

Observed positive states include:

- red `Claim` button + visible completion date: achievement is completed but reward is unclaimed;
- pale `Completed` stamp + visible completion date: achievement is completed and reward is already claimed;
- progress fraction such as `0/1` or `3/20`: achievement is not yet completed.

### Input mode boundary (Phase 6K.3)

The Achievement Scanner is a standalone **Keyboard/Mouse-mode** feature. The user places Genshin
Impact in Keyboard/Mouse mode and opens the Achievements screen when prompted by the companion
overlay. The readiness loop is passive and does not send input.

Achievement-only scans must not create a ViGEm backend, enter a `GameInputSession`, borrow
`GameNavigator`, switch the game to Controller mode, or perform controller restoration. Search
text and semantic mouse clicks remain isolated behind achievement-specific keyboard/mouse
abstractions rather than being mixed directly into OCR or classification logic.

The legacy weapon, artifact, character, development-item, and material scanners remain
controller-assisted and keep their existing input lifecycle. The two scan families are mutually
exclusive in the MainForm selection UI.

---

## 4. Core design decision

The primary algorithm is **search-per-achievement**, not scroll-through-every-row.

For each canonical achievement or achievement stage group:

1. focus the achievement search field;
2. clear previous search text;
3. type the canonical search text;
4. trigger search;
5. wait for stable result UI;
6. detect and validate the match count;
7. verify the visible achievement title;
8. classify completion state from positive evidence;
9. map the verified result to canonical achievement ID(s);
10. save observation;
11. clear/exit search;
12. continue to the next catalog entry.

Scrolling through the full achievement list is not part of the primary design.

Do not introduce list-scrolling as the default algorithm unless a later phase proves search-based scanning cannot cover a required case.

---

## 5. Safety and correctness rules

These rules are mandatory across every Phase 6 task.

### 5.1 Positive evidence only

Never interpret missing evidence as completion.

Examples:

- no `Claim` detected + no `Completed` detected != completed;
- no progress fraction detected != completed;
- title OCR failure != expected achievement;
- `Match(es): 1` alone != identity proof.

No positive state evidence means `Unresolved`.

### 5.2 Identity must be proven

A result may only be assigned to a canonical achievement when:

- the search result screen is positively detected;
- the match count is valid;
- the visible title is verified against the expected canonical title with a safe unique match;
- any collision-specific rules for that entry are satisfied.

Do not choose the first fuzzy match merely because it is closest.

### 5.3 Ambiguity fails closed

If two or more plausible identities or completion interpretations remain:

- mark the observation unresolved;
- retain bounded diagnostics;
- do not fabricate an ID or completion state;
- continue or stop according to the phase-specific policy.

### 5.4 Existing scan safety rules remain

Achievement scanning must preserve:

- cancellation semantics;
- capture-overlay hiding;
- controller recovery lifecycle for legacy inventory scans;
- result staging/publication rules;
- bounded retry behavior;
- no forced export after cancellation unless existing policy explicitly permits manual export;
- no unbounded loops.

---

## 6. Reuse existing Inventory Kamera infrastructure

Achievement scanning should reuse existing abstractions where practical.

Preferred shared components include:

- `CaptureOverlayGuard`
- `ScanCompanionOverlay`
- `FailureDiagnosticStore`
- `IOcrService`
- `IImagePreprocessor`
- `TextNormalizer`
- `ScanSession`
- `ScanRun`
- existing cancellation token / cancellation checks
- current result publication patterns

Achievement-only scanning uses `AchievementKeyboardMouseNavigator`, `GameTextInput`, and a small
normal-Windows-pointer abstraction. It must not create a parallel controller lifecycle, second
recovery system, or achievement-specific global mutable input state.

If achievement scanning needs text entry, introduce or reuse a small text-input abstraction rather than directly depending on InputSimulator from the scraper.

---

## 7. Canonical achievement data

Traveler's Toolkit is the canonical downstream consumer.

Its repository already contains a full achievement catalog and canonical IDs.

Inventory Kamera should consume a scanner-friendly generated snapshot rather than maintain an unrelated hand-written achievement database.

### Minimum scanner entry shape

Each canonical scanner entry should provide at least:

- canonical achievement ID;
- canonical English title;
- category/group metadata where useful;
- stage grouping metadata where applicable;
- search aliases only when justified by real collision evidence.

Conceptual example:

```json
{
  "id": 12345,
  "name": "You There! Fly!",
  "category": "Wonders of the World",
  "stageGroup": null
}
```

For a staged family:

```json
{
  "name": "Example Staged Achievement",
  "ids": [1001, 1002, 1003],
  "category": "Example Category"
}
```

### Data-source rules

- Do not fetch live remote catalog data during each scan.
- Prefer a generated/versioned local snapshot for deterministic behavior.
- The generation/update process may source from Traveler's Toolkit's canonical data.
- Catalog version/update mechanics belong in a dedicated phase.
- Unknown IDs or unsupported catalog entries must never be invented.

---

## 8. Search navigation contract

Achievement search navigation must be state-aware.

The scanner should be able to positively classify at least:

- achievement screen idle;
- search field focused/editable;
- search results active;
- no-results state;
- multi-result state;
- single-result state.

The navigation sequence must use bounded attempts and fresh captures. On the healthy Phase 6K.3
path, one fresh post-submit capture is shared across screen, identity, completion, and conditional
reward classification.

### Text entry

Achievement scanning requires Genshin to be in Keyboard/Mouse mode.

Requirements:

- the known idle Keyboard/Mouse layout must be established before the scanner starts;
- focus is obtained by a normalized semantic click at the fixed Search/Clear toggle point; exact
  typing is used when Idle/Clear proof establishes an empty field, while Ctrl+A/Delete replacement
  is reserved for bounded stale-text recovery;
- the scanner must not type blindly into an unknown screen;
- after typing, search submission uses Enter and the result screen is the authoritative proof;
- after classification, search is cleared through the visible Clear control and Idle is verified.

If the result screen or identity cannot be proven, use only bounded fallback evidence and fail the
achievement observation closed if uncertainty remains. Do not add an expensive focus-OCR loop to
every healthy target.

---

## 9. Search-result verification

The search result must not be trusted based only on the query string.

### Required signals

For a normal exact search, the preferred success path is:

- valid achievement search-results screen;
- `Match(es): 1`;
- visible result title matches the expected canonical title;
- title match is unique and above a validated threshold.

### Title normalization

Normalization may:

- ignore case;
- normalize whitespace;
- normalize punctuation where safe;
- tolerate OCR spacing artifacts;
- support multiple preprocessing/OCR variants.

Normalization must not:

- collapse distinct canonical titles into the same identity without collision handling;
- silently remove meaningful words;
- use category assumptions as sole identity proof.

### Multi-match handling

If `Match(es) > 1`:

- do not automatically select the first result;
- classify the entry as requiring collision handling;
- use the dedicated ambiguity strategy from Phase 6G;
- unresolved cases must remain unresolved rather than guessed.

---

## 10. Completion-state classifier

The classifier should use current fixed-position UI evidence rather than historical star counting as the primary source.

### Completed — unclaimed

Positive evidence:

- `Claim` action detected in the expected state region;
- and/or a valid completion date detected in the expected date region;
- with verified achievement identity.

Expected output:

- `CompletedUnclaimed`

### Completed — claimed

Positive evidence:

- `Completed` stamp detected;
- and/or valid completion date detected;
- with verified achievement identity.

Expected output:

- `CompletedClaimed`

### Incomplete

Positive evidence:

- valid progress fraction such as `0/1`, `3/20`, etc.;
- with verified achievement identity.

Expected output:

- `Incomplete`

### Unresolved

Examples:

- conflicting `Completed` and progress evidence;
- completion-state region cannot be parsed;
- title identity is unresolved;
- result layout is unexpected;
- match count is unresolved.

### Important rule

Do not classify by absence.

For example:

- `Claim` not found does not imply claimed;
- `Completed` not found does not imply incomplete;
- date not found does not imply incomplete.

---

## 11. Completion dates

Completion dates are useful but are **not required for the first scanner release**.

Current Traveler's Toolkit native progress format expects an ISO timestamp, while Genshin exposes date-level precision.

Therefore:

- Phase 6 initial export may set only completion IDs;
- do not fabricate a UTC completion time from a date-only value;
- date-only support may be added later through an explicit data-model change;
- date OCR can still be captured as diagnostic metadata if useful.

---

## 12. Staged/multi-stage achievements

Do not assume historical AkashaScanner star counting remains valid for current staged achievement UI.

Before implementing staged achievement inference:

1. collect current live examples of multi-stage achievements;
2. determine how Genshin search displays completed and incomplete stages;
3. verify whether separate stage IDs can be inferred from explicit UI state;
4. define deterministic stage semantics;
5. add regression fixtures.

Until then:

- single-stage achievements may be implemented first;
- staged families may remain unsupported/unresolved rather than guessed;
- no stage completion should be inferred solely from historical behavior.

### Staged achievement / unclaimed reward policy

Historical star counting must never be used to infer achievement stages. Before a scan begins, the
scanner should advise the user to claim all pending achievement rewards for best accuracy, while
still allowing the user to continue without doing so.

If a later full-scan flow encounters `Claim`, it must pause that achievement and explain that an
unclaimed reward prevents safe staged-progress resolution. The user may then Resume or Skip
Achievement. The scanner must never click `Claim` automatically. Resume re-checks the same
achievement and pauses again while `Claim` remains visible, including when multiple already-earned
staged rewards are waiting to be claimed. Skip Achievement performs no completion or stage
inference for that achievement, records the skip, and continues with the next achievement.

At the end of the scan, the scanner reports each skipped achievement's name, canonical ID or stage
group when known, and reason for the skip. If stage context remains ambiguous after pending rewards
are cleared, the scanner fails closed rather than guessing. Phase 6K.2 implements this workflow as
a UI-independent interactive coordinator plus WinForms dialogs. Each Resume is explicitly
user-driven and re-scans the same target; Skip records no completion evidence.

When `Claim` is absent, the staged position may be resolved only from positive reward-number
evidence that uniquely identifies one member of the current canonical stage family. Reward mapping
is catalog-driven and is never a global 5/10/20 rule. An incomplete current stage means only its
earlier stages are complete; a completed-and-claimed current stage includes that stage as well as
its predecessors. Duplicate reward patterns, unreadable or conflicting reward OCR, and rewards not
present in the family remain unresolved and add no stage IDs. No stage inference uses stars,
descriptions, categories, the requested representative ID, or progress-fraction targets. Users
should claim pending achievement rewards before scanning for the most accurate staged results.

---

## 13. Resume / update scan

Achievement completion is monotonic: a completed achievement does not become incomplete.

A later phase should support loading a previous achievement scan.

### Update-scan behavior

If an achievement is already recorded as completed in a trusted previous scan:

- skip rescanning it by default;
- preserve the previous completion result;
- scan only unresolved/not-completed entries.

This is a major performance feature and should be built after baseline full-scan correctness.

Do not let previous scan data override contradictory current positive evidence without an explicit reconciliation rule.

---

## 14. Export contract

### Primary first-release export

Use a simple GOOD-like achievement payload compatible with Traveler's Toolkit:

```json
{
  "achievements": [80001, 80002, 80003]
}
```

Requirements:

- IDs must be canonical;
- deduplicate IDs;
- sort numerically for deterministic output;
- include only positively completed achievements;
- unresolved/incomplete entries must not be exported as completed.

### Diagnostic/native scanner record

A richer internal record may contain:

- canonical ID;
- requested title;
- observed title;
- match count;
- identity confidence;
- completion state;
- completion signals;
- raw date text if captured;
- retry count;
- unresolved reason.

This richer record is for diagnostics/resume and must not weaken the primary export semantics.

---

## 15. Diagnostics

Achievement diagnostics must follow the existing bounded retention model.

For each failed/unresolved observation, retain only the minimum useful crops.

Preferred retained crops:

- search-state / match-count crop;
- visible title crop;
- completion-state crop;
- optional narrow contextual result-row crop.

Do not retain full-screen captures by default because the game UI may contain UID/account-identifying information.

### Required log fields

At minimum:

```text
achievementId=
expectedTitle=
searchText=
attempt=
screenState=
matchCountRaw=
matchCount=
titleRawReads=[]
titleNormalized=
titleCandidates=[]
selectedTitle=
titleConfidence=
claimEvidence=
completedEvidence=
progressEvidence=
dateRaw=
finalCompletionState=
reason=
```

---

## 16. Testing philosophy

Every isolated detector/parser must be testable without launching Genshin.

Use sanitized narrow fixtures wherever image evidence is needed.

Do not commit full account screenshots.

### Automated test categories

- search-screen geometry;
- match-count parser;
- title normalization;
- canonical matcher;
- exact one-match verification;
- multi-match rejection;
- no-results handling;
- completion-state resolver;
- Claim detector;
- Completed detector;
- progress fraction parser;
- conflict resolution;
- retry semantics;
- cancellation;
- diagnostics retention;
- export deduplication/sorting;
- prior-scan merge/resume behavior;
- controller/text-input lifecycle where testable.

Automated tests do not replace live game validation.

---

## 17. Phase breakdown

### Phase 6A — Achievement screen and search-state detector

Goal:

Build deterministic screen recognition and geometry helpers only.

Must answer from a screenshot:

- is this the Achievement screen?
- is search idle or active?
- what is the raw `Match(es)` count?
- is there one result, multiple results, or no valid result?
- what are the normalized result-title and completion-state crop rectangles?

No full scanning yet.

Deliverables:

- shared normalized capture regions;
- achievement screen detector;
- match-count parser;
- sanitized fixtures from current UI;
- diagnostics;
- tests across supported 16:9 and 16:10 scaling.

Exit criteria:

- deterministic classification on all supplied fixtures;
- no false positive on known non-achievement screens;
- full test suite/build green;
- live manual validation of detector only.

### Phase 6B — Canonical achievement catalog

Goal:

Add deterministic local achievement catalog data compatible with Traveler's Toolkit IDs.

Deliverables:

- scanner data model;
- local generated catalog snapshot;
- validation of duplicate IDs/names/stage groups;
- catalog loader/provider abstraction;
- tests.

Exit criteria:

- every exported ID is canonical;
- generation/update path documented;
- no network dependency during scan.

### Phase 6C — Search-box navigation and keyboard text entry

Goal:

Historical goal (superseded by Phase 6K.3): safely automate one search in controller mode.

Deliverables:

- focus verification;
- clear-search logic;
- text-input abstraction;
- search submission;
- bounded settle/retry behavior;
- safe exit/clear;
- cancellation support.

No completion inference yet.

Exit criteria:

- live test can repeatedly search a known title and return to idle state;
- no typing occurs unless search focus is proven;
- recovery remains functional.

### Phase 6D — Search-result identity verification

Goal:

Prove that a visible result is the requested achievement.

Deliverables:

- title OCR variants;
- normalization;
- canonical unique-match scoring;
- confidence/margin rules;
- one-match requirement for normal path;
- fail-closed ambiguity handling.

Exit criteria:

- representative punctuation-heavy, quoted, long, and highlighted titles resolve;
- intentional collisions remain unresolved;
- no first-candidate guessing.

### Phase 6E — Completion-state classifier

Goal:

Classify verified result as:

- CompletedUnclaimed;
- CompletedClaimed;
- Incomplete;
- Unresolved.

Deliverables:

- fixed completion-state regions;
- positive `Claim` evidence;
- positive `Completed` evidence;
- progress parser;
- optional date parser as supporting evidence;
- conflict-safe resolver;
- diagnostics.

Exit criteria:

- current live examples resolve correctly;
- no absence-based completion inference;
- conflicts fail closed.

### Phase 6F — Single-achievement end-to-end scanner

Goal:

Run the complete flow for one known achievement.

Flow:

- verify start state;
- search title;
- verify identity;
- classify completion;
- clear search;
- return result.

For a staged title-family with visible `Claim`, Phase 6F reports the formal orchestration state
`PendingClaimRequiresUserAction`. This preserves the verified family and Claim evidence without
selecting an individual stage; pause/Resume/Skip UI remains deferred to the later full-scan flow.

Exit criteria:

- multiple live completed/incomplete examples pass;
- cancellation safe;
- recovery safe;
- no state leak into next scan.

### Phase 6G — Collision and multi-match handling

Goal:

Safely handle titles that produce multiple or ambiguous search results.

Possible strategies to benchmark:

- full exact title;
- distinctive prefix/suffix;
- stronger visible-title OCR;
- category text as secondary corroboration;
- manual correction/confirmation.

Rules:

- no hardcoded silent guess;
- user override only through explicit confirmation;
- ambiguous automated result remains unresolved.

The v1 policy uses the full canonical title as the only search query. Duplicate titles belonging to
one validated stage group remain valid staged families; unrelated exact-title collisions remain
explicitly unresolved. Any positively observed `Match(es) > 1` result also remains unresolved. The
scanner does not select the first row or use catalog order as a tie-break, and it does not use
description or category OCR for collision resolution. Full-scan/reporting phases will surface these
structured unresolved collision and multi-match results later.

### Phase 6H — Full catalog scan

Goal:

Scan all supported achievement entries.

Requirements:

- deterministic order;
- progress reporting;
- bounded retry policy;
- cancellation;
- per-entry diagnostics;
- no duplicate title-group scans;
- no unbounded runtime behavior.

Performance optimization comes only after correctness.

Phase 6H scans one deterministic target per exact-title group in first catalog appearance order,
not one target per canonical ID. A staged family uses its first-stage/root ID as the representative;
an unrelated collision uses its first source-order ID only to drive the Phase 6F scanner, while all
Phase 6G ambiguity protections remain authoritative. Orchestration is serial and adds no outer
single-scan retry beyond the bounded component retries already implemented. Pending-Claim,
unresolved, and ambiguous results continue only after a positively verified safe reset; Failed
results stop, and `ReadyForNextAchievement=false` always stops. Progress is exposed through a
non-UI abstraction. Phase 6H does not produce canonical completion IDs or export data; export-side
deduplication remains Phase 6J, and resume/update filtering remains Phase 6I.

### Phase 6I — Resume/update scan

Goal:

Use previous trusted achievement result to skip already-completed achievements.

Requirements:

- validate an in-memory trusted completed-ID seed;
- canonicalize IDs;
- do not trust unknown IDs;
- preserve completed entries;
- scan only remaining candidates;
- provide clear counts for skipped/scanned/unresolved.

Phase 6I accepts only an in-memory collection of trusted completed canonical IDs; external JSON or
Traveler's Toolkit file parsing is deferred. Unknown, nonpositive, or otherwise noncanonical IDs
fail validation before scanning, while duplicate valid IDs canonicalize deterministically. Target
skipping operates on the Phase 6H title groups: a target is skipped only when every candidate ID is
already trusted, so partially trusted staged families and collisions are still scanned once.
Exposed trusted-ID collections use ascending canonical numeric order.
Previous trusted IDs are always preserved. A scan may add a positively verified `Completed` result
for a unique-title target, and may add only the completed stage IDs supplied by a `Resolved`
`StageProgressResult` for a staged family. Pending-Claim, reward-ambiguous, unresolved, collision,
and multi-match results add no IDs. Phase 6I adds no outer retries and retains all Phase 6H
ordering, cancellation, failure, progress-callback, and `ReadyForNextAchievement` safety behavior.

### Phase 6J — Traveler's Toolkit export integration

Goal:

Produce and consume import-ready achievement progress without UI or file-path coupling.

Primary output:

```json
{
  "achievements": [...]
}
```

Phase 6J also supports Traveler's Toolkit native
`TRAVELERS_TOOLKIT_ACHIEVEMENTS` version 1 import/export. Native exports always use
`completedAt: null` because Inventory Kamera has no trusted completion timestamps. Both formats
deduplicate and numerically sort canonical IDs. Import accepts GOOD-like achievement arrays and
native version 1 progress objects; unknown canonical IDs remain visible in the structured result
and prevent trusted resume-seed creation rather than being silently discarded.

The import/export core operates on JSON strings and immutable models without file dialogs or UI.
Only validated trusted canonical completion IDs may be exported. Fresh full-scan extraction trusts
a positively `Completed` unique-title result with exactly one candidate ID, plus only the completed
stage IDs supplied by a `Resolved` `StageProgressResult`, using the same rules as Phase 6I.
Pending-Claim or reward-ambiguous stage families, collisions, multi-match results, and unresolved
states add no IDs. Direct app-to-app integration and user workflow remain deferred.

### Phase 6K — Hardening

Goal:

Prepare the feature for release.

Includes:

- resolution/DPI regressions;
- performance tuning;
- retry tuning based on evidence;
- diagnostic retention limits;
- release notes;
- README instructions;
- known limitations;
- full live scan;
- update scan;
- cancellation/recovery tests.

#### Phase 6K.1 — staged achievement progress resolution

The packaged schema retains each canonical Primogem reward. A staged family is resolved only when
the visible reward uniquely identifies one stage in that family. Pending Claim, duplicate reward
patterns, unreadable rewards, and unmapped rewards add no stage IDs.

#### Phase 6K.2 — application UI and lifecycle integration

This section records the Phase 6K.2 integration design. Its shared-controller architecture is
superseded by Phase 6K.3 below.

The MainForm exposes an `Achievements` selection that defaults off. Achievement scanning runs as a
self-contained final stage inside the existing `ScanRun`/`ScanSession` and its single
`GameInputSession`; it does not create a second controller or perform independent restoration.
Before game input, the user sees the pending-reward accuracy warning. Once the active input session
is ready, scanning pauses for the user to open the idle Achievements screen manually.

Pending Claim pauses hide the companion overlay and offer Resume, Skip Achievement, or Cancel Scan.
Resume requires the user to claim manually, return to a search-ready Achievement screen, and then
explicitly re-scans the same target. Skip records an unclaimed-reward skip and trusts no IDs for the
target. Cancellation uses the active `ScanSession` token, and unsafe reset or operational failure
still stops before another target.

GOOD-like and Traveler's Toolkit native v1 progress can be imported as a validated update seed;
unknown IDs fail closed. Trusted results can be explicitly exported as a separate deterministic
GOOD-like achievement file after complete, partial, or cancelled scans. Existing inventory GOOD
export behavior remains separate and unchanged. No reward is claimed automatically, and the
feature remains behind the release gate until Phase 6K.3 live validation is complete.

#### Phase 6K.3 — standalone keyboard/mouse fast path

Phase 6K.3 supersedes the Phase 6K.2 shared-controller staging architecture for Achievement scans.
Achievements are a separate, mutually exclusive scan mode. Selecting Achievements clears and
disables legacy categories; selecting any legacy category clears and disables Achievements until
the last legacy selection is removed. Achievement progress import controls are shown only in
Achievement mode.

The dedicated start path performs only shared window, aspect-ratio, capture, foreground, OCR,
preprocessing, cancellation, and overlay setup. It does not construct `GameScanner`, ViGEm,
`GameInputSession`, or `GameNavigator`. After the existing accuracy warning, the companion overlay
asks the user to switch Genshin Impact to Keyboard/Mouse mode and open the idle Achievements screen.
A cancellable passive readiness waiter captures until a positively supported idle layout appears;
it issues no keyboard or mouse input and replaces the former start handoff modal.

At scan start, `AchievementKeyboardMouseNavigator` foregrounds the game and moves the cursor once
to a normalized Search/Clear toggle point. That point is inside the empty search field and remains
under the Clear control while a query is active. Each healthy target then clicks without moving,
types the exact canonical title without routine Ctrl+A/Delete cleanup, waits about 100 ms, submits
with Enter, waits about 300 ms, and captures one fresh result bitmap. The healthy path performs one
screen detection and reuses that bitmap for identity, completion, and staged reward reading. Result
verification remains authoritative; malformed, unknown, multi-match, and ambiguous evidence fails
closed. A stationary click activates Clear, followed by a 250 ms settle and idle verification; a
bounded second click and exact text replacement remain recovery-only behavior. The verified Clear
state is reused as the next target's start-state proof, avoiding a routine third capture.

Achievement-only scanning registers `F8` as its explicit cancellation hotkey because Enter is part
of the search-submit input sequence. Legacy scans retain their existing Enter cancellation hotkey.
The companion overlay displays the hotkey for the active scan mode. After a pending-Claim pause,
the scanner restores the fixed pointer once and positively reacquires a supported Achievement state
before rescanning the same target. External pointer movement is likewise corrected only as a
bounded fallback, rather than causing per-target cursor movement.

Title OCR tries the threshold variant first and evaluates additional contrast/inverted variants
only when independent identity evidence is not yet acceptable. Completion OCR likewise proceeds
variant-by-variant and stops only after strong, non-conflicting evidence; fallbacks and conflict
safety remain. Reward OCR runs only for an identified stage group that is not pending Claim and
whose completion state requires stage resolution. Comparison-only apostrophe, dash, and diacritic
folding, noise-aware canonical spans, collision rules, pending-Claim behavior, and trusted-export
boundaries remain unchanged.

Live Defect #5 recovery remains conditional. The primary title crop is unchanged for healthy
targets; an expanded title-band crop ending before the completion column is OCR'd only when primary
title evidence is insufficient. A left-anchored canonical prefix of at least 20 comparison-normalized
characters may identify a unique or staged title only when the scanner's exact canonical query has
fresh `Match(es): 1` and `1/1` evidence, OCR confidence remains above the established floor, and no
other catalog title has a safely equivalent prefix. Unrelated collisions and multiple matches remain
ambiguous.

Completion semantics prioritize the dedicated state-label/progress band. Fractions found only in the
broad completion crop are not progress evidence because that crop also contains dates and reward
decoration; Claim/Completed tokens in the broad crop remain bounded fallback evidence when the label
band contains no semantic token. Progress still requires exactly one strict `current/target` value
from the label band with `current < target`; equal progress and conflicts fail closed.

When a valid single-result frame leaves identity or completion unresolved, the single scanner waits
150 ms and captures at most one fresh result frame without re-entering or resubmitting the query.
Identity recovery reruns identity only, then runs completion for the first time if identity becomes
positive. Completion recovery reruns completion only. Logs record `identityRecoveryUsed`,
`completionRecoveryUsed`, `extraCaptureCount`, `recoveryDelayMs`, and the recovery result; aggregate
timing reports separate healthy fast-path and recovered-target buckets.

An explicit keyboard/mouse no-results screen is a separate positive structural state, not a generic
blank-panel inference. When normal Match(es) evidence is absent, the detector requires multiple
Achievement-page signals, active Clear/search controls, and a structurally absent first result row,
with no contradictory positive match count. The first-row check uses the highlighted exact-query
title structure and does not invoke title OCR. OCR of the bounded central `No achievements related
to` message remains optional supporting diagnostics and may establish no-results when row presence
is otherwise unknown, but failed sentence OCR does not reject a structurally blank result.

The keyboard/mouse navigator retains the exact text it submitted. No-results classification requires
that retained submitted query to equal the target's canonical title; it does not require OCR to read
the entire query back from the search field. Visual active-search proof is still mandatory, and a
positively present first result row always returns to the ordinary identity/completion path.

For a unique hidden canonical title, positively verified exact-title no-results evidence means the
achievement is Incomplete and contributes no trusted completed ID. The same conclusion is safe for
an all-hidden canonical stage family: stage 1 is incomplete and zero stages are complete. A
non-hidden title, unrelated collision, or mixed hidden/non-hidden title group remains Unresolved as
a catalog/search mismatch. Every zero-result outcome still uses the normal stationary Clear action
and must positively return to keyboard/mouse Idle before the catalog can continue. Ordinary result
rows retain the existing identity, completion, and Defect #5 recovery requirements.

Per-target logs record focus/type/submit, result wait, capture, screen detection, identity OCR,
completion OCR, reward OCR, clear, and total durations. Completed targets also report aggregate
mean, median, and p95 total time. These diagnostics support tuning; they are not a wall-clock CI
contract or an overlay detail.

Phase 6K.3 remains behind its live validation gate. Automated coverage cannot establish production
readiness, and no production-ready claim should be made until the prescribed live Keyboard/Mouse
scan, recovery, cancellation, pending-Claim, and supported-resolution checks have passed.

---

## 18. First milestone fixture set

Before Phase 6A is considered complete, fixture coverage should include sanitized crops representing:

- Achievement screen idle;
- exact search with `Match(es): 1`;
- partial search with `Match(es) > 1`;
- completed + `Claim`;
- completed + `Completed`;
- incomplete `0/1`;
- incomplete partial progress such as `3/20`;
- highlighted matching substring;
- quoted title;
- punctuation-heavy title;
- long title;
- no-results / invalid search state if available.

All fixtures must be cropped to remove UID/account-bearing UI.

---

## 19. UI integration

Do not redesign MainForm broadly just to add this scanner.

The initial UI should fit existing scan configuration patterns.

Possible first-release controls:

- `Achievements` scan checkbox;
- status/progress text;
- optional `Use previous achievement scan` selector in a later phase;
- export location integrated with existing export destination.

Avoid Designer regeneration unless genuinely necessary.

Prefer minimal code-wired additions where practical and consistent with current UI conventions.

---

## 20. Progress reporting

The scan companion should expose meaningful achievement progress, for example:

```text
Achievements
342 / 1,500 checked
271 completed
65 incomplete
6 unresolved
```

Exact wording can be refined later.

Do not overload the overlay with raw diagnostics.

Technical details belong in logs.

---

## 21. Cancellation and recovery

Achievement scanning must obey existing lifecycle rules.

On cancellation:

- stop issuing new searches;
- cancel pending waits/retries;
- release or restore input state using the existing session lifecycle only when a legacy scan owns it;
- perform no controller restoration for an Achievement-only run because it never creates a controller;
- do not fabricate completion for the current partial observation;
- preserve results only according to existing cancellation/export policy.

On restoration failure:

- use existing Recovery behavior;
- do not introduce a second recovery overlay;
- preserve completed scan data according to current `ScanCompletionPolicy`.

---

## 22. Performance guidance

Correctness comes first.

Do not prematurely parallelize game navigation.

The game interaction portion is inherently serial.

Possible safe optimizations after correctness:

- skip previously completed achievements;
- cache normalized canonical titles;
- precompute collision groups;
- reduce OCR variants only when evidence proves a faster path is equally safe;
- batch catalog preprocessing outside the scan loop.

Do not reduce settle delays or OCR retry counts without measured live evidence.

---

## 23. Things agents must not do

Agents working under this plan must not:

- replace the search-per-achievement design with a scrolling crawler without user approval;
- use process memory or invasive game integration;
- infer completion from absence of UI evidence;
- silently guess ambiguous achievement IDs;
- copy AkashaScanner's thresholds/coordinates without re-measuring current UI;
- hardcode 1920x1080-only pixel rectangles when normalized geometry can be used;
- fetch remote achievement data during every scan;
- fabricate completion timestamps;
- redesign unrelated scanner modules;
- alter existing controller/recovery behavior without proven necessity;
- commit full screenshots containing UID/account data;
- commit build outputs, logs, or local scan files;
- commit or push unless explicitly instructed for that task.

---

## 24. Required workflow for every Phase 6 task

For every implementation slice:

1. read this entire plan;
2. read `AGENTS.md`;
3. inspect current relevant code and tests;
4. inspect the historical AkashaScanner reference only where useful;
5. document the current behavior before editing;
6. define the smallest phase-specific change;
7. implement only that phase;
8. add deterministic tests;
9. run focused tests;
10. run the full serialized functional suite;
11. run isolated concurrency benchmarks if the touched code warrants them;
12. build Release;
13. run `git diff --check`;
14. review `git status --short`;
15. perform required live validation;
16. report:
   - root cause / design evidence;
   - files changed;
   - tests;
   - build;
   - live result;
   - remaining risks;
17. stop.

Do not automatically continue into the next phase.

---

## 25. Commit discipline

Each major phase should normally land as its own commit after live validation.

Suggested commit families:

- `feat: add achievement screen detection`
- `feat: add canonical achievement catalog`
- `feat: add achievement search navigation`
- `feat: verify achievement search results`
- `feat: classify achievement completion state`
- `feat: scan single achievements end to end`
- `feat: handle achievement search collisions`
- `feat: add full achievement scanning`
- `feat: add achievement scan resume support`
- `feat: export achievement progress`

Do not squash unrelated phases together merely for convenience.

---

## 26. Release gate

Achievement Scanner must not be advertised as production-ready until all of the following are true:

- full supported catalog scan completes live;
- representative completed/incomplete/collision cases are verified;
- cancellation is live-tested;
- recovery is live-tested where relevant;
- no known false-positive completion exports remain;
- Traveler's Toolkit successfully imports the generated output;
- supported resolutions are tested;
- bounded diagnostics work;
- full automated suite passes;
- Release build succeeds;
- known limitations are documented.

---

## 27. Immediate next task

The next task after the automated Phase 6K.3 implementation gate is **Phase 6K.3 live validation**.
It must be performed manually by the user under a separately approved brief. Automated implementation
work must not launch Genshin, run a live harness, or create/control ViGEm for Achievement scanning.

---

## 28. Phase 7 — Fast Scan optimization track

Phase 7 is an approved optimization layer alongside the exact-title scanner. It does not remove,
weaken, or replace exact-title verification. The production architecture is:

```text
bulk list discovery
    -> catalog reconciliation
    -> hidden set difference after proven complete traversal
    -> completed-total checksum
    -> exact-title fallback / Full Verification where necessary
```

### Phase 7A — bulk visible-frame foundation (implemented; automated gate only)

Phase 7A adds a separate read-only pipeline for one screenshot of the ordinary Achievement list:

- normalized ordinary-list row slots, independent of search-result geometry;
- positive geometry-based visible-row detection rather than OCR-only discovery;
- lazy per-row title, completion-state, and conditional staged-reward OCR;
- fail-closed canonical identity using the Phase 6 normalization, score, margin, confidence, stage,
  and unrelated-collision rules;
- immutable row/frame results with no retained bitmaps;
- canonical-only deduplication fingerprints, with non-deduplicating diagnostic fingerprints for
  unresolved or ambiguous rows;
- nullable `Achievements completed: X` extraction;
- capture, row-detection, OCR-category, total-frame, row-count, and per-resolved-row timing;
- serial OCR because `IOcrService` does not currently guarantee thread safety;
- deterministic synthetic multi-row, mixed-identity, boundary, scaling, completed-total, staged,
  trust-extraction, and independent-row-failure coverage.

A positively identified visible hidden achievement is handled exactly like any other visible row.
Absence from a single frame proves nothing. Phase 7A does not navigate categories, scroll, inspect a
scrollbar, reconcile the catalog, infer hidden absence, invoke exact-title fallback, or expose a Fast
Scan UI. It is not production-ready without sanitized current-list fixtures and live geometry/OCR
validation.

### Later Phase 7 slices (not implemented)

- Phase 7B: bounded category traversal, scrolling, overlap handling, and complete-list proof.
- Phase 7C: catalog reconciliation and hidden-set difference only after traversal completeness is
  positively established.
- Phase 7D: completed-total checksum, exact-title fallback orchestration, and user-facing Fast Scan /
  Full Verification workflow.

Do not begin a later Phase 7 slice without a separately approved task.

