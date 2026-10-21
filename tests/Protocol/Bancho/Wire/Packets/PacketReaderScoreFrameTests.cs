using Basil.Protocol.Bancho.Models.Replay;
using Basil.Protocol.Bancho.Wire.Packets;

namespace Basil.Protocol.Bancho.Tests.Wire.Packets;

/// <summary>
///     Verifies <c>PacketReader.ReadScoreFrame</c> against a hand-laid-out scoreframe: exactly 29 bytes
///     for score v1 plus 16 more bytes (two doubles) for score v2, truncation at any byte ending in
///     <see cref="EndOfStreamException" />.
/// </summary>
/// <remarks>
///     Fixture layout per the osu! scoreframe: i32 time, u8 id, six u16 counts, i32 total score,
///     u16 max combo, u16 current combo, u8 perfect, u8 hp, u8 tag, u8 scoreV2 — all little-endian.
/// </remarks>
public class PacketReaderScoreFrameTests
{
	// time 1000, id 5, counts 2/3/4/5/6/7, score 0x11223344, maxCombo 300, combo 11,
	// perfect false, hp 142, tag 9, scoreV2 off.
	private const string V1Hex = "e8030000" + "05" + "0200" + "0300" + "0400" + "0500" + "0600" + "0700"
	                             + "44332211" + "2c01" + "0b00" + "00" + "8e" + "09" + "00";

	// 28 bytes of the v1 shape, scoreV2 on, combo portion 0.5, bonus portion 0.25 (as f64 little-endian):
	// the v1 hex's trailing scoreV2 byte 0x00 is replaced by 0x01, then the two doubles follow.
	private static readonly string V2Hex = V1Hex[..^2] + "01" + "000000000000e03f" + "000000000000d03f";

	/// <summary>The frame those fixture bytes describe, for <see cref="ScoreFrame" /> record equality.</summary>
	private static ScoreFrame V1Frame => new()
	{
		Time = 1000,
		Id = 5,
		Num300 = 2,
		Num100 = 3,
		Num50 = 4,
		NumGeki = 5,
		NumKatu = 6,
		NumMiss = 7,
		TotalScore = 287_454_020, // 0x11223344
		MaxCombo = 300,
		CurrentCombo = 11,
		Perfect = false,
		CurrentHp = 142,
		TagByte = 9,
		ScoreV2 = false,
		ComboPortion = null,
		BonusPortion = null
	};

	private static ScoreFrame V2Frame => V1Frame with
	{
		ScoreV2 = true,
		ComboPortion = 0.5,
		BonusPortion = 0.25
	};

	public static TheoryData<int> V1Truncations()
	{
		return Truncations(V1Hex.Length / 2);
	}

	public static TheoryData<int> V2Truncations()
	{
		return Truncations(V2Hex.Length / 2);
	}

	private static TheoryData<int> Truncations(int fullLength)
	{
		var data = new TheoryData<int>();
		for (var length = 0; length < fullLength; length++) data.Add(length);
		return data;
	}

	[Fact]
	public void ReadScoreFrame_V1TwentynineBytes_ParsesEveryField()
	{
		var frame = new PacketReader(new MemoryStream(Convert.FromHexString(V1Hex))).ReadScoreFrame();

		Assert.Equal(V1Frame, frame);
	}

	[Fact]
	public void ReadScoreFrame_V2FortyfiveBytes_ParsesComboAndBonusPortion()
	{
		var frame = new PacketReader(new MemoryStream(Convert.FromHexString(V2Hex))).ReadScoreFrame();

		Assert.Equal(V2Frame, frame);
	}

	[Theory]
	[MemberData(nameof(V1Truncations))]
	public void ReadScoreFrame_V1TruncatedAtAnyByte_ThrowsEndOfStream(int length)
	{
		var bytes = Convert.FromHexString(V1Hex);

		Assert.Throws<EndOfStreamException>(() => new PacketReader(new MemoryStream(bytes[..length])).ReadScoreFrame());
	}

	[Theory]
	[MemberData(nameof(V2Truncations))]
	public void ReadScoreFrame_V2TruncatedAtAnyByte_ThrowsEndOfStream(int length)
	{
		var bytes = Convert.FromHexString(V2Hex);

		Assert.Throws<EndOfStreamException>(() => new PacketReader(new MemoryStream(bytes[..length])).ReadScoreFrame());
	}
}