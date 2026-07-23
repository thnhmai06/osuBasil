using Basil.Application.Services.Multiplayer;
using Basil.Application.Tests.PacketHandlers;
using Basil.Domain.Multiplayer;

namespace Basil.Application.Tests.Services.Multiplayer;

/// <summary>
///     Unit-tests the guard rules added for the `/matches/{matchId}/refs`, `/ban`, `/invite`, and
///     `/slots` sub-resource routes directly on <see cref="MatchControlService" /> — the risk here is
///     entirely in this validation logic, not in HTTP plumbing (already covered by the integration
///     tests), so these run against the same in-memory fixture the `!mp` chat command tests use.
/// </summary>
public class MatchControlServiceGuardTests
{
    private readonly MultiplayerTestSupport.Fixture _fixture = new();

    private MatchControlService MakeService()
    {
        return new MatchControlService(_fixture.MatchMembership, _fixture.MatchPersistence, _fixture.MapRepository,
            _fixture.SessionRegistry);
    }

    [Fact]
    public async Task SetRefereesAsync_EmptyTargets_ReturnsWouldLeaveEmpty()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(host);
        var match = _fixture.CreateMatch(host);
        var control = MakeService();

        var result = await control.SetRefereesAsync(match, []);

        Assert.Equal(MatchControlService.SetRefereesResult.WouldLeaveEmpty, result);
    }

    [Fact]
    public async Task SetRefereesAsync_FullReplace_AddsNewAndRemovesUnlisted()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        var oldRef = MultiplayerTestSupport.MakePlayer(2, "oldref");
        var newRef = MultiplayerTestSupport.MakePlayer(3, "newref");
        _fixture.RegisterAll(host, oldRef, newRef);
        var match = _fixture.CreateMatch(host, hostIsReferee: false);
        match.AddReferee(oldRef.Id);
        var control = MakeService();

        var result = await control.SetRefereesAsync(match, [newRef]);

        Assert.Equal(MatchControlService.SetRefereesResult.Ok, result);
        Assert.DoesNotContain(oldRef.Id, match.Referees);
        Assert.Contains(newRef.Id, match.Referees);
    }

    [Fact]
    public async Task AddRefereesAsync_AddsBatch_SkipsExisting()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        var already = MultiplayerTestSupport.MakePlayer(2, "already");
        var newRef = MultiplayerTestSupport.MakePlayer(3, "newref");
        _fixture.RegisterAll(host, already, newRef);
        var match = _fixture.CreateMatch(host, hostIsReferee: false);
        match.AddReferee(already.Id);
        var control = MakeService();

        await control.AddRefereesAsync(match, [already, newRef]);

        Assert.Equal(2, match.Referees.Count);
        Assert.Contains(newRef.Id, match.Referees);
    }

    [Fact]
    public async Task RemoveOneRefereeAsync_LastReferee_ReturnsWouldLeaveEmpty()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(host);
        var match = _fixture.CreateMatch(host);
        var control = MakeService();

        var result = await control.RemoveOneRefereeAsync(null, null, match, host);

        Assert.Equal(MatchControlService.RemoveRefereeResult.WouldLeaveEmpty, result);
        Assert.Contains(host.Id, match.Referees);
    }

    [Fact]
    public async Task RemoveOneRefereeAsync_NotAReferee_ReturnsNotAReferee()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        var other = MultiplayerTestSupport.MakePlayer(2, "other");
        _fixture.RegisterAll(host, other);
        var match = _fixture.CreateMatch(host);
        var control = MakeService();

        var result = await control.RemoveOneRefereeAsync(null, null, match, other);

        Assert.Equal(MatchControlService.RemoveRefereeResult.NotAReferee, result);
    }

    [Fact]
    public async Task RemoveOneRefereeAsync_NotLastReferee_Succeeds()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        var referee = MultiplayerTestSupport.MakePlayer(2, "referee");
        _fixture.RegisterAll(host, referee);
        var match = _fixture.CreateMatch(host);
        match.AddReferee(referee.Id);
        var control = MakeService();

        var result = await control.RemoveOneRefereeAsync(null, null, match, referee);

        Assert.Equal(MatchControlService.RemoveRefereeResult.Ok, result);
        Assert.DoesNotContain(referee.Id, match.Referees);
    }

    [Fact]
    public void SetBans_NewlyBannedSeatedPlayer_IsKickedFromMatch()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        var target = MultiplayerTestSupport.MakePlayer(2, "target");
        _fixture.RegisterAll(host, target);
        var match = _fixture.CreateMatch(host);
        Assert.True(_fixture.MatchMembership.Join(target, match, ""));
        var control = MakeService();

        control.SetBans(match, [target.Id]);

        Assert.Contains(target.Id, match.BannedIds);
        Assert.Null(target.Match);
    }

    [Fact]
    public void AddBans_KicksNewlySeatedPlayer_SkipsAlreadyBanned()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        var target = MultiplayerTestSupport.MakePlayer(2, "target");
        _fixture.RegisterAll(host, target);
        var match = _fixture.CreateMatch(host);
        Assert.True(_fixture.MatchMembership.Join(target, match, ""));
        var control = MakeService();

        control.AddBans(match, [target.Id]);

        Assert.Contains(target.Id, match.BannedIds);
        Assert.Null(target.Match);
    }

    [Fact]
    public void ForceInvite_TargetBanned_Rejected()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        var target = MultiplayerTestSupport.MakePlayer(2, "target");
        _fixture.RegisterAll(host, target);
        var match = _fixture.CreateMatch(host);
        match.AddBan(target.Id);
        var control = MakeService();

        var result = control.ForceInvite(match, target);

        Assert.Equal(MatchControlService.ForceInviteResult.TargetBanned, result);
        Assert.Null(target.Match);
    }

    [Fact]
    public void ForceInvite_TargetInAnotherMatch_Rejected()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        var otherHost = MultiplayerTestSupport.MakePlayer(2, "otherhost");
        var target = MultiplayerTestSupport.MakePlayer(3, "target");
        _fixture.RegisterAll(host, otherHost, target);
        var match = _fixture.CreateMatch(host);
        var otherMatch = _fixture.CreateMatch(otherHost);
        Assert.True(_fixture.MatchMembership.Join(target, otherMatch, ""));
        var control = MakeService();

        var result = control.ForceInvite(match, target);

        Assert.Equal(MatchControlService.ForceInviteResult.TargetInAnotherMatch, result);
    }

    [Fact]
    public void ForceInvite_AlreadyInThisMatch_ReturnsOk()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(host);
        var match = _fixture.CreateMatch(host);
        var control = MakeService();

        var result = control.ForceInvite(match, host);

        Assert.Equal(MatchControlService.ForceInviteResult.Ok, result);
    }

    [Fact]
    public void ForceInvite_BypassesPasswordPrivateAndLock_SeatsPlayer()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        var target = MultiplayerTestSupport.MakePlayer(2, "target");
        _fixture.RegisterAll(host, target);
        var match = _fixture.CreateMatch(host);
        match.IsPrivate = true;
        match.IsLocked = true;
        var control = MakeService();

        var result = control.ForceInvite(match, target);

        Assert.Equal(MatchControlService.ForceInviteResult.Ok, result);
        Assert.Equal(match, target.Match);
    }

    [Fact]
    public void ForceInvite_NoFreeSlot_ReturnsNoFreeSlot()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        var target = MultiplayerTestSupport.MakePlayer(2, "target");
        _fixture.RegisterAll(host, target);
        var match = _fixture.CreateMatch(host);
        for (var i = 0; i < 16; i++) match.Slots[i].Status = SlotStatus.Locked;
        var control = MakeService();

        var result = control.ForceInvite(match, target);

        Assert.Equal(MatchControlService.ForceInviteResult.NoFreeSlot, result);
    }

    [Fact]
    public async Task SetSlotsAsync_Put_UnknownUserId_Rejected()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        var notInMatch = MultiplayerTestSupport.MakePlayer(2, "outsider");
        _fixture.RegisterAll(host, notInMatch);
        var match = _fixture.CreateMatch(host);
        var control = MakeService();

        var entries = new Dictionary<int, MatchControlService.SlotPatchEntry>
        {
            [1] = new(notInMatch.Id, null, null)
        };

        var result = await control.SetSlotsAsync(match, entries, isFullReplace: true);

        Assert.Equal(MatchControlService.SetSlotsResult.UnknownUserId, result);
    }

    [Fact]
    public async Task SetSlotsAsync_Put_MissingCurrentOccupant_ReturnsPlayerCountMismatch()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        var other = MultiplayerTestSupport.MakePlayer(2, "other");
        _fixture.RegisterAll(host, other);
        var match = _fixture.CreateMatch(host);
        Assert.True(_fixture.MatchMembership.Join(other, match, ""));
        var control = MakeService();

        // Only re-teams host's slot — doesn't mention `other`, who is also currently seated.
        var hostSlot = match.GetSlotId(host.Id)!.Value;
        var entries = new Dictionary<int, MatchControlService.SlotPatchEntry>
        {
            [hostSlot] = new(host.Id, "Red", null)
        };

        var result = await control.SetSlotsAsync(match, entries, isFullReplace: true);

        Assert.Equal(MatchControlService.SetSlotsResult.PlayerCountMismatch, result);
    }

    [Fact]
    public async Task SetSlotsAsync_Patch_DoesNotRequireFullOccupantCoverage()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        var other = MultiplayerTestSupport.MakePlayer(2, "other");
        _fixture.RegisterAll(host, other);
        var match = _fixture.CreateMatch(host);
        Assert.True(_fixture.MatchMembership.Join(other, match, ""));
        var control = MakeService();

        var hostSlot = match.GetSlotId(host.Id)!.Value;
        var entries = new Dictionary<int, MatchControlService.SlotPatchEntry>
        {
            [hostSlot] = new(host.Id, "Red", null)
        };

        var result = await control.SetSlotsAsync(match, entries, isFullReplace: false);

        Assert.Equal(MatchControlService.SetSlotsResult.Ok, result);
        Assert.Equal(MatchTeam.Red, match.Slots[hostSlot].Team);
    }

    [Fact]
    public async Task SetSlotsAsync_UserIdAndLockedTogether_Rejected()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(host);
        var match = _fixture.CreateMatch(host);
        var control = MakeService();

        var hostSlot = match.GetSlotId(host.Id)!.Value;
        var entries = new Dictionary<int, MatchControlService.SlotPatchEntry>
        {
            [hostSlot] = new(host.Id, null, true)
        };

        var result = await control.SetSlotsAsync(match, entries, isFullReplace: false);

        Assert.Equal(MatchControlService.SetSlotsResult.SlotOccupiedAndLocked, result);
    }

    [Fact]
    public async Task SetSlotsAsync_Swap_SwapsTwoOccupants()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        var other = MultiplayerTestSupport.MakePlayer(2, "other");
        _fixture.RegisterAll(host, other);
        var match = _fixture.CreateMatch(host);
        Assert.True(_fixture.MatchMembership.Join(other, match, ""));
        var control = MakeService();

        var hostSlot = match.GetSlotId(host.Id)!.Value;
        var otherSlot = match.GetSlotId(other.Id)!.Value;
        var entries = new Dictionary<int, MatchControlService.SlotPatchEntry>
        {
            [hostSlot] = new(other.Id, null, null),
            [otherSlot] = new(host.Id, null, null)
        };

        var result = await control.SetSlotsAsync(match, entries, isFullReplace: true);

        Assert.Equal(MatchControlService.SetSlotsResult.Ok, result);
        Assert.Equal(other.Id, match.Slots[hostSlot].PlayerId);
        Assert.Equal(host.Id, match.Slots[otherSlot].PlayerId);
    }

    [Fact]
    public async Task SetSlotsAsync_InvalidTeamValue_PreservesExistingTeam()
    {
        var host = MultiplayerTestSupport.MakePlayer(1, "host");
        _fixture.RegisterAll(host);
        var match = _fixture.CreateMatch(host, MatchTeamType.TeamVs);
        var control = MakeService();

        var hostSlot = match.GetSlotId(host.Id)!.Value;
        var teamBefore = match.Slots[hostSlot].Team;
        var entries = new Dictionary<int, MatchControlService.SlotPatchEntry>
        {
            [hostSlot] = new(host.Id, "Neutral", null)
        };

        var result = await control.SetSlotsAsync(match, entries, isFullReplace: false);

        Assert.Equal(MatchControlService.SetSlotsResult.Ok, result);
        Assert.Equal(teamBefore, match.Slots[hostSlot].Team);
    }
}
