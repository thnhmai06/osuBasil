using OpenOsuTournament.Bancho.Application.Sessions.Multiplayer;
using OpenOsuTournament.Bancho.Domain;
using OpenOsuTournament.Bancho.Domain.Beatmaps;
using OpenOsuTournament.Bancho.Domain.Multiplayer;

namespace OpenOsuTournament.Bancho.Application.Tests.Sessions;

/// <summary>Ported from app/objects/match.py's Match — slot lookup/state helpers, no networking.</summary>
public class MatchSessionTests
{
    private static MatchSession MakeMatch(int hostId = 1)
    {
        return new MatchSession(
            0, "test match", "pw", true,
            "Some Map", 100, new string('a', 32), hostId,
            GameMode.VanillaOsu, Mods.NoMod, MatchWinConditions.Score,
            MatchTeamTypes.HeadToHead, false, 0, "#multi_0");
    }

    [Fact]
    public void NewMatch_HasSixteenOpenSlots()
    {
        var match = MakeMatch();

        Assert.Equal(16, match.Slots.Count);
        Assert.All(match.Slots, s => Assert.Equal(SlotStatus.Open, s.Status));
        Assert.All(match.Slots, s => Assert.True(s.Empty));
    }

    [Fact]
    public void GetFreeSlotId_ReturnsFirstOpenSlot()
    {
        var match = MakeMatch();
        match.Slots[0].Status = SlotStatus.NotReady;
        match.Slots[0].PlayerId = 5;

        Assert.Equal(1, match.GetFreeSlotId());
    }

    [Fact]
    public void GetFreeSlotId_ReturnsNullWhenFull()
    {
        var match = MakeMatch();
        foreach (var slot in match.Slots) slot.Status = SlotStatus.NotReady;

        Assert.Null(match.GetFreeSlotId());
    }

    [Fact]
    public void GetSlot_FindsSlotByPlayerId()
    {
        var match = MakeMatch();
        match.Slots[3].PlayerId = 42;

        var slot = match.GetSlot(42);

        Assert.Same(match.Slots[3], slot);
    }

    [Fact]
    public void GetSlotId_FindsIndexByPlayerId()
    {
        var match = MakeMatch();
        match.Slots[7].PlayerId = 42;

        Assert.Equal(7, match.GetSlotId(42));
    }

    [Fact]
    public void GetHostSlot_FindsSlotOccupiedByHost()
    {
        var match = MakeMatch(9);
        match.Slots[2].PlayerId = 9;

        Assert.Same(match.Slots[2], match.GetHostSlot());
    }

    [Fact]
    public void IsReferee_TrueForHostAndAddedReferees()
    {
        var match = MakeMatch();
        match.AddReferee(2);

        Assert.True(match.IsReferee(1));
        Assert.True(match.IsReferee(2));
        Assert.False(match.IsReferee(3));
    }

    [Fact]
    public void RemoveReferee_NoLongerAReferee()
    {
        var match = MakeMatch();
        match.AddReferee(2);

        match.RemoveReferee(2);

        Assert.False(match.IsReferee(2));
    }

    [Fact]
    public void UnreadyPlayers_OnlyResetsSlotsInExpectedStatus()
    {
        var match = MakeMatch();
        match.Slots[0].Status = SlotStatus.Ready;
        match.Slots[1].Status = SlotStatus.NoMap;

        match.UnreadyPlayers();

        Assert.Equal(SlotStatus.NotReady, match.Slots[0].Status);
        Assert.Equal(SlotStatus.NoMap, match.Slots[1].Status);
    }

    [Fact]
    public void ResetPlayersLoadedStatus_ClearsLoadedAndSkippedOnAllSlots()
    {
        var match = MakeMatch();
        match.Slots[0].Loaded = true;
        match.Slots[0].Skipped = true;

        match.ResetPlayersLoadedStatus();

        Assert.False(match.Slots[0].Loaded);
        Assert.False(match.Slots[0].Skipped);
    }

    [Fact]
    public void MatchSlot_CopyFrom_CopiesPlayerStatusTeamAndMods_ButNotLoadedOrSkipped()
    {
        var source = new MatchSlot
        {
            PlayerId = 5, Status = SlotStatus.Ready, Team = MatchTeams.Red, Mods = Mods.Hidden, Loaded = true,
            Skipped = true
        };
        var target = new MatchSlot();

        target.CopyFrom(source);

        Assert.Equal(5, target.PlayerId);
        Assert.Equal(SlotStatus.Ready, target.Status);
        Assert.Equal(MatchTeams.Red, target.Team);
        Assert.Equal(Mods.Hidden, target.Mods);
        Assert.False(target.Loaded);
        Assert.False(target.Skipped);
    }

    [Fact]
    public void MatchSlot_Reset_ClearsEverythingBackToOpen()
    {
        var slot = new MatchSlot
        {
            PlayerId = 5, Status = SlotStatus.Ready, Team = MatchTeams.Red, Mods = Mods.Hidden, Loaded = true,
            Skipped = true
        };

        slot.Reset();

        Assert.True(slot.Empty);
        Assert.Equal(SlotStatus.Open, slot.Status);
        Assert.Equal(MatchTeams.Neutral, slot.Team);
        Assert.Equal(Mods.NoMod, slot.Mods);
        Assert.False(slot.Loaded);
        Assert.False(slot.Skipped);
    }

    [Fact]
    public void MatchSlot_Reset_CanTargetADifferentStatus()
    {
        var slot = new MatchSlot { PlayerId = 5, Status = SlotStatus.Ready };

        slot.Reset(SlotStatus.Locked);

        Assert.Equal(SlotStatus.Locked, slot.Status);
    }
}