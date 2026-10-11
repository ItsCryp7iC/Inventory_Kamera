using System;
using System.Drawing;
using Tesseract;

namespace InventoryKamera
{
	/// <summary>
	/// Focused numeric OCR fallback for a material quantity crop. The caller owns the source bitmap;
	/// all resized and processed images created here are disposed before this method returns.
	/// </summary>
	internal static class MaterialQuantityNumericFallback
	{
		internal const string PreprocessingMode =
			"scale=2;filter-rgb=0-150;clear-top-quarter;grayscale;psm=SingleLine;digits-only";

		internal static MaterialQuantityFallbackObservation Read(
			Bitmap quantityBitmap,
			IOcrService ocrService,
			IImagePreprocessor imagePreprocessor)
		{
			if (quantityBitmap == null) throw new ArgumentNullException(nameof(quantityBitmap));
			if (ocrService == null) throw new ArgumentNullException(nameof(ocrService));
			if (imagePreprocessor == null) throw new ArgumentNullException(nameof(imagePreprocessor));

			using Bitmap resized = GenshinProcesor.ResizeImage(
				quantityBitmap,
				quantityBitmap.Width * 2,
				quantityBitmap.Height * 2);
			Bitmap filtered = (Bitmap)resized.Clone();
			Bitmap grayscale = null;
			try
			{
				var darkPixels = new IntRange(0, 150);
				imagePreprocessor.FilterColors(ref filtered, darkPixels, darkPixels, darkPixels);

				for (int x = 0; x < filtered.Width; x++)
					for (int y = 0; y < filtered.Height * 0.25; y++)
						filtered.SetPixel(x, y, Color.White);

				grayscale = imagePreprocessor.ConvertToGrayscale(filtered);
				(string rawText, float confidence) = ocrService.AnalyzeTextWithConfidence(
					grayscale,
					PageSegMode.SingleLine,
					numbersOnly: true);
				return MaterialQuantityFallbackObservation.Create(
					PreprocessingMode,
					rawText?.Trim(),
					confidence);
			}
			finally
			{
				grayscale?.Dispose();
				filtered.Dispose();
			}
		}
	}
}
