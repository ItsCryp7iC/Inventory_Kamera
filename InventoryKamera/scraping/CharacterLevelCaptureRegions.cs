using System;
using System.Drawing;

namespace InventoryKamera
{
	/// <summary>
	/// Provides the Character Attributes level crop without including the neighboring EXP counter.
	/// Horizontal bounds are shared across supported aspect ratios; only the vertical position
	/// differs for the existing 16:9 and 16:10 layouts.
	/// </summary>
	internal static class CharacterLevelCaptureRegions
	{
		private const double Left = 0.7626;
		private const double Right = 0.8620;
		private const double NormalTop = 0.1895;
		private const double SixteenByTenTop = 0.1695;
		private const double Height = 0.0352;

		internal static Rectangle Level(Size captureSize, bool isNormalAspectRatio)
		{
			if (captureSize.Width <= 0) throw new ArgumentOutOfRangeException(nameof(captureSize));
			if (captureSize.Height <= 0) throw new ArgumentOutOfRangeException(nameof(captureSize));

			int left = (int)(Left * captureSize.Width);
			int right = (int)(Right * captureSize.Width);
			int top = (int)((isNormalAspectRatio ? NormalTop : SixteenByTenTop) * captureSize.Height);
			int height = (int)(Height * captureSize.Height);

			return new Rectangle(left, top, Math.Max(1, right - left), Math.Max(1, height));
		}
	}
}
