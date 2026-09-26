using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace InventoryKamera
{
    public static class GenshinProcesor
	{
		private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
		private static GameDataSnapshot compatibilitySnapshot = CreateCompatibilityDefaults();

		static GenshinProcesor()
        {
			Logger.Info("Scraper initialized");
        }

		/// <summary>
		/// A narrow compatibility view for models constructed without explicit lookup data. Normal UI
		/// and scanner paths receive a <see cref="GameDataSnapshot"/> directly. The entire compatibility
		/// view is replaced as one reference, never dictionary-by-dictionary.
		/// </summary>
		internal static GameDataSnapshot CompatibilitySnapshot =>
			Volatile.Read(ref compatibilitySnapshot);

		internal static void InstallCompatibilitySnapshot(GameDataSnapshot snapshot)
		{
			if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
			Interlocked.Exchange(ref compatibilitySnapshot, snapshot);
		}

		private static GameDataSnapshot CreateCompatibilityDefaults()
		{
			var stats = new Dictionary<string, string>
			{
				["hp"] = "hp",
				["hp%"] = "hp_",
				["atk"] = "atk",
				["atk%"] = "atk_",
				["def"] = "def",
				["def%"] = "def_",
				["energyrecharge"] = "enerRech_",
				["elementalmastery"] = "eleMas",
				["healingbonus"] = "heal_",
				["critrate"] = "critRate_",
				["critdmg"] = "critDMG_",
				["physicaldmgbonus"] = "physical_dmg_",
			};
			string[] elements = { "pyro", "hydro", "dendro", "electro", "anemo", "cryo", "geo" };
			foreach (string element in elements)
				stats[$"{element}dmgbonus"] = $"{element}_dmg_";

			return new GameDataSnapshot(
				new Dictionary<string, JObject>(),
				new Dictionary<string, JObject>(),
				new Dictionary<string, string>(),
				new Dictionary<string, string>(),
				new Dictionary<string, string>(),
				stats,
				elements.ToDictionary(
					element => element,
					element => char.ToUpper(element[0]) + element.Substring(1)),
				new[] { "flower", "plume", "sands", "goblet", "circlet" },
				new[]
				{
					"enhancementore",
					"fineenhancementore",
					"mysticenhancementore",
					"sanctifyingunction",
					"sanctifyingessence",
				});
		}

		internal static JObject BuildManequinEntry(string key)
			=> GameDataSnapshotFactory.BuildManequinEntry(key);

		internal static void UpdateCharacterName(string target, string name)
        {
			target = target.ConvertToGood().ToLower();
			name = name.ConvertToGood().ToLower();

			if (target == name) return;

			GameDataSnapshot snapshot = CompatibilitySnapshot;
			if (snapshot.Characters.ContainsKey(name))
			{
				Logger.Error("{0} already exists as a character in the game. " +
					"This may wind up confusing Kamera when connecting items for {1}.", name, target);
			}

            if (snapshot.Characters.ContainsKey(target))
			{
				var customNames = new Dictionary<string, string> { [target] = name };
				InstallCompatibilitySnapshot(snapshot.WithCharacterCustomNames(customNames));
				Logger.Info("Internally set {0} custom name to {1}", target, name);
			}
			else throw new KeyNotFoundException($"Could not find '{target}' entry in characters.json");
		}

		internal static void AssignTravelerName(string name, IOcrService ocrService, IImagePreprocessor imagePreprocessor, IScanProgressReporter progressReporter)
		{
			name = string.IsNullOrWhiteSpace(name) ? CharacterScraper.ScanMainCharacterName(ocrService, imagePreprocessor, progressReporter) : name.ToLower();
			if (!string.IsNullOrWhiteSpace(name))
			{
				UpdateCharacterName("traveler", name);
				progressReporter.SetMainCharacterName(name);
			}
			else
			{
				progressReporter.AddError("Could not parse Traveler's username");
			}
		}

		#region Check valid parameters

		// Legacy forwarding wrappers use the atomically replaced compatibility snapshot. New scanner
		// consumers receive their scan's GameDataSnapshot explicitly.

		internal static bool IsValidSetName(string setName) => LookupService.IsValidSetName(setName, CompatibilitySnapshot);

		internal static bool IsValidMaterial(string name) => LookupService.IsValidMaterial(name, CompatibilitySnapshot);

		internal static bool IsValidStat(string stat) => LookupService.IsValidStat(stat, CompatibilitySnapshot);

		internal static bool IsValidSlot(string gearSlot) => LookupService.IsValidSlot(gearSlot, CompatibilitySnapshot);

		internal static bool IsValidCharacter(string character) => LookupService.IsValidCharacter(character, CompatibilitySnapshot);

		internal static bool IsValidElement(string element) => LookupService.IsValidElement(element, CompatibilitySnapshot);

		internal static bool IsEnhancementMaterial(string material) => LookupService.IsEnhancementMaterial(material, CompatibilitySnapshot);

		internal static bool IsValidWeapon(string weapon) => LookupService.IsValidWeapon(weapon, CompatibilitySnapshot);

		#endregion Check valid parameters

		#region Element Searching

		// Legacy forwarding wrappers use the atomically replaced compatibility snapshot. New scanner
		// consumers receive their scan's GameDataSnapshot explicitly.

		internal static string FindClosestGearSlot(string input) => TextNormalizer.FindClosestGearSlot(input, CompatibilitySnapshot);

		internal static string FindClosestStat(string stat, int minConfidence = 90) => TextNormalizer.FindClosestStat(stat, CompatibilitySnapshot, minConfidence);

		internal static string FindElementByName(string name, int minConfidence = 90) => TextNormalizer.FindElementByName(name, CompatibilitySnapshot, minConfidence);

		internal static string FindClosestWeapon(string name, int maxEdits = 90) => TextNormalizer.FindClosestWeapon(name, CompatibilitySnapshot, maxEdits);

		internal static string FindClosestSetName(string name, int minConfidence = 90) => TextNormalizer.FindClosestSetName(name, CompatibilitySnapshot, minConfidence);

		internal static string FindClosestArtifactSetFromArtifactName(string name, int minConfidence = 90) =>
			TextNormalizer.FindClosestArtifactSetFromArtifactName(name, CompatibilitySnapshot, minConfidence);

		internal static string FindClosestCharacterName(string name, int minConfidence = 90) =>
			TextNormalizer.FindClosestCharacterName(name, CompatibilitySnapshot, minConfidence);

		internal static string FindClosestDevelopmentName(string name, int minConfidence = 90) =>
			TextNormalizer.FindClosestDevelopmentName(name, CompatibilitySnapshot, minConfidence);

		internal static string FindClosestMaterialName(string name, int minConfidence = 90) =>
			TextNormalizer.FindClosestMaterialName(name, CompatibilitySnapshot, minConfidence);

        #endregion Element Searching


        #region Image Operations

        internal static Bitmap ResizeImage(System.Drawing.Image image, int width, int height)
		{
			var destRect = new Rectangle(0, 0, width, height);
			var destImage = new Bitmap(width, height);

			destImage.SetResolution(image.HorizontalResolution, image.VerticalResolution);

			using (var graphics = Graphics.FromImage(destImage))
			{
				graphics.CompositingMode = CompositingMode.SourceCopy;
				graphics.CompositingQuality = CompositingQuality.HighQuality;
				graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
				graphics.SmoothingMode = SmoothingMode.HighQuality;
				graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

				using (var wrapMode = new ImageAttributes())
				{
					wrapMode.SetWrapMode(WrapMode.TileFlipXY);
					graphics.DrawImage(image, destRect, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, wrapMode);
				}
			}

			return destImage;
		}

		internal static Bitmap ResizeImage(Bitmap image, Size tSize)
		{
			int targetWidth = (int)Math.Round((double)image.Width * (tSize.Height / tSize.Width));
			int targetHeight = (int)Math.Round((double)image.Height * (tSize.Height / tSize.Width));
			using (var reSized = new Bitmap(targetWidth, targetHeight))
			using (var g = Graphics.FromImage(reSized))
			{
				g.DrawImage(image, 0,0, targetWidth, targetHeight);
				return (Bitmap)reSized.Clone();
			}
		}

		internal static Bitmap ScaleImage(System.Drawing.Image image, double factor)
		{
			return ResizeImage(image, (int)( image.Width * factor ), (int)( image.Height * factor ));
		}

		internal static bool CompareColors(Color a, Color b)
		{
			int[] diff = new int[3];
			diff[0] = Math.Abs(a.R - b.R);
			diff[1] = Math.Abs(a.G - b.G);
			diff[2] = Math.Abs(a.B - b.B);

			return diff[0] < 10 && diff[1] < 10 && diff[2] < 10;
		}

		internal static Color ClosestColor(List<Color> colors, Color c2)
		{
			var diff = colors.Select(x => new { Value = x, Diff = GetColorDifference(x, c2) }).ToList();

			foreach (var c in colors)
            {
                if (CompareColors(c, c2)) return c;
            }

            return diff.Find(x=> x.Diff == diff.Min(y=>y.Diff)).Value;
		}

        private static int GetColorDifference(Color c, Color c2)
        {
			int r = c.R - c2.R, g = c.G - c2.G, b = c.B - c2.B;
			return r*r + g*g + b*b;
		}

        internal static void SetGamma(double red, double green, double blue, ref Bitmap bitmap)
		{
			Bitmap temp = bitmap;
			Bitmap bmap = (Bitmap)temp.Clone();
			Color c;
			byte[] redGamma = CreateGammaArray(red);
			byte[] greenGamma = CreateGammaArray(green);
			byte[] blueGamma = CreateGammaArray(blue);
			for (int i = 0; i < bmap.Width; i++)
			{
				for (int j = 0; j < bmap.Height; j++)
				{
					c = bmap.GetPixel(i, j);
					bmap.SetPixel(i, j, Color.FromArgb(redGamma[c.R],
					   greenGamma[c.G], blueGamma[c.B]));
				}
			}
			bitmap = (Bitmap)bmap.Clone();
		}

		private static byte[] CreateGammaArray(double color)
		{
			byte[] gammaArray = new byte[256];
			for (int i = 0; i < 256; ++i)
			{
				gammaArray[i] = (byte)Math.Min(255,
		(int)( ( 255.0 * Math.Pow(i / 255.0, 1.0 / color) ) + 0.5 ));
			}
			return gammaArray;
		}

		internal static void SetColor(string colorFilterType, ref Bitmap bitmap)
		{
			Bitmap temp = bitmap;
			Bitmap bmap = (Bitmap)temp.Clone();
			Color c;
			for (int i = 0; i < bmap.Width; i++)
			{
				for (int j = 0; j < bmap.Height; j++)
				{
					c = bmap.GetPixel(i, j);
					int nPixelR = 0;
					int nPixelG = 0;
					int nPixelB = 0;
					if (colorFilterType == "red")
					{
						nPixelR = c.R;
						nPixelG = c.G - 255;
						nPixelB = c.B - 255;
					}
					else if (colorFilterType == "green")
					{
						nPixelR = c.R - 255;
						nPixelG = c.G;
						nPixelB = c.B - 255;
					}
					else if (colorFilterType == "blue")
					{
						nPixelR = c.R - 255;
						nPixelG = c.G - 255;
						nPixelB = c.B;
					}
					nPixelR = Math.Max(nPixelR, 0);
					nPixelR = Math.Min(255, nPixelR);

					nPixelG = Math.Max(nPixelG, 0);
					nPixelG = Math.Min(255, nPixelG);

					nPixelB = Math.Max(nPixelB, 0);
					nPixelB = Math.Min(255, nPixelB);

					bmap.SetPixel(i, j, Color.FromArgb((byte)nPixelR,
					  (byte)nPixelG, (byte)nPixelB));
				}
			}
			bitmap = (Bitmap)bmap.Clone();
		}

		internal static void SetBrightness(int brightness, ref Bitmap bitmap)
		{
			if (brightness < -255) brightness = -255;
			if (brightness > 255) brightness = 255;

			Bitmap temp = bitmap;
			Bitmap bmap = (Bitmap)temp.Clone();
			Color c;
			for (int i = 0; i < bmap.Width; i++)
			{
				for (int j = 0; j < bmap.Height; j++)
				{
					c = bmap.GetPixel(i, j);
					int cR = c.R + brightness;
					int cG = c.G + brightness;
					int cB = c.B + brightness;

					if (cR < 0) cR = 1;
					if (cR > 255) cR = 255;

					if (cG < 0) cG = 1;
					if (cG > 255) cG = 255;

					if (cB < 0) cB = 1;
					if (cB > 255) cB = 255;

					bmap.SetPixel(i, j, Color.FromArgb((byte)cR, (byte)cG, (byte)cB));
				}
			}
			bitmap = (Bitmap)bmap.Clone();
		}

		internal static Bitmap CopyBitmap(Bitmap source, Rectangle region)
		{
			ClipToSource(source, ref region);
            return source.Clone(region, source.PixelFormat);

			void ClipToSource(Bitmap s, ref Rectangle r)
			{
				if (r.X + r.Width > source.Width) { r.Width = s.Width - r.X; }
				if (r.Y + r.Height > source.Height) { r.Height = s.Height - r.Y; }
			}
        }

		#endregion Image Operations

		internal static string ConvertToGood(this string text)
		{
            text = text.ToLower();
            var pascal = CultureInfo.GetCultureInfo("en-US").TextInfo.ToTitleCase(text);
            return Regex.Replace(pascal, @"[\W]", string.Empty);
        }

        internal static bool CharacterMatchesElement(string name, string element)
        {
            return !string.IsNullOrWhiteSpace(name.ToLower()) && GetCharactersElements(name.ToLower()).Contains(element.ToLower());
        }

        internal static List<string> GetCharactersElements(string name)
		{
            if (string.IsNullOrWhiteSpace(name.ToLower()))
            {
                return new List<string>();
            }
            else
            {
                if (CompatibilitySnapshot.Characters.TryGetValue(name.ToLower(), out var data))
                {
                    return data["Element"].ToObject<List<string>>();
                }
                else
                {
                    return null;
                }
            }
        }
    }
}
