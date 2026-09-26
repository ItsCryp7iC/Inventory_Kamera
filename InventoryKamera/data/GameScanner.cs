using InventoryKamera.game;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace InventoryKamera
{
    public class GameScanner
	{

		private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

		[JsonProperty]
		public List<Character> Characters;

		[JsonProperty]
		public Inventory Inventory;

		private List<Artifact> equippedArtifacts;
		private List<Weapon> equippedWeapons;

		private readonly IOcrService ocrService;
		private readonly IPositionalOcrService positionalOcrService;
		private readonly IImagePreprocessor imagePreprocessor;
		private readonly IScanSettings scanSettings;
		private readonly IScanProgressReporter progressReporter;
		private readonly ScanSession scanSession;
		private GameDataSnapshot gameData;

		private WeaponScraper weaponScraper;
		private ArtifactScraper artifactScraper;
		private CharacterScraper characterScraper;
		private MaterialScraper materialScraper;

		private readonly int NumWorkers;

		public bool HasData
        {
			get { return Characters.Count > 0 || Inventory.Size > 0; }
        }

		/// <param name="progressReporter">
		/// Owned by the caller (<see cref="MainForm"/>), not constructed here: unlike
		/// <c>ocrService</c>/<c>imagePreprocessor</c>/<c>scanSettings</c>, a <see cref="ScanViewModel"/>
		/// needs to outlive any single <see cref="GameScanner"/> instance (MainForm recreates one
		/// per scan) so its subscribers don't have to re-subscribe every time.
		/// </param>
		internal GameScanner(IScanProgressReporter progressReporter, ScanSession scanSession)
			: this(progressReporter, scanSession, new GameDataSnapshotFactory().Load())
		{
		}

		internal GameScanner(
			IScanProgressReporter progressReporter,
			ScanSession scanSession,
			GameDataSnapshot gameData)
		{
			Characters = new List<Character>();
			Inventory = new Inventory();
			equippedArtifacts = new List<Artifact>();
			equippedWeapons = new List<Weapon>();
			this.scanSession = scanSession ?? throw new ArgumentNullException(nameof(scanSession));
			this.gameData = gameData ?? throw new ArgumentNullException(nameof(gameData));

			var tesseractOcr = new OcrService();
			ocrService = tesseractOcr;
			positionalOcrService = tesseractOcr;
			imagePreprocessor = new ImageProcessor();
			scanSettings = new ScanSettings();
			this.progressReporter = progressReporter;

			// Base worker count on available CPU (leaving headroom for the UI/navigation thread) so
			// small machines don't oversubscribe; the scanner-speed setting further caps it down for
			// the slower, lower-load profiles.
			int baseWorkers = Math.Max(1, Environment.ProcessorCount - 1);
			int userCap = scanSettings.ScannerDelay == 0 ? 3 : 2;
			NumWorkers = Math.Min(baseWorkers, userCap);

			Logger.Info("Kamera initialized");
		}

		internal GameDataSnapshot GameData => gameData;

		private void ConfigurePerScanCharacterNames()
		{
			var customNames = new Dictionary<string, string>();
			string travelerName = string.IsNullOrWhiteSpace(scanSettings.TravelerName)
				? CharacterScraper.ScanMainCharacterName(ocrService, imagePreprocessor, progressReporter)
				: scanSettings.TravelerName.ToLower();

			if (!string.IsNullOrWhiteSpace(travelerName))
			{
				AddCustomName(customNames, "traveler", travelerName);
				progressReporter.SetMainCharacterName(travelerName);
			}
			else
			{
				progressReporter.AddError("Could not parse Traveler's username");
			}

			AddCustomName(customNames, "wanderer", scanSettings.WandererName);
			AddCustomName(customNames, "manequin1", scanSettings.Manequin1Name);
			AddCustomName(customNames, "manequin2", scanSettings.Manequin2Name);
			gameData = gameData.WithCharacterCustomNames(customNames);
		}

		private void AddCustomName(Dictionary<string, string> customNames, string target, string name)
		{
			string targetKey = GameDataSnapshot.NormalizeKey(target);
			string nameKey = GameDataSnapshot.NormalizeKey(name);
			if (targetKey == nameKey) return;

			if (gameData.Characters.ContainsKey(nameKey))
			{
				Logger.Error("{0} already exists as a character in the game. " +
					"This may wind up confusing Kamera when connecting items for {1}.", nameKey, targetKey);
			}

			if (!gameData.Characters.ContainsKey(targetKey))
				throw new KeyNotFoundException($"Could not find '{targetKey}' entry in characters.json");

			customNames[targetKey] = nameKey;
			Logger.Info("Internally set {0} custom name to {1}", targetKey, nameKey);
		}

		private void InitializeScrapers()
		{
			weaponScraper = new WeaponScraper(
				ocrService, imagePreprocessor, scanSettings, progressReporter, scanSession, gameData);
			artifactScraper = new ArtifactScraper(
				ocrService, imagePreprocessor, scanSettings, progressReporter, scanSession, gameData);
			characterScraper = new CharacterScraper(
				ocrService, imagePreprocessor, scanSettings, progressReporter, scanSession, gameData);
			materialScraper = new MaterialScraper(
				ocrService, imagePreprocessor, scanSettings, progressReporter, scanSession, gameData);
		}

		public void ResetLogging()
		{
			try
			{
				Directory.Delete("./logging/weapons", true);
				Directory.Delete("./logging/artifacts", true);
				Directory.Delete("./logging/characters", true);
				Directory.Delete("./logging/materials", true);
			}
			catch { }

			Directory.CreateDirectory("./logging/weapons");
			Directory.CreateDirectory("./logging/artifacts");
			Directory.CreateDirectory("./logging/characters");
			Directory.CreateDirectory("./logging/materials");

			Logger.Info("Logging directory reset");
		}

		public void StopImageProcessorWorkers()
		{
			scanSession.AbortWorkersAndWait();
		}

		public void GatherData()
		{
			ResetLogging();

			// Initialize Image Processors
			scanSession.StartWorkers(NumWorkers, ImageProcessorWorkerAsync);
			Logger.Debug("Added {0} workers", NumWorkers);

			try
			{
			ocrService.Restart();


			ConfigurePerScanCharacterNames();
			InitializeScrapers();

			bool scanInventory = scanSettings.ScanWeapons || scanSettings.ScanArtifacts
				|| scanSettings.ScanCharDevItems || scanSettings.ScanMaterials;
			if ((scanInventory || scanSettings.ScanCharacters) && !scanSession.IsCancellationRequested)
			{
				// Phase 3 §6c: a SINGLE controller connection spans every controller-driven scan phase
				// -- the inventory group (Weapons/Artifacts/Character Development Items/Materials) AND
				// Characters. Previously the character phase opened its own second navigator after
				// the inventory one had been disposed; disconnecting and reconnecting the virtual
				// controller mid-scan let Genshin drop back to keyboard/mouse (or surface its
				// "controller disconnected" prompt) in the gap, making the inventory->character handoff
				// flaky (per user, 2026-07-07). Now there is one connect for the whole scan: between the
				// two phase groups we just back out to the unpaused free-roam state (MashBack) and let
				// the next phase re-open the pause menu, never disconnecting until the scan is fully done.
				// Only when this `using` block ends does GameNavigator.Dispose() back out of every menu
				// and switch Genshin back to keyboard/mouse.
				using (var navigator = GameInputFactory.CreateNavigator())
				{
					if (!navigator.IsAvailable)
					{
						progressReporter.AddError($"Controller scan unavailable: {navigator.FailureReason}");
					}
					else
					{
						var paimonDetector = new PaimonMenuDetector(positionalOcrService, imagePreprocessor);
						var inventoryScreenDetector = new InventoryScreenDetector(ocrService, imagePreprocessor);
						var characterScreenDetector = new CharacterScreenDetector(ocrService, imagePreprocessor);
						var paimonNavigator = new PaimonMenuNavigator(
							navigator,
							paimonDetector,
							new NavigationGameScreenCapture(),
							inventoryScreenDetector,
							characterScreenDetector,
							cancellationRequested: () => scanSession.IsCancellationRequested);

						if (scanInventory && !scanSession.IsCancellationRequested)
						{
							bool inventoryEntered = false;
							try
							{
								inventoryEntered = weaponScraper.EnterInventory(paimonNavigator);
							}
							catch (FormatException ex) { progressReporter.AddError(ex.Message); }
							catch (Exception ex)
							{
								progressReporter.AddError(ex.Message + "\n" + ex.StackTrace);
							}

							if (!inventoryEntered)
							{
								Logger.Warn("Skipping all controller-driven inventory phases because Inventory entry was not verified.");
							}
							else
							{
							// Per user (2026-07-05): once a phase has switched to and scanned a tab, the
							// next phase already knows where it left off -- no need to re-detect the
							// current tab via OCR (which had been flaking) just to compute the next
							// switch. Threaded through explicitly rather than re-derived.
							string currentTab = null;

							if (scanSettings.ScanWeapons && !scanSession.IsCancellationRequested)
							{
								Logger.Info("Scanning weapons...");
								try
								{
									currentTab = weaponScraper.ScanWeapons(navigator, knownCurrentTab: currentTab);
								}
								catch (FormatException ex) { progressReporter.AddError(ex.Message); }
								catch (Exception ex)
								{
									progressReporter.AddError(ex.Message + "\n" + ex.StackTrace);
								}
								Logger.Info("Done scanning weapons");
							}

							if (scanSettings.ScanArtifacts && !scanSession.IsCancellationRequested)
							{
								Logger.Info("Scanning artifacts...");
								try
								{
									currentTab = artifactScraper.ScanArtifacts(navigator, knownCurrentTab: currentTab);
								}
								catch (FormatException ex) { progressReporter.AddError(ex.Message); }
								catch (Exception ex)
								{
									progressReporter.AddError(ex.Message + "\n" + ex.StackTrace);
								}
								Logger.Info("Done scanning artifacts");
							}

							if (scanSettings.ScanCharDevItems && !scanSession.IsCancellationRequested)
							{
								Logger.Info("Scanning character development materials...");
								try
								{
									materialScraper.SetInventoryPage(InventoryPage.CharacterDevelopmentItems);
									currentTab = materialScraper.ScanMaterials(navigator, ref Inventory, knownCurrentTab: currentTab);
								}
								catch (FormatException ex) { progressReporter.AddError(ex.Message); }
								catch (Exception ex)
								{
									progressReporter.AddError(ex.Message + "\n" + ex.StackTrace);
								}
								Logger.Info("Done scanning character development materials");
							}

							if (scanSettings.ScanMaterials && !scanSession.IsCancellationRequested)
							{
								Logger.Info("Scanning materials...");
								try
								{
									materialScraper.SetInventoryPage(InventoryPage.Materials);
									currentTab = materialScraper.ScanMaterials(navigator, ref Inventory, knownCurrentTab: currentTab);
								}
								catch (FormatException ex) { progressReporter.AddError(ex.Message); }
								catch (Exception ex)
								{
									progressReporter.AddError(ex.Message + "\n" + ex.StackTrace);
								}
								Logger.Info("Done scanning materials");
							}
							}
						}

						// All inventory items (if any) are queued -- let the image-processor workers
						// drain and finish while the character phase runs below. Characters don't queue
						// to this channel, so it's safe to close it now.
						scanSession.CompleteWork();

						if (scanSettings.ScanCharacters && !scanSession.IsCancellationRequested)
						{
							if (scanInventory)
							{
								// We're still in controller mode sitting in the Inventory menu. Back all
								// the way out to the unpaused free-roam state before opening the Character
								// menu, and give the menu-close animation generous time to finish -- the
								// Character-menu entry re-opens the pause menu and visually detects its
								// starting selection, which still requires a clean free-roam baseline.
								// This replaces the old between-phase controller disconnect/reconnect.
								Logger.Info("Backing out of inventory to free-roam before character scan...");
								navigator.MashBack();
								Thread.Sleep(Math.Max(4000, InventoryScraper.ScaledControllerDelay(3000)));
							}

							Logger.Info("Scanning characters...");
							bool characterEntrySucceeded = false;
							try
							{
								characterEntrySucceeded = characterScraper.ScanCharacters(
									navigator, paimonNavigator, ref Characters);
							}
							catch (Exception ex)
							{
								progressReporter.AddError(ex.Message + "\n" + ex.StackTrace);
							}
							if (characterEntrySucceeded)
								Logger.Info("Done scanning characters");
							else
								Logger.Warn("Character scanning did not continue because Character entry was not verified.");
						}
					}
				}
			}

			if (scanSettings.ScanWeapons || scanSettings.ScanArtifacts || scanSettings.ScanCharDevItems ||
				scanSettings.ScanMaterials || scanSettings.ScanCharacters)
			{
				// Final safety net: guarantee the game is back in the free-roam/main-menu state once
				// every controller-driven scan phase above is done, regardless of whatever
				// GameNavigator.Dispose()'s MashBack (A-press) safety net left behind -- mirrors the
				// old mouse-mode MainMenuScreen()'s double-Escape finisher that every scan used to end
				// with before the controller migration (Phase 3 §6c) dropped it.
				Navigation.sim.Keyboard.KeyPress(Navigation.escapeKey);
				Navigation.SystemWait(Navigation.Speed.UI);
				Navigation.sim.Keyboard.KeyPress(Navigation.escapeKey);
				Navigation.SystemWait(Navigation.Speed.UI);
			}
			}
			finally
			{
				// Idempotent: inventory scans close the queue before Character scanning so workers can
				// drain concurrently; every other normal, cancellation, and exception path closes here.
				scanSession.CompleteWork();
			}

			// Wait for Image Processors to finish
			AwaitProcessors();

			// Every weapon/artifact is now catalogued (from raw best-guess names). Show the batched
			// low-confidence name corrections the scan deferred, and let each patch its record --
			// after the workers have drained (so the callbacks' inventory mutations run single-threaded)
			// and before the assignment passes below (so a correction that rescues an equipped item is
			// assigned to its character).
			progressReporter.FlushDeferredCorrections(scanSession.CancellationToken);

			if (scanSettings.ScanCharacters)
			{
				// Assign Artifacts to Characters
				if (scanSettings.ScanArtifacts)
					AssignArtifacts();
				if (scanSettings.ScanWeapons)
					AssignWeapons();
			}

			// Character Development Items and Materials are now scanned via controller above,
			// in the same GameNavigator session as Weapons/Artifacts (Phase 3 §6c, 2026-07-05).
		}

		private void AwaitProcessors()
		{
			scanSession.AwaitWorkers();
		}

		public async Task ImageProcessorWorkerAsync(CancellationToken abortToken)
		{
			Logger.Debug("Worker task starting");
			try
			{
				await foreach (var imageCollection in scanSession.WorkReader.ReadAllAsync(abortToken))
				{
					try
					{
						await ProcessImageCollectionAsync(imageCollection);
					}
					catch (Exception ex)
					{
						// A single bad item shouldn't take the whole worker down; log and keep going.
						Logger.Error(ex, "Image processor worker failed on a queued {0} item", imageCollection.Type);
					}
				}
			}
			catch (OperationCanceledException)
			{
				// Abrupt stop requested (StopImageProcessorWorkers); drop whatever's still queued.
				Logger.Debug("Worker task cancelled");
			}
			Logger.Debug("Worker task exit");
		}

		private async Task ProcessImageCollectionAsync(OCRImageCollection imageCollection)
		{
					switch (imageCollection.Type)
					{
						case "weapon":
							if (weaponScraper.IsEnhancementMaterial(imageCollection.Bitmaps.First()))
							{
								Logger.Debug("Enhancement material found for weapon #{0}", imageCollection.Id);
								weaponScraper.StopScanning = true;
								break;
							}

							progressReporter.SetGearPictureBox(imageCollection.Bitmaps.Last());

							// Scan as weapon
							var (weapon, pendingWeaponName) = await weaponScraper.CatalogueFromBitmapsAsync(imageCollection.Bitmaps, imageCollection.Id);
							progressReporter.SetGear(imageCollection.Bitmaps.Last(), weapon);

							string weaponPath = $"./logging/weapons/weapon{weapon.Id}/";

							if (scanSettings.LogScreenshots) Directory.CreateDirectory(weaponPath);

							if (weapon.IsValid())
							{
								Logger.Info("Weapon scan #{0}: scanned OK -- {1}", weapon.Id, weapon.ToString());
								progressReporter.IncrementWeaponCount();
								Inventory.Add(weapon);
								if (!string.IsNullOrWhiteSpace(weapon.EquippedCharacter))
									equippedWeapons.Add(weapon);
							}
							else
							{
								progressReporter.AddError($"Unable to validate information for weapon ID#{weapon.Id}");
								string error = "";
								if (!weapon.HasValidWeaponName()) error += "Invalid weapon name\n";
								if (!weapon.HasValidRarity()) error += "Invalid weapon rarity\n";
								if (!weapon.HasValidLevel()) error += "Invalid weapon level\n";
								if (!weapon.HasValidRefinementLevel()) error += "Invalid refinement level\n";
								if (!weapon.HasValidEquippedCharacter()) error += "Inavlid equipped character\n";
								progressReporter.AddError(error + weapon.ToString());
								Logger.Warn("Weapon scan #{0}: FAILED validation -- {1}scanned as: {2}",
									weapon.Id, error.Replace("\n", "; "), weapon.ToString());
								Directory.CreateDirectory(weaponPath);
								using (var writer = File.CreateText(weaponPath + "log.txt"))
								{
									writer.WriteLine($"Version: {AppInfo.Version}");
									writer.WriteLine($"Resolution: {Navigation.GetWidth()}x{Navigation.GetHeight()}");
									writer.WriteLine($"Error log:\n\t{error.Replace("\n", "\n\t")}");
								}
							}

                            if (!weapon.IsValid() || scanSettings.LogScreenshots)
                            {
                                Directory.CreateDirectory(weaponPath + "name");
                                imageCollection.Bitmaps[0].Save(weaponPath + "name/name.png");
                                Directory.CreateDirectory(weaponPath + "rarity");
                                imageCollection.Bitmaps[0].Save(weaponPath + "rarity/rarity.png");
                                Directory.CreateDirectory(weaponPath + "level");
                                imageCollection.Bitmaps[1].Save(weaponPath + "level/level.png");
                                Directory.CreateDirectory(weaponPath + "refinement");
                                imageCollection.Bitmaps[2].Save(weaponPath + "refinement/refinement.png");
                                Directory.CreateDirectory(weaponPath + "locked");
                                imageCollection.Bitmaps[3].Save(weaponPath + "locked/locked.png");
                                Directory.CreateDirectory(weaponPath + "equipped");
                                imageCollection.Bitmaps[4].Save(weaponPath + "equipped/equipped.png");

                                imageCollection.Bitmaps.Last().Save(weaponPath + "card.png");
								Task.Run(() => LogObject(weapon, weaponPath + "weapon.json"));
                            }

                            // A low-confidence weapon name isn't corrected inline anymore -- it's queued
                            // and shown after the whole scan finishes (see FlushDeferredCorrections). The
                            // record above was built and catalogued from the raw best-guess; the callback
                            // patches its name once the user answers, and rescues it into the inventory if
                            // the correction turns a previously-invalid name valid. wasAdded mirrors the
                            // add decision made just above so the rescue path never double-adds.
                            if (pendingWeaponName != null)
                            {
                                bool wasAdded = weapon.IsValid();
                                progressReporter.EnqueueCorrection(
                                    imageCollection.Bitmaps[0], pendingWeaponName.RecognizedText,
                                    pendingWeaponName.ConfidencePercent, pendingWeaponName.FieldLabel,
                                    corrected =>
                                    {
                                        string resolved = pendingWeaponName.Resolve(corrected);
                                        if (resolved == null) return;
                                        weapon.UpdateName(resolved);
                                        Logger.Info("Weapon scan #{0}: name corrected post-scan to \"{1}\".", weapon.Id, weapon.Name);
                                        if (!wasAdded && weapon.IsValid())
                                        {
                                            progressReporter.IncrementWeaponCount();
                                            Inventory.Add(weapon);
                                            if (!string.IsNullOrWhiteSpace(weapon.EquippedCharacter))
                                                equippedWeapons.Add(weapon);
                                        }
                                    });
                            }

                            // Dispose of everything
                            imageCollection.Bitmaps.ForEach(b => b.Dispose());
							break;

						case "artifact":
							if (artifactScraper.IsEnhancementMaterial(imageCollection.Bitmaps.Last()))
							{
								Logger.Debug("Enhancement material found for artifact #{0}", imageCollection.Id);
								artifactScraper.StopScanning = true;
								break;
							}

							progressReporter.SetGearPictureBox(imageCollection.Bitmaps.Last());
							// Scan as artifact
							var (artifact, pendingArtifactSet) = await artifactScraper.CatalogueFromBitmapsAsync(imageCollection.Bitmaps, imageCollection.Id);
							progressReporter.SetGear(imageCollection.Bitmaps.Last(), artifact);

							string artifactPath = $"./logging/artifacts/artifact{artifact.Id}/";

                            if (scanSettings.LogScreenshots) Directory.CreateDirectory(artifactPath);

							if (artifact.IsValid())
							{
								Logger.Info("Artifact scan #{0}: scanned OK -- {1}", artifact.Id, artifact.ToString());
								progressReporter.IncrementArtifactCount();
								Inventory.Add(artifact);
								if (!string.IsNullOrWhiteSpace(artifact.EquippedCharacter))
									equippedArtifacts.Add(artifact);
							}
							else
							{
								progressReporter.AddError($"Unable to validate information for artifact ID#{artifact.Id}");
								string error = "";
								if (!artifact.HasValidSlot()) error += "Invalid artifact gear slot\n";
								if (!artifact.HasValidSetName()) error += "Invalid artifact set name\n";
								if (!artifact.HasValidRarity()) error += "Invalid artifact rarity\n";
								if (!artifact.HasValidLevel()) error += "Invalid artifact level\n";
								if (!artifact.HasValidMainStat()) error += "Invalid artifact main stat\n";
								if (!artifact.HasValidSubStats()) error += "Invalid artifact sub stats\n";
								if (!artifact.HasValidEquippedCharacter()) error += "Invalid equipped character\n";
								progressReporter.AddError(error + artifact.ToString());
								Logger.Warn("Artifact scan #{0}: FAILED validation -- {1}scanned as: {2}",
									artifact.Id, error.Replace("\n", "; "), artifact.ToString());
								Directory.CreateDirectory(artifactPath);
								using (var writer = File.CreateText(artifactPath + "log.txt"))
								{
									writer.WriteLine($"Version: {AppInfo.Version}");
									writer.WriteLine($"Resolution: {Navigation.GetWidth()}x{Navigation.GetHeight()}");
									writer.WriteLine($"Error Log:\n\t{error.Replace("\n", "\n\t")}");
								}
							}

                            if (!artifact.IsValid() || scanSettings.LogScreenshots)
                            {
                                Directory.CreateDirectory(artifactPath + "name");
                                imageCollection.Bitmaps[0].Save(artifactPath + "name/name.png");
                                Directory.CreateDirectory(artifactPath + "slot");
								imageCollection.Bitmaps[1].Save(artifactPath + "slot/slot.png");
                                Directory.CreateDirectory(artifactPath + "mainstat");
                                imageCollection.Bitmaps[2].Save(artifactPath + "mainstat/mainstat.png");
								Directory.CreateDirectory(artifactPath + "level");
								imageCollection.Bitmaps[3].Save(artifactPath + "level/level.png");
								Directory.CreateDirectory(artifactPath + "substats");
								imageCollection.Bitmaps[4].Save(artifactPath + "substats/substats.png");
								Directory.CreateDirectory(artifactPath + "equipped");
								imageCollection.Bitmaps[5].Save(artifactPath + "equipped/equipped.png");
								Directory.CreateDirectory(artifactPath + "locked");
								imageCollection.Bitmaps[6].Save(artifactPath + "locked/locked.png");
                                Directory.CreateDirectory(artifactPath + "sanctify");
                                imageCollection.Bitmaps[7].Save(artifactPath + "sanctify/sanctify.png");


                                imageCollection.Bitmaps.Last().Save(artifactPath + "card.png");

								Task.Run(()=>LogObject(artifact, artifactPath + "artifact.json"));
							}

							// A low-confidence artifact set name is queued for correction after the whole
							// scan finishes (see FlushDeferredCorrections) rather than corrected inline.
							// The record above was catalogued from the raw best-guess; the callback patches
							// its set name once the user answers, and rescues it into the inventory when the
							// correction turns a previously-invalid (unmatched) set valid. wasAdded mirrors
							// the add decision just above so the rescue path never double-adds.
							if (pendingArtifactSet != null)
							{
								bool wasAdded = artifact.IsValid();
								progressReporter.EnqueueCorrection(
									imageCollection.Bitmaps[0], pendingArtifactSet.RecognizedText,
									pendingArtifactSet.ConfidencePercent, pendingArtifactSet.FieldLabel,
									corrected =>
									{
										string resolved = pendingArtifactSet.Resolve(corrected);
										if (resolved == null) return;
										artifact.UpdateSetName(resolved);
										Logger.Info("Artifact scan #{0}: set name corrected post-scan to \"{1}\".", artifact.Id, artifact.SetName);
										if (!wasAdded && artifact.IsValid())
										{
											progressReporter.IncrementArtifactCount();
											Inventory.Add(artifact);
											if (!string.IsNullOrWhiteSpace(artifact.EquippedCharacter))
												equippedArtifacts.Add(artifact);
										}
									});
							}

							// Dispose of everything
							imageCollection.Bitmaps.ForEach(b => b.Dispose());
							break;

						default:
							progressReporter.AddError("Unknown Image type for Image Processor");
							break;
					}
		}

        private static void LogObject(object obj, string path)
        {
            using (var file = new StreamWriter(path))
            {
                var serializer = new JsonSerializer
                {
                    Formatting = Formatting.Indented
                };
                serializer.Serialize(file, obj);
            }
        }

        public void AssignArtifacts()
		{
			foreach (Artifact artifact in equippedArtifacts)
			{
				foreach (Character character in Characters)
				{
					if (artifact.EquippedCharacter == character.NameGOOD)
					{
						character.AssignArtifact(artifact); // Do we even need to do this?
						Logger.Debug("Assigned {0} to {1}", artifact.GearSlot, character.NameGOOD);
						break;
					}
				}
			}
		}

		public void AssignWeapons()
		{
			foreach (Character character in Characters)
			{
				foreach (Weapon weapon in equippedWeapons)
				{
					if (weapon.EquippedCharacter == character.NameGOOD)
					{
						character.AssignWeapon(weapon);
						Logger.Debug("Assigned {0} to {1}", weapon.Name, character.NameGOOD);
						break;
					}
				}
				if (character.Weapon is null)
				{
					Inventory.Add(new Weapon(character.WeaponType, character.NameGOOD, gameData));
					Logger.Info("Default weapon assigned to {0}", character.NameGOOD);
				}
			}
		}
	}
}
