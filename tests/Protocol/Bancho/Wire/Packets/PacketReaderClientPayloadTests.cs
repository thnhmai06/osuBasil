using Basil.Protocol.Bancho.Models.Multiplayer;
using Basil.Protocol.Bancho.Models.Presence;
using Basil.Protocol.Bancho.Wire.Packets;

namespace Basil.Protocol.Bancho.Tests.Wire.Packets;

/// <summary>
///     Verifies the client request split (<c>PacketReader.Split</c>) and the typed payload readers against
///     hand-written payload bytes, per the packet layouts bancho.py reads.
/// </summary>
public class PacketReaderClientPayloadTests
{
	private static PacketReader Reader(string hex)
	{
		return PacketReader.ForPayload(Convert.FromHexString(hex));
	}

	[Fact]
	public void Split_ReturnsEachPacketWithItsOwnPayload()
	{
		// Ping (4) empty, ChannelJoin (63) with "#"
		var packets = PacketReader.Split(Convert.FromHexString("04000000000000" + "3f000003000000" + "0b0123"));

		Assert.Collection(packets,
			p =>
			{
				Assert.Equal(ClientPacketType.Ping, p.Type);
				Assert.Empty(p.Payload);
			},
			p =>
			{
				Assert.Equal(ClientPacketType.ChannelJoin, p.Type);
				Assert.Equal(Convert.FromHexString("0b0123"), p.Payload);
			});
	}

	[Fact]
	public void Split_UnknownType_IsSkippedByItsLength()
	{
		// id 0xFFFF with two payload bytes, then Ping
		var packets = PacketReader.Split(Convert.FromHexString("ffff00" + "02000000" + "aabb" + "04000000000000"));

		Assert.Equal(ClientPacketType.Ping, Assert.Single(packets).Type);
	}

	[Fact]
	public void Split_EmptyBody_ReturnsNoPackets()
	{
		Assert.Empty(PacketReader.Split([]));
	}

	[Fact]
	public void Split_TruncatedHeader_ThrowsEndOfStream()
	{
		Assert.Throws<EndOfStreamException>(() => PacketReader.Split(Convert.FromHexString("040000")));
	}

	[Fact]
	public void Split_TruncatedPayload_ThrowsEndOfStream()
	{
		Assert.Throws<EndOfStreamException>(() => PacketReader.Split(Convert.FromHexString("3f000005000000" + "0b")));
	}

	[Fact]
	public void Split_NegativeLength_ThrowsInvalidData()
	{
		Assert.Throws<InvalidDataException>(() => PacketReader.Split(Convert.FromHexString("040000" + "ffffffff")));
	}

	[Fact]
	public void Reader_OverPayload_ThrowsEndOfStream()
	{
		Assert.Throws<EndOfStreamException>(() => Reader("0200").ReadInt32());
	}

	[Fact]
	public void ReadChangeAction_ReadsFieldsInOrder()
	{
		// u8 action 2, "hi", "" md5, u32 mods 64, u8 mode 1, i32 map id 5
		var action = Reader("02" + "0b026869" + "00" + "40000000" + "01" + "05000000").ReadChangeAction();

		Assert.Equal(new ChangeAction(2, "hi", "", 64, 1, 5), action);
	}

	[Fact]
	public void ReadChangeAction_ModsHighBit_ReadsUnsigned()
	{
		var action = Reader("00" + "00" + "00" + "00000080" + "00" + "00000000").ReadChangeAction();

		Assert.Equal(2_147_483_648u, action.Mods);
	}

	[Fact]
	public void ReadJoinMatch_ReadsIdAndPassword()
	{
		Assert.Equal(new JoinMatchRequest(258, "pw"), Reader("02010000" + "0b027077").ReadJoinMatch());
	}

	[Fact]
	public void ReadBeatmapInfoRequest_ReadsFilenamesThenIds()
	{
		var request = Reader("02000000" + "0b05612e6f7375" + "0b0162" + "01000000" + "07000000")
			.ReadBeatmapInfoRequest();

		Assert.Equal(["a.osu", "b"], request.Filenames);
		Assert.Equal([7], request.Ids);
	}

	[Fact]
	public void ReadBeatmapInfoRequest_NegativeCount_ThrowsInvalidData()
	{
		Assert.Throws<InvalidDataException>(() => Reader("ffffffff").ReadBeatmapInfoRequest());
	}

	[Fact]
	public void ReadInt32List_ReadsU16CountThenSignedValues()
	{
		// UserStatsRequest and UserPresenceRequest carry this shape
		Assert.Equal([1, -1], Reader("0200" + "01000000" + "ffffffff").ReadInt32List());
	}

	[Fact]
	public void ReadInt32_ReadsSignedLittleEndian()
	{
		// StartSpectating, MatchChangeSlot, MatchLock, MatchChangeMods, MatchTransferHost, MatchInvite,
		// FriendAdd, FriendRemove, ReceiveUpdates, ToggleBlockNonFriendDms, Tournament*, UserPresenceRequestAll
		Assert.Equal(-2, Reader("feffffff").ReadInt32());
	}

	[Fact]
	public void ReadOsuString_ReadsErrorReportAndChannelNames()
	{
		// ErrorReport, ChannelJoin and ChannelPart carry one string
		Assert.Equal("#osu", Reader("0b04236f7375").ReadOsuString());
	}

	[Fact]
	public void ReadMessage_ReadsPublicPrivateAndAwayMessages()
	{
		// SendPublicMessage, SendPrivateMessage and SetAwayMessage carry a message
		var message = Reader("00" + "0b0168" + "0b022366" + "01000000").ReadMessage();

		Assert.Equal("h", message.Text);
		Assert.Equal("#f", message.Recipient);
		Assert.Equal(1, message.SenderId);
	}
}