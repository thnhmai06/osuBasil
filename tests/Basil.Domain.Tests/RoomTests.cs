using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Users;

namespace Basil.Domain.Tests;

/// <summary>Verifies `Room`'s slot lookup and match-state helpers.</summary>
public class RoomTests
{
	private static User MakeUser(int id)
	{
		return new User { Id = id, Name = $"user{id}" };
	}

	private static Room MakeMatch(int hostId = 1)
	{
		return new Room
		{
			SlotId = 0,
			Name = "test match",
			Password = "pw",
			Host = MakeUser(hostId)
		};
	}

	[Fact]
	public void NewMatch_HasSixteenOpenSlots()
	{
		var match = MakeMatch();

		Assert.Equal(16, match.Slots.Count);
		Assert.All(match.Slots, s => Assert.Equal(RoomSlotStatus.Open, s.Status));
		Assert.All(match.Slots, s => Assert.True(s.IsEmpty));
	}

	[Fact]
	public void GetFreeSlotId_ReturnsFirstOpenSlot()
	{
		var match = MakeMatch();
		match.Slots[0].Status = RoomSlotStatus.NotReady;
		match.Slots[0].Player = MakeUser(5);

		Assert.Equal(1, match.GetFreeSlotId());
	}

	[Fact]
	public void GetFreeSlotId_ReturnsNullWhenFull()
	{
		var match = MakeMatch();
		foreach (var slot in match.Slots) slot.Status = RoomSlotStatus.NotReady;

		Assert.Null(match.GetFreeSlotId());
	}

	[Fact]
	public void GetSlot_FindsSlotByPlayerId()
	{
		var match = MakeMatch();
		var player = MakeUser(42);
		match.Slots[3].Player = player;

		var slot = match.GetSlot(player);

		Assert.Same(match.Slots[3], slot);
	}

	[Fact]
	public void GetSlotId_FindsIndexByPlayerId()
	{
		var match = MakeMatch();
		var player = MakeUser(42);
		match.Slots[7].Player = player;

		Assert.Equal(7, match.GetSlotId(player));
	}

	[Fact]
	public void GetHostSlot_FindsSlotOccupiedByHost()
	{
		var match = MakeMatch(9);
		match.Slots[2].Player = MakeUser(9);

		Assert.Same(match.Slots[2], match.GetHostSlot());
	}

	[Fact]
	public void IsReferee_TrueOnlyForAddedReferees_HostIsNotAutomaticallyOne()
	{
		var match = MakeMatch();
		match.AddReferee(MakeUser(2));

		Assert.False(match.IsReferee(MakeUser(1)));
		Assert.True(match.IsReferee(MakeUser(2)));
		Assert.False(match.IsReferee(MakeUser(3)));
	}

	[Fact]
	public void RemoveReferee_NoLongerAReferee()
	{
		var match = MakeMatch();
		var referee = MakeUser(2);
		match.AddReferee(referee);

		match.RemoveReferee(referee);

		Assert.False(match.IsReferee(referee));
	}

	[Fact]
	public void UnreadyPlayers_OnlyResetsSlotsInExpectedStatus()
	{
		var match = MakeMatch();
		match.Slots[0].Status = RoomSlotStatus.Ready;
		match.Slots[1].Status = RoomSlotStatus.NoMap;

		match.UnreadyPlayers();

		Assert.Equal(RoomSlotStatus.NotReady, match.Slots[0].Status);
		Assert.Equal(RoomSlotStatus.NoMap, match.Slots[1].Status);
	}

	[Fact]
	public void ResetPlayersLoadedStatus_ClearsLoadedAndSkippedOnAllSlots()
	{
		var match = MakeMatch();
		match.Slots[0].BeatmapLoaded = true;
		match.Slots[0].IntroSkipped = true;

		match.ResetPlayersLoadedStatus();

		Assert.False(match.Slots[0].BeatmapLoaded);
		Assert.False(match.Slots[0].IntroSkipped);
	}

	[Fact]
	public void MatchSlot_CopyFrom_CopiesPlayerStatusTeamAndMods_ButNotLoadedOrSkipped()
	{
		var source = new RoomSlot
		{
			Player = MakeUser(5),
			Status = RoomSlotStatus.Ready,
			Team = MatchTeam.Red,
			Mods = Mods.Hidden,
			BeatmapLoaded = true,
			IntroSkipped = true
		};
		var target = new RoomSlot();

		target.CopyFrom(source);

		Assert.Equal(5, target.Player?.Id);
		Assert.Equal(RoomSlotStatus.Ready, target.Status);
		Assert.Equal(MatchTeam.Red, target.Team);
		Assert.Equal(Mods.Hidden, target.Mods);
		Assert.False(target.BeatmapLoaded);
		Assert.False(target.IntroSkipped);
	}

	[Fact]
	public void MatchSlot_Reset_ClearsEverythingBackToOpen()
	{
		var slot = new RoomSlot
		{
			Player = MakeUser(5),
			Status = RoomSlotStatus.Ready,
			Team = MatchTeam.Red,
			Mods = Mods.Hidden,
			BeatmapLoaded = true,
			IntroSkipped = true
		};

		slot.Reset();

		Assert.True(slot.IsEmpty);
		Assert.Equal(RoomSlotStatus.Open, slot.Status);
		Assert.Equal(MatchTeam.Neutral, slot.Team);
		Assert.Equal(Mods.NoMod, slot.Mods);
		Assert.False(slot.BeatmapLoaded);
		Assert.False(slot.IntroSkipped);
	}

	[Fact]
	public void MatchSlot_Reset_CanTargetADifferentStatus()
	{
		var slot = new RoomSlot { Player = MakeUser(5), Status = RoomSlotStatus.Ready };

		slot.Reset(RoomSlotStatus.Locked);

		Assert.Equal(RoomSlotStatus.Locked, slot.Status);
	}
}