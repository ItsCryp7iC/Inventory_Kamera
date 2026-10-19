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
- reuse Inventory Kamera's existing hardened scan, controller, recovery, diagnostics, cancellation, and export infrastructure;
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
- Inventory Kamera already has a controller abstraction, recovery workflow, capture guard, failure diagnostics, and scan session lifecycle;
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

### Controller mode

Controller mode remains usable for menu navigation.

Text entry into the achievement search field still requires normal keyboard text input.

Therefore:

- controller navigation and keyboard text entry may coexist;
- the scanner does not need to switch the whole game back to Keyboard/Mouse just to enter search text;
- text injection must still be isolated behind an abstraction and must not be mixed directly into scanner logic.

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
- controller recovery lifecycle;
- result staging/publication rules;
- bounded retry behavior;
- no forced export after cancellation unless existing policy explicitly permits manual export;
- no unbounded loops.

---

## 6. Reuse existing Inventory Kamera infrastructure

Achievement scanning should reuse existing abstractions where practical.

Preferred existing components include:

- `GameInputSession`
- `GameNavigator`
- `IGameInput`
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

Do not create a parallel controller lifecycle, second recovery system, or achievement-specific global mutable input state.

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

The navigation sequence must use bounded attempts and fresh captures.

### Text entry

Keyboard text input is allowed while Genshin is in controller mode.

Requirements:

- text injection must occur only when search-field focus is positively established;
- existing search text must be cleared explicitly;
- the scanner must not type blindly into an unknown screen;
- after typing, search submission must use a verified semantic action;
- after classification, search must be cleared/closed through a verified action.

If search focus or screen state cannot be proven, enter safe recovery or fail the achievement observation.

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

At the end of the scan, the scanner should report each skipped achievement's name, canonical ID or
stage group when known, and reason for the skip. If stage context remains ambiguous after pending
rewards are cleared, the scanner must fail closed rather than guess. This is a future orchestration
and full-scan rule; Phase 6E documents it but does not implement its UI or workflow.

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

Safely automate one search in controller mode.

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
- release or restore input state using the existing session lifecycle;
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

The next implementation task is **Phase 6A only**:

> Achievement screen and search-state detector.

It must not implement full scanning, catalog iteration, export, resume, or collision correction yet.

The first Phase 6A agent should:

- derive normalized geometry from the supplied current Genshin screenshots;
- create sanitized crops/fixtures;
- implement Achievement-screen detection;
- implement search idle/active detection;
- implement `Match(es): N` parsing;
- expose normalized result-title and completion-state crop regions;
- add diagnostics and deterministic tests;
- validate across current 16:9 and 16:10 scaling conventions;
- stop before navigation automation.

