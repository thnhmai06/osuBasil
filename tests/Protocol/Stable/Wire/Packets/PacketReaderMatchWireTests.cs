using Basil.Protocol.Game.Stable.Wire.Packets;

namespace Basil.Protocol.Stable.Tests.Wire.Packets;

public class PacketReaderMatchWireTests
{
	private static MemoryStream MatchPayload(
		ushort id,
		byte[] statuses,
		byte[] teams,
		int[] playerIds,
		int hostId,
		bool freeMods,
		int[] slotMods,
		int seed)
	{
		var stream = new MemoryStream();
		// no using: disposing the writer would close the stream the reader needs.
		var writer = new BinaryWriter(stream);
		writer.Write(id);
		writer.Write((sbyte)0); // in progress
		writer.Write((sbyte)0); // powerplay
		writer.Write(0); // mods

		EmptyOsuString(writer); // name
		EmptyOsuString(writer); // password
		EmptyOsuString(writer); // map name
		writer.Write(0); // map id
		EmptyOsuString(writer); // map md5

		for (var i = 0; i < 16; i++) writer.Write(statuses[i]);
		for (var i = 0; i < 16; i++) writer.Write(teams[i]);

		foreach (var playerId in playerIds) writer.Write(playerId);
		writer.Write(hostId);
		writer.Write((sbyte)0); // mode
		writer.Write((sbyte)0); // win condition
		writer.Write((sbyte)0); // team type
		writer.Write(freeMods ? (sbyte)1 : (sbyte)0);
		if (freeMods)
			foreach (var slotModsValue in slotMods)
				writer.Write(slotModsValue);

		writer.Write(seed);
		writer.Flush();
		stream.Position = 0;
		return stream;

		static void EmptyOsuString(BinaryWriter writer)
		{
			// existence byte 0x0B then ULEB128 length 0.
			writer.Write((byte)0x0B);
			writer.Write((byte)0x00);
		}
	}

	private static void AssertFullyConsumed(MemoryStream stream)
	{
		Assert.Equal(stream.Length, stream.Position);
	}

	[Fact]
	public void QuitEverySlot_NoPlayerIds()
	{
		// room id 40000, every slot marked quit (0x80), no occupied slots, so no player-id words follow.
		var stream = MatchPayload(
			40000,
			[.. Enumerable.Repeat<byte>(0x80, 16)],
			[.. Enumerable.Repeat<byte>(0, 16)],
			[],
			0,
			false,
			[],
			0);

		var match = new PacketReader(stream).ReadMatch();

		Assert.Equal(40000, match.Id);
		Assert.Equal(Enumerable.Repeat(128, 16), match.SlotStatuses);
		Assert.Empty(match.SlotIds);
		AssertFullyConsumed(stream);
	}

	[Fact]
	public void ReadyStatus_OthersNotRead()
	{
		// slot 0 is ready (0x08), which carries a player id; quit (0x80), locked (0x02) and open (0x01) carry none.
		var stream = MatchPayload(
			40_000,
			[
				0x08, 0x80, 0x02,
				1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1
			],
			[.. Enumerable.Repeat<byte>(1, 16)],
			[250],
			3,
			false,
			[],
			42);

		var match = new PacketReader(stream).ReadMatch();

		Assert.Equal([250], match.SlotIds);
		Assert.Equal(3, match.HostId);
		Assert.Equal(42, match.Seed);
		AssertFullyConsumed(stream);
	}

	[Fact]
	public void FreeModsOff_NoSlotMods()
	{
		var stream = MatchPayload(
			40_000,
			[.. Enumerable.Repeat<byte>(0x01, 16)],
			[.. Enumerable.Repeat<byte>(2, 16)],
			[],
			3,
			false,
			[],
			42);

		var match = new PacketReader(stream).ReadMatch();

		Assert.False(match.FreeMods);
		Assert.Empty(match.SlotMods);
		AssertFullyConsumed(stream);
	}

	[Fact]
	public void FreeModsOn_SlotModWords()
	{
		var stream = MatchPayload(
			40_000,
			[.. Enumerable.Repeat<byte>(0x01, 16)],
			[.. Enumerable.Repeat<byte>(2, 16)],
			[],
			3,
			true,
			[.. Enumerable.Repeat(7, 16)],
			42);

		var match = new PacketReader(stream).ReadMatch();

		Assert.True(match.FreeMods);
		Assert.Equal(Enumerable.Repeat(7, 16), match.SlotMods);
		AssertFullyConsumed(stream);
	}
}