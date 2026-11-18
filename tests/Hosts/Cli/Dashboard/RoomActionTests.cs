using Basil.Host.Cli.Dashboard.Tabs;

namespace Basil.Host.Cli.Tests.Dashboard;

public sealed class RoomActionTests
{
	private static RoomItem Room(string state = "waiting", int players = 1, string map = "Map", bool countdown = false,
		bool open = true)
	{
		return new RoomItem(1, "QA room", players, 16, state, string.Empty, "bob", string.Empty, 0, 0, open, false, [],
			open ? new RoomRules(false, false, map, "Osu", "None", false, "HeadToHead", "Score") : null, countdown);
	}

	[Fact]
	public void StartRound_IdleRoomWithMapAndPlayer_Offered()
	{
		Assert.True(Room().CanStartRound);
	}

	[Theory]
	[InlineData("playing", 1, "Map")]
	[InlineData("waiting", 0, "Map")]
	[InlineData("waiting", 1, "")]
	public void StartRound_PlayingOrEmptyOrNoMap_NotOffered(string state, int players, string map)
	{
		Assert.False(Room(state, players, map).CanStartRound);
	}

	[Fact]
	public void StartRound_ClosedRoom_NotOffered()
	{
		Assert.False(Room(open: false).CanStartRound);
	}

	[Fact]
	public void Timer_NoCountdownAndRoundCanStart_StartOffered()
	{
		var room = Room();

		Assert.True(room.CanStartTimer);
		Assert.False(room.CanAbortTimer);
	}

	[Fact]
	public void Timer_CountdownRunning_OnlyAbortOffered()
	{
		var room = Room(countdown: true);

		Assert.False(room.CanStartTimer);
		Assert.True(room.CanAbortTimer);
	}

	[Fact]
	public void Timer_RoundCannotStart_StartNotOffered()
	{
		Assert.False(Room(players: 0).CanStartTimer);
	}
}
