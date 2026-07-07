using OpenOsuTournament.Bancho.Protocol.Multiplayer;

namespace OpenOsuTournament.Bancho.Protocol.Tests;

public class MatchSlotDataTests
{
    [Fact]
    public void HasPlayer_MatchesStatusMask_ForEveryByteValue()
    {
        for (var status = 0; status <= byte.MaxValue; status++)
        {
            var expected = (status & 0b0111_1100) != 0;
            var actual = new MatchSlotData(status, 0, 0, null).HasPlayer;

            Assert.Equal(expected, actual);
        }
    }
}
