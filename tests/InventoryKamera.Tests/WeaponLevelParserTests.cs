using Xunit;

namespace InventoryKamera.Tests
{
	public class WeaponLevelParserTests
	{
		[Theory]
		[InlineData("1/20", 1, 20, false)]
		[InlineData("20/20", 20, 20, false)]
		[InlineData("20 / 40", 20, 40, true)]
		[InlineData("Lv. 20/40", 20, 40, true)]
		[InlineData("40/50", 40, 50, true)]
		[InlineData("50/60", 50, 60, true)]
		[InlineData("60/70", 60, 70, true)]
		[InlineData("70/80", 70, 80, true)]
		[InlineData("80/90", 80, 90, true)]
		[InlineData("90/90", 90, 90, false)]
		public void Parse_ValidPair_IsAccepted(
			string rawText,
			int expectedLevel,
			int expectedMaximum,
			bool expectedAscended)
		{
			WeaponLevelParseResult result = WeaponLevelParser.Parse(rawText);

			Assert.True(result.Success);
			Assert.Equal(expectedLevel, result.Level);
			Assert.Equal(expectedMaximum, result.MaximumLevel);
			Assert.Equal(expectedAscended, result.Ascended);
		}

		[Theory]
		[InlineData("0/20", "level_below_one")]
		[InlineData("90/80", "level_exceeds_maximum")]
		[InlineData("20/30", "unsupported_maximum")]
		[InlineData("90/100", "unsupported_maximum")]
		[InlineData("20/39", "unsupported_maximum")]
		[InlineData("20/", "malformed_pair")]
		[InlineData("2040", "malformed_pair")]
		[InlineData("20//40", "malformed_pair")]
		[InlineData("Level 20/40", "malformed_pair")]
		[InlineData("20/40 garbage", "malformed_pair")]
		[InlineData("-1/20", "malformed_pair")]
		public void Parse_InvalidOrPartiallySalvageableText_IsRejected(
			string rawText,
			string expectedReason)
		{
			WeaponLevelParseResult result = WeaponLevelParser.Parse(rawText);

			Assert.False(result.Success);
			Assert.Equal(expectedReason, result.ReasonCode);
		}
	}
}
