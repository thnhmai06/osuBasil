using System.Drawing;
using Basil.Domain.Client;
using Basil.Domain.Scores;
using Basil.Protocol.Stable.Wire.Packets;

namespace Basil.Protocol.Stable.Tests.Wire.Packets;

/// <summary>
///     Verifies <c>PacketReader.ReadReplayFrameBundle</c> against a hand-laid-out bundle: an extra i32, a
///     u16 frame count, the frames (14 bytes each in the osu! replay wire format), the action byte, the
///     29-byte score frame, and the u16 sequence number. Any truncation ends in
///     <see cref="EndOfStreamException" />.
/// </summary>
public class PacketReaderReplayFrameBundleTests
{
	/// <remarks>
	///     Verbatim copy of the fixture constant in <see cref="PacketReaderScoreFrameTests" /> so this file is
	///     self-contained.
	/// </remarks>
	// time 1000, id 5, counts 2/3/4/5/6/7, score 0x11223344, maxCombo 300, combo 11,
	// perfect false, hp 142, tag 9, scoreV2 off.
	private const string V1Hex = "e8030000" + "05" + "0200" + "0300" + "0400" + "0500" + "0600" + "0700"
	                             + "44332211" + "2c01" + "0b00" + "00" + "8e" + "09" + "00";

	private static ScoreFrame V1Frame => new(TimeSpan.FromMilliseconds(1000), 5,
		new ScoreState(new HitCounts(2, 3, 4, 5, 6, 7), 287_454_020, 300, 11, false), 142, 9, null);

	/// <summary>
	///     Builds a SpectateFrames payload per the osu! wire format: extra i32, u16 frame count, then
	///     <paramref name="frameCount" /> frames of u8 key (Left1), u8 taiko (None), f32 x (1.5), f32 y (-2.5),
	///     i32 time (8 * frame index); followed by the action byte (Standard), the score frame above, and the
	///     u16 sequence 5.
	/// </summary>
	private static byte[] Bundle(int frameCount)
	{
		var stream = new MemoryStream();
		using var writer = new BinaryWriter(stream);
		writer.Write(1);
		writer.Write((ushort)frameCount);

		for (var i = 0; i < frameCount; i++)
		{
			writer.Write((byte)KeyState.Left1);
			writer.Write((byte)TaikoDrumState.None);
			writer.Write(1.5f); // 0000c03f little-endian
			writer.Write(-2.5f); // 000020c0 little-endian
			writer.Write(8 * i);
		}

		writer.Write((byte)SpectatorAction.Standard);
		writer.Write(Convert.FromHexString(V1Hex));
		writer.Write((ushort)5);
		return stream.ToArray();
	}

	public static TheoryData<int> Truncations()
	{
		// a two-frame bundle is 66 bytes: 4 extra + 2 count + 2 * 14 frames + 1 action + 29 score frame + 2 sequence.
		var data = new TheoryData<int>();
		for (var length = 0; length < 66; length++) data.Add(length);
		return data;
	}

	[Fact]
	public void EmptyCount_Parsed()
	{
		var bundle = new PacketReader(new MemoryStream(Bundle(0))).ReadReplayFrameBundle();

		// asserted per member: the record's frame-array field compares by reference.
		Assert.Empty(bundle.ReplayFrames);
		Assert.Equal(V1Frame, bundle.ScoreFrame);
		Assert.Equal(SpectatorAction.Standard, bundle.Action);
		Assert.Equal(5, bundle.Sequence);
	}

	[Fact]
	public void TwoFrames_Parsed()
	{
		var bundle = new PacketReader(new MemoryStream(Bundle(2))).ReadReplayFrameBundle();

		Assert.Equal(
		[
			new ReplayFrame(KeyState.Left1, TaikoDrumState.None, new PointF(1.5f, -2.5f), TimeSpan.FromMilliseconds(0)),
			new ReplayFrame(KeyState.Left1, TaikoDrumState.None, new PointF(1.5f, -2.5f), TimeSpan.FromMilliseconds(8))
		], bundle.ReplayFrames);
	}

	[Fact]
	public void HighestCount_ReadsAll()
	{
		var bundle = new PacketReader(new MemoryStream(Bundle(65_535))).ReadReplayFrameBundle();

		Assert.Equal(65_535, bundle.ReplayFrames.Count);
		Assert.Equal(
			new ReplayFrame(KeyState.Left1, TaikoDrumState.None, new PointF(1.5f, -2.5f), TimeSpan.FromMilliseconds(0)),
			bundle.ReplayFrames.First());
		Assert.Equal(
			new ReplayFrame(KeyState.Left1, TaikoDrumState.None, new PointF(1.5f, -2.5f),
				TimeSpan.FromMilliseconds(8 * 65_534)),
			bundle.ReplayFrames.Last());
	}

	[Theory]
	[MemberData(nameof(Truncations))]
	public void Truncated_EndOfStream(int byteLength)
	{
		var bytes = Bundle(2);

		Assert.Throws<EndOfStreamException>(() =>
			new PacketReader(new MemoryStream(bytes[..byteLength])).ReadReplayFrameBundle());
	}
}