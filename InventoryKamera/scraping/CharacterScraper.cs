using InventoryKamera.game;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace InventoryKamera
{
    internal class CharacterScraper
	{
		private static NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

		protected int NumOfCharToScan;

		private readonly IOcrService ocrService;
		private readonly IImagePreprocessor imagePreprocessor;
		private readonly IScanSettings scanSettings;
		private readonly IScanProgressReporter progressReporter;
		private readonly ScanSession scanSession;

        public CharacterScraper(
			IOcrService ocrService,
			IImagePreprocessor imagePreprocessor,
			IScanSettings scanSettings,
			IScanProgressReporter progressReporter,
			ScanSession scanSession)
		{
			this.ocrService = ocrService;
			this.imagePreprocessor = imagePreprocessor;
			this.scanSettings = scanSettings;
			this.progressReporter = progressReporter;
			this.scanSession = scanSession ?? throw new ArgumentNullException(nameof(scanSession));
			NumOfCharToScan = scanSettings.NumOfCharToScan;
		}

		/// <summary>
		/// Childe (Tartaglia) passive buff fix -- his kit grants +1 auto-attack talent level to the
		/// first 4 party members once he's Ascension 4+, which the raw scanned talent level doesn't
		/// reflect. Used by the controller-driven batched path (<see cref="ScanCharacters"/>)
		/// since it only needs the final assembled roster, not how it was scanned.
		/// </summary>
		private void ApplyTartagliaFix(List<Character> Characters)
		{
			for (int i = 0; i < Characters.Count; i++)
			{
				if (Characters[i].NameGOOD.ToLower() == "tartaglia" && Characters[i].Ascension >= 4)
				{
					Logger.Info("Ascension 4+ Tartaglia found at position {0}.", i);
					if (i < 4)
					{
						for (int j = 0; j < 4; j++)
						{
							Characters[j].Talents["auto"] -= 1;
							Logger.Info("Applied Tartaglia auto attack fix to {0} at position {1}.", Characters[j].NameGOOD, j);
						}
						break;
					}
					else
					{
						Characters[i].Talents["auto"] -= 1;
						Logger.Info("Applied Tartaglia auto attack fix to self only.");
						break;
					}
				}
				else if (Characters[i].NameGOOD.ToLower() == "tartaglia") break;
			}
		}

		/// <summary>
		/// Skirk passive buff fix -- per user (2026-07-05), her kit grants +1 Skill talent level to
		/// the whole active team whenever that team (the first 4 scanned characters, matching
		/// <see cref="ApplyTartagliaFix"/>'s own team-position convention) is composed entirely of
		/// Hydro and Cryo characters, which the raw scanned talent level doesn't reflect. Unlike
		/// Tartaglia's fix, there's no ascension requirement, and there's no self-only fallback for an
		/// off-team Skirk -- her buff simply doesn't apply if she isn't one of the first 4. Stacks
		/// additively with any other active team buff on the same talent (only Tartaglia's exists
		/// today, and it targets a different talent, "auto", so there's no actual overlap yet).
		/// </summary>
		private void ApplySkirkFix(List<Character> Characters)
		{
			if (Characters.Count < 4) return;

			int skirkIndex = Characters.FindIndex(c => c.NameGOOD.ToLower() == "skirk");
			if (skirkIndex < 0 || skirkIndex >= 4) return;

			bool wholeTeamHydroOrCryo = Characters.Take(4).All(c =>
				c.Element != null && (c.Element.ToLower() == "hydro" || c.Element.ToLower() == "cryo"));

			if (!wholeTeamHydroOrCryo) return;

			Logger.Info("Skirk found on an all-Hydro/Cryo team -- applying team Skill buff correction.");
			for (int j = 0; j < 4; j++)
			{
				Characters[j].Talents["skill"] -= 1;
				Logger.Info("Applied Skirk skill fix to {0} at position {1}.", Characters[j].NameGOOD, j);
			}
		}

		/// <summary>
		/// Controller-driven character scan (Phase 3 §6c), batched by sub-tab across the whole roster
		/// instead of per-character. Per user design decision (2026-07-05): switching the character
		/// screen's sub-tab (left-stick up/down; full order is Attributes, Weapons, Artifacts,
		/// Constellations, Talents, Profile) pays a real UI transition-animation cost, while advancing
		/// between characters (right shoulder button, mirroring how
		/// <see cref="InventoryScraper.SwitchToTab"/> cycles inventory tabs with LB/RB)
		/// does not. Interleaving sub-tabs per character would pay that animation cost repeatedly per
		/// character; this instead pays it exactly twice for the whole scan (Attributes to
		/// Constellations, a 3-step move past Weapons/Artifacts; Constellations to Talents, 1 step),
		/// independent of roster size -- at the cost of doing 3 full passes over the roster instead of
		/// 1. That trade favors batching since shoulder-tap advance is the cheap, already-fast-tuned
		/// primitive elsewhere (see <see cref="WeaponScraper.ScanWeapons"/>). Weapons and
		/// Artifacts sub-tabs are only transited through, never scanned, from this method (equipped
		/// gear is already covered by <see cref="WeaponScraper"/>/<see cref="ArtifactScraper"/>'s own
		/// controller scans).
		/// No dedicated rewind-to-start step exists between phases (per user, 2026-07-05): a full
		/// scan's cursor is already back on the first character after Phase 1's wraparound detection,
		/// so Phase 2/3 just read forward again (which wraps around for free, since count equals the
		/// whole roster). A partial scan's cursor instead sits one character past the last one
		/// scanned (no wraparound, since the roster has more characters left over) -- Phase 2 reads
		/// backward from there (<see cref="ScanRosterBackward"/>) to reach the same characters a
		/// rewind-then-forward pass would, in half the shoulder taps, which conveniently leaves the
		/// cursor back on the first character for Phase 3 to read forward
		/// (<see cref="ScanRosterForward"/>) same as the full-scan case.
		/// Constellation-node navigation and talent reading each get their own controller-native
		/// method (<see cref="ScanConstellations"/>/<see cref="ScanTalents"/>)
		/// rather than reusing the mouse path's per-node click loop.
		/// <paramref name="Characters"/> is scanned up to <see cref="NumOfCharToScan"/> entries (0 =
		/// whole roster).
		/// Returns false only when Character menu entry could not be visually verified; no roster input
		/// is sent in that case.
		/// </summary>
		public bool ScanCharacters(
			GameNavigator navigator,
			PaimonMenuNavigator paimonMenuNavigator,
			ref List<Character> Characters)
		{
			int maxToScan = NumOfCharToScan;
			if (maxToScan != 0) progressReporter.SetCharacter_Max(maxToScan);
			progressReporter.ResetCharacterDisplay();
			CharacterNavigationTiming characterTiming = CharacterNavigationTiming.FromCurrentScanSpeed();
			Logger.Info(
				"Character scan timing: requested={0} ({1:0.###}x), effective={2} ({3:0.###}x)",
				characterTiming.RequestedTier,
				characterTiming.RequestedMultiplier,
				characterTiming.EffectiveTier,
				characterTiming.EffectiveMultiplier);

			if (!EnterCharacterMenu(navigator, paimonMenuNavigator)) return false;

			// Per user (2026-07-05): the Character menu always opens on the Attributes sub-tab
			// regardless of what was open last time, so no reset-to-known-position step is needed
			// here. Also per user: unlike the pause-menu tab bar, this sub-tab control is
			// unbounded/circular (Up from Attributes wraps around to Profile, the last sub-tab, not
			// clamped) -- the clamped-control "over-shoot is harmless" idiom used elsewhere
			// (GameNavigator.MashBack, an earlier version of this method) does NOT apply here and
			// would actively misnavigate.
			// --- Phase 1: Attributes (name, element, level) for the whole roster ---
			string firstName = null;
			var scanned = new HashSet<string>();

			// True once the loop breaks via wraparound (cursor already sitting back on the first
			// character); false if it breaks via the scan-count cap or a cancel request (cursor
			// sitting one character past the last one scanned, since the roster has more characters
			// than were scanned so no wraparound happened). Drives Phase 2's read direction below.
			bool endedAtFirstCharacter = false;

			// Physical shoulder-tap gap between consecutive recorded characters -- NOT assumed to be
			// 1. Per user (2026-07-05, live-tested): a manequin slot (no constellation page to enter)
			// sitting between recorded characters caused Phase 2 to land on it and press confirm,
			// which closed the whole Character menu -- tracing back to Phase 2/3 assuming 1 shoulder
			// tap always equals 1 recorded character, when a manequin (or a duplicate-name repeat)
			// consumes a tap without being recorded into Characters. gapsBeforeEach[i] is the tap
			// count from Characters[i-1] (or the scan's starting position, for i==0, always 0) to
			// reach Characters[i] -- replayed exactly by ScanRosterForward/ScanRosterBackward below
			// instead of a uniform single-tap assumption.
			var gapsBeforeEach = new List<int>();
			int gapSinceLastRecorded = 0;
			int wrapGap = 0; // taps from the last recorded character back to the first, once wrapped
			int unrecognizedCount = 0; // non-manequin slots whose name/element couldn't be read

			while (true)
			{
				string name = null, element = null;
				string rawRead = ScanNameAndElement(characterTiming, ref name, ref element);

				bool isManequin = name == "Manequin1" || name == "Manequin2";
				bool hasValidNameAndElement = !isManequin && !string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(element);

				if (hasValidNameAndElement)
				{
					if (Characters.Count > 0 && name == firstName)
					{
						// Wrapped back to the first character -- stop before advancing again so the
						// cursor is left sitting on it.
						wrapGap = gapSinceLastRecorded;
						endedAtFirstCharacter = true;
						break;
					}

					bool ascended = false;
					int level = ScanLevel(characterTiming, ref ascended);
					if (level == -1)
					{
						progressReporter.AddError($"Could not determine {name}'s level. Setting to 1.");
						level = 1;
						ascended = false;
					}

					// Log the Attributes screen here -- after name/element/level are read but before the
					// duplicate gate below -- so the full-window capture always lands even when level (or
					// any other field) failed to parse. That failing case is exactly what's worth
					// debugging for a new aspect ratio like 16:10, where a mis-placed region is the reason
					// the read failed in the first place.
					LogCharacterScreenshot(name, "attributes", NameElementRegion());
					LogCharacterWindow(name, "attributes");

					if (!scanned.Contains(name))
					{
						var character = new Character
						{
							NameGOOD = name,
							Element = element,
							Level = level,
							Ascended = ascended
						};
						Characters.Add(character);
						gapsBeforeEach.Add(gapSinceLastRecorded);
						gapSinceLastRecorded = 0;
						scanned.Add(name);
						progressReporter.IncrementCharacterCount();
						if (Characters.Count == 1) firstName = name;
						Logger.Info("{0} attributes: element={1}, level={2}{3}", character.NameGOOD, character.Element, character.Level, character.Ascended ? "+" : "");
					}
					else
					{
						Logger.Info("Prevented {0} duplicate scan (controller)", name);
					}
				}
				else if (!isManequin)
				{
					// Name and element are read from a single combined OCR string and always fail
					// together (ScanNameAndElement clears both on any parse/element-mismatch failure),
					// so this is one combined error, not two -- with the raw OCR text so the user can see
					// what was actually read.
					progressReporter.AddError($"Could not determine character element and name (\"{rawRead}\")");

					// Unrecognized, non-manequin slot: it never gets added to Characters, so it's
					// already skipped by the Constellations (Phase 2) and Talents (Phase 3) passes (both
					// only iterate recorded characters). Save one full-window screenshot so the user can
					// identify who was skipped -- it goes in the top-level characters folder (not a
					// per-character subfolder, since we have no name to file it under). Saved regardless
					// of LogScreenshots: an unrecognized character is a failure worth capturing.
					unrecognizedCount++;
					Logger.Warn("Unrecognized character (raw OCR: \"{0}\") -- skipping all passes and saving a full screenshot.", rawRead);
					Directory.CreateDirectory("./logging/characters");
					using (var screenshot = Navigation.CaptureWindow())
						screenshot.Save($"./logging/characters/unrecognized_{unrecognizedCount}.png");
				}

				navigator.TapNextTab(characterTiming.Scale(80));
				Thread.Sleep(characterTiming.Scale(100));
				gapSinceLastRecorded++;

				if (maxToScan != 0 && Characters.Count >= maxToScan) break;
				if (scanSession.IsCancellationRequested)
				{
					Logger.Info("Stopping character scan: cancel requested");
					break;
				}
			}

			int count = Characters.Count;
			if (count == 0) return true;

			// Per user (2026-07-05): a cancel request during Phase 1 was respected (the loop above
			// already checks it), but Phase 2/3 never checked it at all, so a cancel mid-scan
			// silently kept running the full Constellations/Talents passes over every character
			// already recorded instead of stopping. If Phase 1 itself was cancelled, skip straight to
			// the Tartaglia fix on whatever was scanned rather than starting Phase 2/3 at all.
			if (scanSession.IsCancellationRequested)
			{
				ApplyTartagliaFix(Characters);
				ApplySkirkFix(Characters);
				return true;
			}

			// Local functions/lambdas below can't capture the `ref` parameter Characters directly
			// (CS1628) -- alias it to a plain local. Same List<Character> instance either way.
			List<Character> characterList = Characters;

			// Gap from the last recorded character to wherever the cursor now sits: back at the
			// first character (full scan, via wrapGap) or however many manequin/duplicate slots past
			// the last one scanned (partial scan or cancel, via whatever accrued since).
			int gapAfterLast = endedAtFirstCharacter ? wrapGap : gapSinceLastRecorded;

			// --- Phase 2: Constellations for the whole roster ---
			// Per user (2026-07-05): the full sub-tab order is Attributes, Weapons, Artifacts,
			// Constellations, Talents, Profile -- Constellations is 3 stops down from Attributes, not
			// 1 (Weapons and Artifacts sit between them).
			navigator.Move(GameNavigator.MenuDirection.Down, 3,
				holdMs: characterTiming.Scale(150),
				settleMs: characterTiming.Scale(150));

			void ScanConstellation(Character character)
			{
				// Verify the roster cursor is actually on this character before pressing B -- on a
				// manequin (no constellation page) B closes the whole Character menu and derails the
				// scan. See VerifyOnExpectedCharacter.
				if (!VerifyOnExpectedCharacter(characterTiming, character, "Constellation")) return;

				// Per user (2026-07-05): greedy (C6-first, read backward) mode only for 4-star
				// characters so far -- see IsFourStarCharacter/ScanConstellationsGreedy.
				character.Constellation = IsFourStarCharacter(character)
					? ScanConstellationsGreedy(navigator, character, characterTiming)
					: ScanConstellations(navigator, character, characterTiming);
				Logger.Info("{0} Constellation: {1}", character.NameGOOD, character.Constellation);
			}

			// Per user (2026-07-05): no rewind step -- a full scan's cursor is already sitting on the
			// first character (read forward, which returns to the start again via wrapGap, gap-aware
			// rather than assuming count taps), and a partial scan's cursor instead sits gapAfterLast
			// characters past the last one scanned, so reading backward from there reaches the same
			// characters a rewind-then-forward pass would, for far fewer shoulder taps.
			if (endedAtFirstCharacter)
				ScanRosterForward(navigator, characterTiming, characterList, gapsBeforeEach, gapAfterLast, ScanConstellation);
			else
				ScanRosterBackward(navigator, characterTiming, characterList, gapsBeforeEach, gapAfterLast, ScanConstellation);

			if (scanSession.IsCancellationRequested)
			{
				ApplyTartagliaFix(Characters);
				ApplySkirkFix(Characters);
				return true;
			}

			// --- Phase 3: Talents for the whole roster ---
			// Talents is immediately adjacent to Constellations in the sub-tab order, so this stays a
			// single step. Both Phase 2 branches above leave the cursor sitting back on the first
			// character (a full lap for the forward read, or the natural endpoint of the backward
			// read), so this phase can always read forward. No trailing gap needed (null) since this
			// is the last phase -- nothing follows that needs the cursor back at the start.
			navigator.Move(GameNavigator.MenuDirection.Down, 1,
				holdMs: characterTiming.Scale(150),
				settleMs: characterTiming.Scale(150));

			ScanRosterForward(navigator, characterTiming, characterList, gapsBeforeEach, null, character =>
			{
				// Same identity check as the constellation pass: confirm the cursor is on this
				// character before reading talents, so a drifted cursor doesn't record a manequin's or
				// the wrong character's talent levels.
				if (!VerifyOnExpectedCharacter(characterTiming, character, "Talent")) return;

				character.Talents = ScanTalents(character, characterTiming);
				Logger.Info("{0} Talents: {1}", character.NameGOOD, "{" + string.Join(", ", character.Talents.Select(kv => kv.Key + "=" + kv.Value).ToArray()) + "}");

				ApplyConstellationTalentScaling(character);
			});

			ApplyTartagliaFix(Characters);
			ApplySkirkFix(Characters);
			return true;
		}

		/// <summary>
		/// Advances <paramref name="taps"/> times in one direction, one shoulder-button tap at a time
		/// (never a single multi-position jump), matching how <see cref="ScanCharacters"/>'s
		/// Phase 1 measured each gap the same way.
		/// </summary>
		private void AdvanceRoster(
			GameNavigator navigator,
			CharacterNavigationTiming timing,
			bool forward,
			int taps)
		{
			for (int t = 0; t < taps; t++)
			{
				if (forward)
					navigator.TapNextTab(timing.Scale(80));
				else
					navigator.TapPreviousTab(timing.Scale(80));
				Thread.Sleep(timing.Scale(100));
			}
		}

		/// <summary>
		/// Runs <paramref name="scanCharacter"/> once per character in <paramref name="characters"/>,
		/// assuming the cursor is already sitting on the first one, advancing forward (right shoulder)
		/// by the exact physical gap to the next character after each read -- <paramref
		/// name="gapsBeforeEach"/>[i] is the gap consumed to reach <paramref name="characters"/>[i]
		/// (see <see cref="ScanCharacters"/>'s Phase 1 for how it's measured), reused
		/// here to advance from character i to i+1. After the last character, advances by <paramref
		/// name="gapAfterLast"/> if given (used when the following phase also needs the cursor back
		/// at the start), or not at all if null (the last phase needs no such trailing move).
		/// </summary>
		private void ScanRosterForward(
			GameNavigator navigator,
			CharacterNavigationTiming timing,
			List<Character> characters,
			List<int> gapsBeforeEach,
			int? gapAfterLast,
			Action<Character> scanCharacter)
		{
			int count = characters.Count;
			for (int i = 0; i < count; i++)
			{
				scanCharacter(characters[i]);

				// Per user (2026-07-05): a cancel request during Phase 2/3 previously went
				// unchecked, silently finishing the whole roster pass instead of stopping.
				if (scanSession.IsCancellationRequested)
				{
					Logger.Info("Stopping character scan: cancel requested");
					break;
				}

				int? gap = i < count - 1 ? gapsBeforeEach[i + 1] : gapAfterLast;
				if (gap.HasValue) AdvanceRoster(navigator, timing, forward: true, taps: gap.Value);
			}
		}

		/// <summary>
		/// Runs <paramref name="scanCharacter"/> once per character in <paramref name="characters"/>,
		/// last to first, assuming the cursor is sitting <paramref name="gapAfterLast"/> physical
		/// positions past the last character, advancing backward (left shoulder) by the exact gap
		/// before each read -- the mirror image of <see cref="ScanRosterForward"/>, reusing the same
		/// <paramref name="gapsBeforeEach"/> measurements since the physical distance between two
		/// characters doesn't depend on which direction it's crossed. Used for a partial scan, where
		/// reading backward from Phase 1's stopping point reaches the same characters a
		/// rewind-to-start-then-forward pass would, without the wasted rewind taps.
		/// </summary>
		private void ScanRosterBackward(
			GameNavigator navigator,
			CharacterNavigationTiming timing,
			List<Character> characters,
			List<int> gapsBeforeEach,
			int gapAfterLast,
			Action<Character> scanCharacter)
		{
			int count = characters.Count;
			for (int i = count - 1; i >= 0; i--)
			{
				int gap = i == count - 1 ? gapAfterLast : gapsBeforeEach[i + 1];
				AdvanceRoster(navigator, timing, forward: false, taps: gap);
				scanCharacter(characters[i]);

				// Per user (2026-07-05): a cancel request during Phase 2/3 previously went
				// unchecked, silently finishing the whole roster pass instead of stopping.
				if (scanSession.IsCancellationRequested)
				{
					Logger.Info("Stopping character scan: cancel requested");
					break;
				}
			}
		}

		/// <summary>
		/// Opens Genshin's pause menu and enters Character through the shared state-aware Paimon-menu
		/// navigator. The leading Escape + MashBack preserve the existing free-roam reset before the
		/// navigator begins its capture/move/re-detect loop.
		/// </summary>
		private bool EnterCharacterMenu(GameNavigator navigator, PaimonMenuNavigator paimonMenuNavigator)
		{
			Navigation.sim.Keyboard.KeyPress(Navigation.escapeKey);
			Navigation.SystemWait(Navigation.Speed.UI);

			// Safety net (same idiom as GameNavigator.ExitControllerMode): the previous
			// controller-driven phase's teardown already backs out of any nested menu, but
			// EnterControllerMode()'s A-press below assumes a clean free-roam state to avoid
			// triggering an unwanted in-game action instead of a scheme-switch nudge. Over-pressing
			// A here is harmless once already at the root (per MashBack's own doc comment).
			navigator.MashBack();

			// Paimon navigation is already live-verified at the requested global speed. The
			// Character-specific safe profile begins only after this entry succeeds.
			var timing = new PaimonMenuNavigationTiming(
				controllerModeSettleMs: InventoryScraper.ScaledControllerDelay(2000),
				menuOpenSettleMs: InventoryScraper.ScaledControllerDelay(2000),
				moveHoldMs: InventoryScraper.ScaledControllerDelay(PaimonMenuNavigationTiming.SingleStepHoldMs),
				moveSettleMs: InventoryScraper.ScaledControllerDelay(PaimonMenuNavigationTiming.SingleStepSettleMs),
				preConfirmSettleMs: InventoryScraper.ScaledControllerDelay(600),
				confirmHoldMs: InventoryScraper.ScaledControllerDelay(300),
				destinationSettleMs: Math.Max(3000, InventoryScraper.ScaledControllerDelay(2000)),
				detectionRetryMs: InventoryScraper.ScaledControllerDelay(300));

			using (PaimonMenuNavigationResult result = paimonMenuNavigator.OpenCharacter(timing))
			{
				if (result.Success)
				{
					Logger.Info("State-aware Character entry succeeded. {0}", result.DetectionDetails);
					return true;
				}

				string error = result.Message + " Character scanning was skipped. " + result.DetectionDetails;
				Logger.Error(error);
				progressReporter.AddError(error);
				if (result.DiagnosticScreenshot != null)
				{
					const string path = "./logging/paimonmenu/character_entry_failure.png";
					Directory.CreateDirectory(Path.GetDirectoryName(path));
					result.DiagnosticScreenshot.Save(path);
				}
				return false;
			}
		}

		/// <summary>
		/// Re-reads the currently-selected slot's name/element header and returns whether it matches
		/// the character we expect to be scanning. Phases 2 (Constellations) and 3 (Talents) both
		/// replay Phase 1's shoulder-tap gaps to land back on each recorded character while skipping
		/// manequin slots; if that replay drifts even one slot (e.g. a constellation enter/exit not
		/// returning the roster cursor to the exact same position), this catches it before we act on
		/// the wrong slot -- pressing confirm on a manequin closes the whole Character menu, and reading
		/// a wrong slot records another character's constellation/talent data. Returns false (and logs)
		/// on any mismatch or unreadable slot, so the caller skips that character rather than corrupting
		/// it. Uses a small retry budget (fails fast on a wrong slot); the name/element header is shown
		/// on every Character sub-tab, so it reads the same region as Phase 1's Attributes read.
		/// </summary>
		private bool VerifyOnExpectedCharacter(
			CharacterNavigationTiming timing,
			Character character,
			string phaseLabel)
		{
			string currentName = null, currentElement = null;
			ScanNameAndElement(timing, ref currentName, ref currentElement, maxAttempts: 5);
			if (currentName == character.NameGOOD) return true;

			Logger.Warn("{0} scan expected \"{1}\" but the selected slot reads \"{2}\" -- skipping this character.",
				phaseLabel, character.NameGOOD, currentName ?? "(unreadable)");
			return false;
		}

		/// <summary>
		/// The always-visible name/element header region on the Character screen. Shared by
		/// <see cref="ScanNameAndElement"/>'s OCR read and Phase 1's per-character attributes
		/// screenshot. Measured (2026-07-05) with <c>ui/CoordinatePickerForm.cs</c>.
		/// </summary>
		private static Rectangle NameElementRegion() => new RECT(
			Left:   (int)( 0.0941 * Navigation.GetWidth() ),
			Top:    (int)( 0.0430 * Navigation.GetHeight() ),
			Right:  (int)( 0.2442 * Navigation.GetWidth() ),
			Bottom: (int)( 0.0920 * Navigation.GetHeight() ));

		/// <summary>
		/// Saves a screenshot of just <paramref name="region"/> (the section actually being scanned,
		/// not the whole window) under a per-character logging folder, gated on
		/// <see cref="scanSettings"/>'s LogScreenshots setting -- shared by every controller-path
		/// capture point (<see cref="ScanCharacters"/>'s Attributes read,
		/// <see cref="ScanConstellations"/> per node, <see cref="ScanTalents"/>). <paramref
		/// name="relativeName"/> may contain '/' to nest into a subfolder (e.g.
		/// <c>"constellations/constellation_1"</c>).
		/// </summary>
		private void LogCharacterScreenshot(string characterName, string relativeName, Rectangle region, bool force = false)
		{
			if (!force && !scanSettings.LogScreenshots) return;

			string path = $"./logging/characters/{characterName}/{relativeName}.png";
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			using (var screenshot = Navigation.CaptureRegion(region))
				screenshot.Save(path);
		}

		/// <summary>
		/// Saves a full-window screenshot (the whole game window, not just the scanned region) next to
		/// the region crop that <see cref="LogCharacterScreenshot"/> writes, gated on the same
		/// LogScreenshots setting. Purpose is coordinate discovery: every character-scan region in this
		/// class is expressed as a window-size fraction, and re-deriving those fractions for a new
		/// aspect ratio (e.g. 16:10) needs a full-window capture of each screen to measure against. The
		/// resolution is baked into the filename so captures from different resolutions/aspect ratios
		/// sit side by side instead of overwriting each other. <paramref name="relativeName"/> may
		/// contain '/' to nest into a subfolder, same as <see cref="LogCharacterScreenshot"/>.
		/// </summary>
		private void LogCharacterWindow(string characterName, string relativeName)
		{
			if (!scanSettings.LogScreenshots) return;

			string path = $"./logging/characters/{characterName}/{relativeName}_full_{Navigation.GetWidth()}x{Navigation.GetHeight()}.png";
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			using (var screenshot = Navigation.CaptureWindow())
				screenshot.Save(path);
		}

		/// <summary>
		/// Applies the constellation-3/5 talent-level discount (Genshin auto-grants a talent level
		/// via certain constellations, which the raw scanned talent level doesn't reflect). Used by
		/// the controller-driven batched path (<see cref="ScanCharacters"/>), since it
		/// only needs the assembled name/element/constellation/talents, not how they were scanned.
		/// </summary>
		private void ApplyConstellationTalentScaling(Character character)
		{
			// Pyro Traveler's own constellation scan can't be trusted (per user, 2026-07-05): the
			// secondary constellations that grant +3 to a talent are visually embedded in the same 6
			// nodes as the base unlocks, so ScanConstellations can misreport a
			// lower-constellation Pyro Traveler as C6. Per user: default to assuming C0 and infer the
			// real constellation instead from the raw scanned talent levels -- a Skill level of 11+
			// is only reachable if the C3-equivalent +3 Skill bonus is active, and a Burst level of
			// 11+ only if the C5-equivalent +3 Burst bonus is active (unlocks are sequential, so
			// Burst>=11 implies the Skill bonus is active too). This replaces, rather than
			// supplements, the generic scanned-Constellation-based discount below for this character.
			if (character.NameGOOD.Contains("Traveler") && character.Element?.ToLower() == "pyro")
			{
				int constellation = 0;

				if (character.Talents.TryGetValue("skill", out int skillLevel) && skillLevel >= 11)
				{
					constellation = 3;
					character.Talents["skill"] -= 3;
					Logger.Info("{0}: scanned Skill level {1} implies at least C3 -- adjusted to {2}.",
						character.NameGOOD, skillLevel, character.Talents["skill"]);
				}

				if (character.Talents.TryGetValue("burst", out int burstLevel) && burstLevel >= 11)
				{
					constellation = 5;
					character.Talents["burst"] -= 3;
					Logger.Info("{0}: scanned Burst level {1} implies at least C5 -- adjusted to {2}.",
						character.NameGOOD, burstLevel, character.Talents["burst"]);
				}

				Logger.Info("{0} (Pyro): constellation corrected from scanned {1} to inferred {2}.",
					character.NameGOOD, character.Constellation, constellation);
				character.Constellation = constellation;
				return;
			}

			if (character.Constellation < 3) return;

			string lookupKey = character.NameGOOD.Contains("Traveler") ? "traveler" : character.NameGOOD.ToLower();

			if (GenshinProcesor.Characters.TryGetValue(lookupKey, out var characterData))
			{
				if (characterData["ConstellationOrder"] == null)
				{
					// Every character in the database should have this field -- its absence
					// means the character data failed to fully download/parse, not that this
					// character legitimately lacks constellation-order data.
					progressReporter.AddError($"{character.NameGOOD}: missing ConstellationOrder data. Talent levels were not adjusted for constellation bonuses.");
					return;
				}

				// For the Traveler the order is keyed by element (a JObject); every other
				// character stores a flat [const3Talent, const5Talent] JArray. Resolve to that
				// two-element array up front and validate it. A Traveler whose per-element entry
				// is missing or malformed (an incomplete auto-build, or a legacy nested
				// characters.json) would otherwise index into null here and throw a
				// NullReferenceException that aborts the entire character scan.
				JToken constellationOrder;
				if (character.NameGOOD.Contains("Traveler"))
				{
					constellationOrder = characterData["ConstellationOrder"] is JObject elementOrders && character.Element != null
						? elementOrders[character.Element.ToLower()]
						: null;
				}
				else
				{
					constellationOrder = characterData["ConstellationOrder"];
				}

				if (!(constellationOrder is JArray order) || order.Count < 2)
				{
					progressReporter.AddError($"{character.NameGOOD}: missing or malformed ConstellationOrder data. Talent levels were not adjusted for constellation bonuses.");
					return;
				}

				string talentLeveledAtConst3 = (string)order[0];
				string talentLeveledAtConst5 = (string)order[1];

				if (character.Constellation >= 3)
				{
					Logger.Info("{0} constellation 3+, adjusting scanned {1} level", character.NameGOOD, talentLeveledAtConst3);
					character.Talents[talentLeveledAtConst3] -= 3;
				}

				if (character.Constellation >= 5)
				{
					Logger.Info("{0} constellation 5+, adjusting scanned {1} level", character.NameGOOD, talentLeveledAtConst5);
					character.Talents[talentLeveledAtConst5] -= 3;
				}
			}
		}

		public static string ScanMainCharacterName(IOcrService ocrService, IImagePreprocessor imagePreprocessor, IScanProgressReporter progressReporter)
		{
			var xReference = 1280.0;
			var yReference = 720.0;
			if (Navigation.GetAspectRatio() == new Size(8, 5))
			{
				yReference = 800.0;
			}

			RECT region = new RECT(
				Left:   (int)(185 / xReference * Navigation.GetWidth()),
				Top:    (int)(26  / yReference * Navigation.GetHeight()),
				Right:  (int)(460 / xReference * Navigation.GetWidth()),
				Bottom: (int)(60  / yReference * Navigation.GetHeight()));

			Bitmap nameBitmap = Navigation.CaptureRegion(region);

			//Image Operations
			GenshinProcesor.SetGamma(0.2, 0.2, 0.2, ref nameBitmap);
			imagePreprocessor.SetInvert(ref nameBitmap);
			Bitmap n = imagePreprocessor.ConvertToGrayscale(nameBitmap);

			progressReporter.SetNavigation_Image(nameBitmap);

			string text = ocrService.AnalyzeText(n).Trim();
			if (text != "")
			{
				// Only keep a-Z and 0-9
				text = Regex.Replace(text, @"[\W_]", string.Empty).ToLower();

				// Only keep text up until first space
				text = Regex.Replace(text, @"\s+\w*", string.Empty);

			}
			else
			{
				progressReporter.AddError(text);
			}
			n.Dispose();
			nameBitmap.Dispose();
			return text;
		}

		/// <summary>
		/// Reads the always-visible name/element region on the Attributes sub-tab. Region re-measured
		/// (2026-07-05) with <c>ui/CoordinatePickerForm.cs</c> against a two-line wrapped name.
		/// <paramref name="maxAttempts"/> caps the retry loop -- Phase 1's roster read uses the default
		/// 20 (a genuine character should read within that), while the Phase 2 constellation guard
		/// passes a smaller budget so it fails fast on a manequin/wrong slot instead of burning the full
		/// 20 retries before skipping. Returns the raw OCR text from the final attempt (name and
		/// element are read from one combined OCR string, so callers can surface it in a failure
		/// message to show what was actually read).
		/// </summary>
		private string ScanNameAndElement(
			CharacterNavigationTiming timing,
			ref string name,
			ref string element,
			int maxAttempts = 20)
		{
			int attempts = 0; // reduced from 75 per user (2026-07-05) -- excessive when parsing genuinely fails
			Rectangle region = NameElementRegion();
			string rawText = "";

			do
			{
				using (Bitmap bm = Navigation.CaptureRegion(region))
				{
					Bitmap n = imagePreprocessor.ConvertToGrayscale(bm);
					imagePreprocessor.SetThreshold(110, ref n);
					imagePreprocessor.SetInvert(ref n);

					n = GenshinProcesor.ResizeImage(n, n.Width * 2, n.Height * 2);
					string block = ocrService.AnalyzeText(n, Tesseract.PageSegMode.Auto).ToLower().Trim();
					string line = ocrService.AnalyzeText(n, Tesseract.PageSegMode.SingleLine).ToLower().Trim();

					// Characters with wrapped names will not have a slash
					string nameAndElement = line.Contains("/") ? line : block;
					rawText = nameAndElement;

					if (nameAndElement.Contains("/"))
					{
						var split = nameAndElement.Split('/');

						// Search for element and character name in block

						// Long name characters might look like
						// <Element>   <First Name>
						// /           <Last Name>
						// Per user (2026-07-05, live screenshot): the wrapped case's first line holds
						// BOTH the element and the first word of the name (e.g. "Anemo Yumemizuki"),
						// which the element-extraction below already isolated correctly, but the name
						// search previously used only the text after "/" ("Mizuki"), silently dropping
						// "Yumemizuki" -- fixed by carrying the leftover first-line text forward into
						// the name search instead of discarding it.
						string namePart1 = "";
						if (!split[0].Contains(" "))
						{
							element = GenshinProcesor.FindElementByName(split[0].Trim());
						}
						else
						{
							var firstLineWords = split[0].Split(new[] { ' ' }, 2);
							element = GenshinProcesor.FindElementByName(firstLineWords[0].Trim());
							if (firstLineWords.Length > 1) namePart1 = firstLineWords[1];
						}

						// Find character based on the leftover first-line text (if any) plus the
						// string after /. Long name characters might search by their last name only
						// but it'll still work in the non-wrapped case (namePart1 stays empty).
						name = GenshinProcesor.FindClosestCharacterName(Regex.Replace(namePart1 + split[1], @"[\W]", string.Empty));

						if (!GenshinProcesor.CharacterMatchesElement(name, element)) { name = ""; element = ""; }
                    }
					n.Dispose();

					if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(element))
					{
						Logger.Debug("Scanned character name as {0} with element {1}", name, element);
                        progressReporter.SetCharacter_NameAndElement(bm, name, element);
						return rawText;
					}
					else
                    {
                        // Per-attempt retry: don't dump a screenshot here -- it used to litter the
                        // top-level ./logging/characters folder with hash-named images (one per failed
                        // attempt, ungated). A single full-window screenshot is saved once by the Phase 1
                        // caller if the character stays unrecognized after all retries (unrecognized_N.png).
                        Logger.Debug("Could not parse character name/element (attempt {0}/{1}). Retrying...", attempts+1, maxAttempts);
                    }
				}
				attempts++;
				Thread.Sleep(timing.Scale(200));
			} while ( attempts < maxAttempts );
			name = null;
			element = null;
			return rawText;
		}

		/// <summary>
		/// Reads the level/ascension region on the Attributes sub-tab. 16:9 region measured
		/// (2026-07-05), 16:10 Y position measured (2026-07-13, 1680x1050), both with
		/// <c>ui/CoordinatePickerForm.cs</c>. Expressed as position + size: the crop's x/width/height are
		/// shared across aspect ratios, so only the y position branches -- the window is letterboxed
		/// vertically, but the horizontal axis and the box's dimensions don't change.
		/// </summary>
		private int ScanLevel(CharacterNavigationTiming timing, ref bool ascended)
		{
            int attempt = 0;

			// Shared x/width/height; only the y position shifts by aspect ratio (see doc comment).
			Rectangle region = new Rectangle(
				x:      (int)( 0.7626 * Navigation.GetWidth() ),
				y:      (int)( (Navigation.IsNormal ? 0.1895 : 0.1695) * Navigation.GetHeight() ),   // 16:9 : 16:10
				width:  (int)( 0.1209 * Navigation.GetWidth() ),
				height: (int)( 0.0352 * Navigation.GetHeight() ));

			do
			{
				Bitmap bm = Navigation.CaptureRegion(region);

				bm = GenshinProcesor.ResizeImage(bm, bm.Width * 2, bm.Height * 2);
				Bitmap n = imagePreprocessor.ConvertToGrayscale(bm);
				imagePreprocessor.SetInvert(ref n);
				// Bug fix (2026-07-05): contrast was being applied to `bm` (the display-only copy
				// passed to progressReporter.SetCharacter_Level below), not `n` (what's actually fed
				// to Tesseract) -- the contrast boost never reached the OCR input at all, likely
				// contributing to level misreads.
				imagePreprocessor.SetContrast(30.0, ref n);

				string text = ocrService.AnalyzeText(n).Trim();
				Logger.Debug("Scanned character level as {0}", text);

				text = Regex.Replace(text, @"(?![0-9/]).", string.Empty);
				Logger.Debug("Filtered scanned text to {0}", text);
				if (text.Contains("/"))
				{
					var values = text.Split('/');
                    if (int.TryParse(values[0], out int level) && int.TryParse(values[1], out int maxLevel))
                    {
                        maxLevel = (int)Math.Round(maxLevel / 10.0, MidpointRounding.AwayFromZero) * 10;
                        ascended = 20 <= level && level < maxLevel;
                        progressReporter.SetCharacter_Level(bm, level, maxLevel);
                        n.Dispose();
                        bm.Dispose();
                        Logger.Debug("Parsed character level as {0}", level);
                        return level;
                    }
				}
				Logger.Debug("Failed to parse character level and ascension from {0} (text), retrying", text);

				attempt++;

                n.Dispose();
                bm.Dispose();
                Thread.Sleep(timing.Scale(100));
			} while (attempt < 20); // reduced from 50 per user (2026-07-05), matching ScanNameAndElement's cap

			return -1;
		}

		/// <summary>
		/// Controller-mode constellation scan. Per user (2026-07-05, live-tested), the interaction
		/// model here is entirely different from the mouse path's per-node click + color-sample: you
		/// press confirm (B) once to enter the constellation detail view (which opens on the first
		/// constellation already focused), then a single left-stick Down step advances focus to the
		/// next constellation -- the same fixed on-screen region always shows whichever constellation
		/// is currently focused, so there is no per-node capture-region math like the mouse path
		/// needed. Unlock state is read via OCR for the literal word "Activated" (a locked
		/// constellation instead prompts "Activate", per user) rather than the mouse path's
		/// white-background color sample. Exits via cancel (A) once done or once a locked
		/// constellation is found. Region measured (2026-07-05) with <c>ui/CoordinatePickerForm.cs</c>.
		/// </summary>
		private int ScanConstellations(
			GameNavigator navigator,
			Character character,
			CharacterNavigationTiming timing)
		{
			Rectangle activatedRegion = new RECT(
				Left:   (int)( 0.1574 * Navigation.GetWidth() ),
				Top:    (int)( 0.8323 * Navigation.GetHeight() ),
				Right:  (int)( 0.2241 * Navigation.GetWidth() ),
				Bottom: (int)( 0.8777 * Navigation.GetHeight() ));

			int constellation;

			navigator.TapConfirm(timing.Scale(300));
			Thread.Sleep(timing.Scale(600)); // set to 600 per user (2026-07-05)

			Bitmap constellationShot = null;
			for (constellation = 0; constellation < 6; constellation++)
			{
				if (constellation > 0)
				{
					// Per user (2026-07-05): settleMs already sleeps after the stick releases
					// (GameNavigator.MoveStep), so a separate Thread.Sleep on top of it was a
					// redundant double-wait -- folded into settleMs directly instead.
					navigator.MoveStep(GameNavigator.MenuDirection.Down,
						holdMs: timing.Scale(100),
						settleMs: timing.Scale(400));
				}

				LogCharacterScreenshot(character.NameGOOD, $"constellations/constellation_{constellation + 1}", activatedRegion);
				LogCharacterWindow(character.NameGOOD, $"constellations/constellation_{constellation + 1}");

				bool activated = ReadConstellationActivated(activatedRegion, timing);

				// Capture the constellation region to show in the UI: keep the last ACTIVATED node,
				// falling back to the first node examined so a C0 character still shows something.
				if (activated || constellationShot == null)
				{
					constellationShot?.Dispose();
					constellationShot = Navigation.CaptureRegion(activatedRegion);
				}

				if (!activated) break;
			}

			navigator.TapBack(timing.Scale(300));
			Thread.Sleep(timing.Scale(250)); // lowered from 400 per user (2026-07-05)

			progressReporter.SetCharacter_Constellation(constellationShot, constellation);
			constellationShot?.Dispose();
			return constellation;
		}

		/// <summary>
		/// Reads the currently-focused constellation node's unlock state, retrying up to 3 times
		/// (re-capturing a fresh screenshot each time, not just re-OCRing the same bitmap -- a miss is
		/// more likely the node-move animation not having settled yet than a deterministic misread) --
		/// see <see cref="ScanConstellations"/>'s doc comment history for why. Reads via
		/// OCR for the literal word "Activated" (a locked constellation instead prompts "Activate").
		/// Preprocessing is gamma+invert+grayscale (2026-07-05): a live screenshot showed "Activated"
		/// as bold orange/gold text on a blue-green gradient background, the same pattern that broke
		/// contrast-based preprocessing for the weapon card nameplate (see
		/// WeaponScraper/ScanMainCharacterName's doc comments); gamma correction is the proven fix for
		/// that exact text/background combination in this codebase. Shared by
		/// <see cref="ScanConstellations"/> and <see cref="ScanConstellationsGreedy"/>.
		/// </summary>
		private bool ReadConstellationActivated(
			Rectangle activatedRegion,
			CharacterNavigationTiming timing)
		{
			for (int readAttempt = 0; readAttempt < 3; readAttempt++)
			{
				if (readAttempt > 0)
					Thread.Sleep(timing.Scale(200));

				Bitmap bm = Navigation.CaptureRegion(activatedRegion);
				GenshinProcesor.SetGamma(0.2, 0.2, 0.2, ref bm);
				imagePreprocessor.SetInvert(ref bm);
				Bitmap n = imagePreprocessor.ConvertToGrayscale(bm);

				string text = ocrService.AnalyzeText(n, Tesseract.PageSegMode.SingleBlock).Trim();
				bool activated = text.ToLower().Contains("activated");

				n.Dispose();
				bm.Dispose();

				if (activated) return true;
			}
			return false;
		}

		/// <summary>
		/// Greedy variant of <see cref="ScanConstellations"/>: instead of walking C1 to
		/// C6 forward and stopping at the first lock, jumps straight to C6 and reads backward. Per
		/// user (2026-07-05): the constellation list is an unbounded/circular scroll (same as the
		/// Character screen's own sub-tab row), so a single Up move from the C1 focus that confirming
		/// (B) opens on wraps straight to C6 instead of needing 5 Down moves to walk there. Since
		/// unlocks are always sequential (C6 unlocked implies C1-C5 are too), if C6 reads Activated
		/// the whole scan is done in a single read instead of up to six. Per user, only used for
		/// 4-star characters for now (<see cref="IsFourStarCharacter"/>) since those are commonly
		/// fully-conned, making the C6-first check likely to pay off; 5-stars stay on the forward scan
		/// where a full-con check failing fast (locked at C1) is the more common case.
		/// NOT YET LIVE-VERIFIED: the circular-scroll-wraps-in-one-Up-move assumption for the
		/// constellation list specifically (confirmed only for the Character screen's own sub-tab row
		/// so far).
		/// </summary>
		private int ScanConstellationsGreedy(
			GameNavigator navigator,
			Character character,
			CharacterNavigationTiming timing)
		{
			Rectangle activatedRegion = new RECT(
				Left:   (int)( 0.1574 * Navigation.GetWidth() ),
				Top:    (int)( 0.8323 * Navigation.GetHeight() ),
				Right:  (int)( 0.2241 * Navigation.GetWidth() ),
				Bottom: (int)( 0.8777 * Navigation.GetHeight() ));

			navigator.TapConfirm(timing.Scale(300));
			Thread.Sleep(timing.Scale(600));

			navigator.MoveStep(GameNavigator.MenuDirection.Up,
				holdMs: timing.Scale(100),
				settleMs: timing.Scale(400));

			Bitmap constellationShot = null;
			int constellation = 0;
			for (int node = 5; node >= 0; node--)
			{
				LogCharacterScreenshot(character.NameGOOD, $"constellations/constellation_greedy_{node + 1}", activatedRegion);

				bool activated = ReadConstellationActivated(activatedRegion, timing);

				// Capture the constellation region for the UI: the activated node found, or the first
				// node examined as a fallback if none are activated (C0).
				if (activated || constellationShot == null)
				{
					constellationShot?.Dispose();
					constellationShot = Navigation.CaptureRegion(activatedRegion);
				}

				if (activated)
				{
					constellation = node + 1;
					break;
				}

				if (node > 0)
				{
					navigator.MoveStep(GameNavigator.MenuDirection.Up,
						holdMs: timing.Scale(100),
						settleMs: timing.Scale(400));
				}
			}

			navigator.TapBack(timing.Scale(300));
			Thread.Sleep(timing.Scale(250));

			progressReporter.SetCharacter_Constellation(constellationShot, constellation);
			constellationShot?.Dispose();
			return constellation;
		}

		/// <summary>
		/// Whether <paramref name="character"/>'s database entry has Rarity == 4. Missing Rarity
		/// (e.g. a characters.json predating the 2026-07-05 DatabaseManager change that added it, not
		/// yet refreshed via Update Database) is treated as "unknown" (false) rather than assumed, so
		/// callers fall back to the safe non-greedy scan instead of guessing.
		/// </summary>
		private bool IsFourStarCharacter(Character character)
		{
			string lookupKey = character.NameGOOD.Contains("Traveler") ? "traveler" : character.NameGOOD.ToLower();
			return GenshinProcesor.Characters.TryGetValue(lookupKey, out var data)
				&& data["Rarity"] != null
				&& data["Rarity"].ToObject<int>() == 4;
		}

		/// <summary>
		/// Controller-mode talent scan. Per user (2026-07-05, live-tested), the Talents sub-tab
		/// already displays every talent's level simultaneously as "Lv. XX" rows (plus other
		/// descriptive text sharing the same region) -- no per-icon click/capture loop is needed the
		/// way the mouse path requires. Parses every "Lv. XX" match out of the single captured region,
		/// in on-screen top-to-bottom order, and assigns the first three as auto/skill/burst.
		/// Per user (2026-07-05): the mouse path's Mona/Ayaka movement-talent row skip does not apply
		/// here -- removed after a live test got stuck retrying (found 3 "Lv. XX" rows, wanted 4)
		/// rather than just taking the 3 that were actually there. Retries (matching the mouse path's
		/// per-icon retry loop) until at least 3 "Lv. XX" rows are found. Region measured (2026-07-05)
		/// with <c>ui/CoordinatePickerForm.cs</c>.
		/// </summary>
		private Dictionary<string, int> ScanTalents(
			Character character,
			CharacterNavigationTiming timing)
		{
			var talents = new Dictionary<string, int>
			{
				{ "auto" , -1 },
				{ "skill", -1 },
				{ "burst", -1 }
			};

			Rectangle region = new RECT(
				Left:   (int)( 0.8290 * Navigation.GetWidth() ),
				Top:    (int)( 0.1248 * Navigation.GetHeight() ),
				Right:  (int)( 0.8743 * Navigation.GetWidth() ),
				Bottom: (int)( 0.5687 * Navigation.GetHeight() ));

			const int requiredRows = 3;
			int attempt = 0;

			while (attempt < 20) // reduced from 50 per user (2026-07-05), matching ScanNameAndElement/ScanLevel's cap
			{
				Bitmap bm = Navigation.CaptureRegion(region);
				Bitmap resized = GenshinProcesor.ResizeImage(bm, bm.Width * 2, bm.Height * 2);
				Bitmap n = imagePreprocessor.ConvertToGrayscale(resized);
				imagePreprocessor.SetContrast(60, ref n);
				imagePreprocessor.SetInvert(ref n);

				var lines = ocrService.AnalyzeText(n, Tesseract.PageSegMode.SingleBlock).Trim().Split('\n');

				var levels = new List<int>();
				foreach (var line in lines)
				{
					var match = Regex.Match(line, @"[Ll][Vv]\.?\s*(\d+)");
					if (match.Success && int.TryParse(match.Groups[1].Value, out int lvl) && lvl >= 1 && lvl <= 15)
						levels.Add(lvl);
				}

				if (levels.Count >= requiredRows)
				{
					talents["auto"] = levels[0];
					talents["skill"] = levels[1];
					talents["burst"] = levels[2];

					progressReporter.SetCharacter_Talent(bm, talents["auto"].ToString(), 0);
					progressReporter.SetCharacter_Talent(bm, talents["skill"].ToString(), 1);
					progressReporter.SetCharacter_Talent(bm, talents["burst"].ToString(), 2);
					LogCharacterScreenshot(character.NameGOOD, "talents", region);
					LogCharacterWindow(character.NameGOOD, "talents");

					n.Dispose();
					bm.Dispose();
					break;
				}

				Logger.Debug("Controller talent scan: found {0}/{1} \"Lv. XX\" rows, retrying", levels.Count, requiredRows);
				n.Dispose();
				bm.Dispose();
				attempt++;
				Thread.Sleep(timing.Scale(100));
			}

			// The loop only sets all three talents together (on a successful read) then breaks; if it
			// exhausted every attempt without that, they're still -1. Surface it (previously silent --
			// the character just came back with -1/-1/-1 talents) and force-save the talent region so
			// the failure is diagnosable even with LogScreenshots off.
			if (talents["auto"] < 1 || talents["skill"] < 1 || talents["burst"] < 1)
			{
				progressReporter.AddError($"Could not determine {character.NameGOOD}'s talents.");
				LogCharacterScreenshot(character.NameGOOD, "talents_unconfirmed", region, force: true);
			}

			return talents;
		}
	}
}
