using Basil.Protocol.Bancho.Models.Scores;

namespace Basil.Protocol.Bancho.Tests.Models.Scores;

/// <summary>
///     Verifies the colon-delimited wire text an osu! client posts to <c>osu-submit-modular</c>: eighteen or
///     more fields, numeric fields parsed as i32, the <c>True</c>/non-<c>True</c> flags compared exactly,
///     the timestamp read as <c>yyMMddHHmmss</c> in UTC, and the client version field's space count (with
///     the value four masked off) as the client flags.
/// </summary>
public class ScoreSubmissionTests
{
	/// <summary>
	///     An eighteen-field submission: "3 4 " as the client version field contains two spaces, so two
	///     unmasked client-flag bits.
	/// </summary>
	private const string FullLine =
		"md5:lyx:checksum:5:4:3:2:1:0:123456:999:True:XH:16:True:0:260419081102:3 4 ";

	// The wire text of the line above minus its trailing client version field (17 fields) / two fields (16 fields).
	private const string LineWithoutLastField =
		"md5:lyx:checksum:5:4:3:2:1:0:123456:999:True:XH:16:True:0:260419081102";

	private const string LineWithoutLastTwoFields =
		"md5:lyx:checksum:5:4:3:2:1:0:123456:999:True:XH:16:True:0";

	/// <summary>The full recorded fields of <see cref="FullLine" />, asserted against the parser record-for-field.</summary>
	[Fact]
	public void Parse_EighteenFields_ParsesEveryField()
	{
		var submission = ScoreSubmission.Parse(FullLine);

		Assert.Equal(
			new ScoreSubmission(
				"md5", "lyx", "checksum",
				5, 4, 3, 2, 1, 0,
				123456, 999,
				true, "XH", 16,
				true, 0,
				new DateTimeOffset(2026, 4, 19, 8, 11, 2, TimeSpan.Zero),
				2),
			submission);
	}

	[Theory]
	[InlineData(17)]
	[InlineData(16)]
	public void Parse_FewerThanEighteenFields_ThrowsFormatException(int fieldCount)
	{
		var text = fieldCount == 17 ? LineWithoutLastField : LineWithoutLastTwoFields;

		Assert.Throws<FormatException>(() => ScoreSubmission.Parse(text));
	}

	[Theory]
	[InlineData("md5:lyx:checksum:abc:4:3:2:1:0:123456:999:True:XH:16:True:0:260419081102:3 4 ")] // hits 300
	[InlineData(
		"md5:lyx:checksum:5:4:3:2:1:0:3000000000:999:True:XH:16:True:0:260419081102:3 4 ")] // int overflow
	public void Parse_NonNumericIntegerField_ThrowsFormatException(string text)
	{
		Assert.Throws<FormatException>(() => ScoreSubmission.Parse(text));
	}

	[Theory]
	[InlineData("True", true)]
	[InlineData("False", false)]
	public void Parse_PassFlag_IsTheExactTrueOrFalseText(string flagText, bool expected)
	{
		var text =
			$"md5:lyx:checksum:5:4:3:2:1:0:123456:999:True:XH:16:{flagText}:0:260419081102:3 4 ";

		Assert.Equal(expected, ScoreSubmission.Parse(text).IsPassed);
	}

	[Theory]
	[InlineData("True", true)]
	[InlineData("False", false)]
	public void Parse_FullComboFlag_IsTheExactTrueOrFalseText(string flagText, bool expected)
	{
		var text =
			$"md5:lyx:checksum:5:4:3:2:1:0:123456:999:{flagText}:XH:16:True:0:260419081102:3 4 ";

		Assert.Equal(expected, ScoreSubmission.Parse(text).IsFullCombo);
	}

	[Theory]
	[InlineData(0, 0)] // no space stalls
	[InlineData(1, 1)]
	[InlineData(2, 2)]
	[InlineData(3, 3)]
	[InlineData(4, 0)] // the value-four bit is masked off
	[InlineData(5, 1)]
	[InlineData(6, 2)]
	[InlineData(7, 3)]
	public void Parse_ClientVersionSpaces_ReadsSpaceCountMaskedOffFourAsClientFlags(
		int spaceCount,
		int expectedFlags)
	{
		var text =
			"md5:lyx:checksum:5:4:3:2:1:0:123456:999:True:XH:16:True:0:260419081102:"
			+ "5" + new string(' ', spaceCount);

		Assert.Equal(expectedFlags, ScoreSubmission.Parse(text).ClientFlags);
	}

	[Fact]
	public void Parse_Timestamp_InterpretsUtc()
	{
		var text =
			"md5:lyx:checksum:5:4:3:2:1:0:123456:999:True:XH:16:True:0:991231235959:3 4 ";

		var submission = ScoreSubmission.Parse(text);

		// "yy" 99 falls inside the invariant culture's two-digit-year window (up to 2029), so 1999.
		Assert.Equal(new DateTimeOffset(1999, 12, 31, 23, 59, 59, TimeSpan.Zero), submission.Timestamp);
		Assert.Equal(TimeSpan.Zero, submission.Timestamp.Offset);
	}
}