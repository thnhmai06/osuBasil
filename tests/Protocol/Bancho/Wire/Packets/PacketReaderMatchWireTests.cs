using Basil.Protocol.Bancho.Wire.Packets;

namespace Basil.Protocol.Bancho.Tests.Wire.Packets;

/// <summary>
///     Verifies <c>PacketReader.ReadMatch</c> against hand-laid-out room payloads: fixed-byte header fields
///     (u16 id, sbyte in-progress, sbyte powerplay, i32 mods), four osu! strings, 16 slot status bytes,
///     16 slot team bytes, an i32 player id only for occupied slots, host id, mode/win/team/free-mods
///     sbytes, the 16 per-slot mod i32 words when free mods are on, and the i32 seed. All fields are
///     little-endian and the reader consumes the payload exactly.
/// </summary>
public class PacketReaderMatchWireTests
{
	/// <summary>
	///     Builds a room payload per the osu! wire format. <paramref name="playerIds" /> is written as the i32
	///     words following the statuses; the caller supplies exactly the ids the fixture's occupied statuses
	///     require. Strings are written as an empty osu! string (<c>0x0B 0x00</c>), map id and mods as zero.
	/// </summary>
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
	public void ReadMatch_QuitStatusInEverySlot_ReadsNoPlayerIds()
	{
		// room id 40000, every slot marked quit (0x80), no occupied slots, so no player-id words follow.
		var stream = MatchPayload(
			40000,
			[.. Enumerable.Repeat<byte>(0x80, 16)],
			[.. Enumerable.Repeat<byte>(0, 16)],
			[],
			hostId: 0,
			freeMods: false,
			slotMods: [],
			seed: 0);

		var match = new PacketReader(stream).ReadMatch();

		Assert.Equal(40000, match.Id);
		Assert.Equal(Enumerable.Repeat(128, 16), match.SlotStatuses);
		Assert.Empty(match.SlotIds);
		AssertFullyConsumed(stream);
	}

	[Fact]
	public void ReadMatch_ReadyStatusReadsPlayerId_OtherStatusesDoNot()
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
			hostId: 3,
			freeMods: false,
			slotMods: [],
			seed: 42);

		var match = new PacketReader(stream).ReadMatch();

		Assert.Equal([250], match.SlotIds);
		Assert.Equal(3, match.HostId);
		Assert.Equal(42, match.Seed);
		AssertFullyConsumed(stream);
	}

	[Fact]
	public void ReadMatch_FreeModsOff_ReadsNoSlotMods()
	{
		var stream = MatchPayload(
			40_000,
			[.. Enumerable.Repeat<byte>(0x01, 16)],
			[.. Enumerable.Repeat<byte>(2, 16)],
			[],
			hostId: 3,
			freeMods: false,
			slotMods: [],
			seed: 42);

		var match = new PacketReader(stream).ReadMatch();

		Assert.False(match.FreeMods);
		Assert.Empty(match.SlotMods);
		AssertFullyConsumed(stream);
	}

	[Fact]
	public void ReadMatch_FreeModsOn_ReadsASlotModWordPerSlot()
	{
		var stream = MatchPayload(
			40_000,
			[.. Enumerable.Repeat<byte>(0x01, 16)],
			[.. Enumerable.Repeat<byte>(2, 16)],
			[],
			hostId: 3,
			freeMods: true,
			slotMods: [.. Enumerable.Repeat(7, 16)],
			seed: 42);

		var match = new PacketReader(stream).ReadMatch();

		Assert.True(match.FreeMods);
		Assert.Equal(Enumerable.Repeat(7, 16), match.SlotMods);
		AssertFullyConsumed(stream);
	}
}