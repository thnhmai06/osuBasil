using Basil.Protocol.Stable.Models.Multiplayer;

namespace Basil.Protocol.Stable.Tests.Models.Multiplayer;

public class RoomSlotPacketTests
{
	[Fact]
	public void StatusMask_MatchesEveryByte()
	{
		for (var status = 0; status <= byte.MaxValue; status++)
		{
			var expected = (status & 0b0111_1100) != 0;
			var actual = new RoomSlotPacket(status, 0, 0, null).HasPlayer;

			Assert.Equal(expected, actual);
		}
	}
}