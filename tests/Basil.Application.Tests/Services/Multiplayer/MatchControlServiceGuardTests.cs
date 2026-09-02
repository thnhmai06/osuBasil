using Basil.Application.Services.Multiplayer;
using Basil.Application.Tests.Packets;
using Basil.Domain.Multiplayer;
using Microsoft.Extensions.Logging.Abstractions;

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
		return new MatchControlService(_fixture.MatchMembership, _fixture.MatchRepository, _fixture.RoundEndOutbox,
			_fixture.BeatmapRepository,
			_fixture.SessionRegistry, _fixture.IrcSessionRegistry, NullLogger<MatchControlService>.Instance);
	}

	[Fact]
	public void CreateMatch_ChannelTopicStartsAsTheRoomName()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(host);
		var match = _fixture.CreateMatch(host);

		var channel = _fixture.ChannelRegistry.GetByName(match.ChatChannelName);

		Assert.NotNull(channel);
		Assert.Equal(match.Name, channel.Topic);
	}

	[Fact]
	public async Task SetNameAsync_SyncsChannelTopicToTheNewName()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(host);
		var match = _fixture.CreateMatch(host);
		var control = MakeService();

		await control.SetNameAsync(match, "Grand Finals");

		Assert.Equal("Grand Finals", match.Name);
		Assert.Equal("Grand Finals", _fixture.ChannelRegistry.GetByName(match.ChatChannelName)!.Topic);
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
	public async Task SetRefereesAsync_OmitsCurrentCreatorReferee_ReturnsWouldRemoveCreator()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var newRef = MultiplayerTestSupport.MakePlayer(2, "newref");
		_fixture.RegisterAll(host, newRef);
		var match = _fixture.CreateMatch(host);
		var control = MakeService();

		var result = await control.SetRefereesAsync(match, [newRef]);

		Assert.Equal(MatchControlService.SetRefereesResult.WouldRemoveCreator, result);
		Assert.Contains(host.Id, match.Referees);
	}

	[Fact]
	public async Task SetRefereesAsync_IncludesCreator_ReplacesRestNormally()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var oldRef = MultiplayerTestSupport.MakePlayer(2, "oldref");
		var newRef = MultiplayerTestSupport.MakePlayer(3, "newref");
		_fixture.RegisterAll(host, oldRef, newRef);
		var match = _fixture.CreateMatch(host);
		match.AddReferee(oldRef.Id);
		var control = MakeService();

		var result = await control.SetRefereesAsync(match, [host, newRef]);

		Assert.Equal(MatchControlService.SetRefereesResult.Ok, result);
		Assert.Contains(host.Id, match.Referees);
		Assert.Contains(newRef.Id, match.Referees);
		Assert.DoesNotContain(oldRef.Id, match.Referees);
	}

	/// <summary>
	///     Regression test (Issue #4): AddRefereeAsync used to unconditionally add and report success
	///     even when the target already held referee status. It now detects the no-op case up front
	///     instead of mutating and reporting success for something that didn't change.
	/// </summary>
	[Fact]
	public async Task AddRefereeAsync_TargetAlreadyReferee_ReturnsAlreadyReferee()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var referee = MultiplayerTestSupport.MakePlayer(2, "referee");
		_fixture.RegisterAll(host, referee);
		var match = _fixture.CreateMatch(host, hostIsReferee: false);
		match.AddReferee(referee.Id);
		var control = MakeService();

		var result = await control.AddRefereeAsync(null, null, match, referee);

		Assert.Equal(MatchControlService.AddRefereeResult.AlreadyReferee, result);
		Assert.Single(match.Referees);
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
	public async Task RemoveOneRefereeAsync_LastReferee_IsAlsoCreator_ReturnsTargetIsCreator()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(host);
		var match = _fixture.CreateMatch(host);
		var control = MakeService();

		var result = await control.RemoveOneRefereeAsync(null, null, match, host);

		Assert.Equal(MatchControlService.RemoveRefereeResult.TargetIsCreator, result);
		Assert.Contains(host.Id, match.Referees);
	}

	[Fact]
	public async Task RemoveOneRefereeAsync_LastReferee_NotCreator_ReturnsWouldLeaveEmpty()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var referee = MultiplayerTestSupport.MakePlayer(2, "referee");
		_fixture.RegisterAll(host, referee);
		var match = _fixture.CreateMatch(host, hostIsReferee: false);
		match.AddReferee(referee.Id);
		var control = MakeService();

		var result = await control.RemoveOneRefereeAsync(null, null, match, referee);

		Assert.Equal(MatchControlService.RemoveRefereeResult.WouldLeaveEmpty, result);
		Assert.Contains(referee.Id, match.Referees);
	}

	[Fact]
	public async Task RemoveOneRefereeAsync_TargetIsCreator_Rejected_EvenWithOtherRefereesPresent()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var referee = MultiplayerTestSupport.MakePlayer(2, "referee");
		_fixture.RegisterAll(host, referee);
		var match = _fixture.CreateMatch(host);
		match.AddReferee(referee.Id);
		var control = MakeService();

		var result = await control.RemoveOneRefereeAsync(null, null, match, host);

		Assert.Equal(MatchControlService.RemoveRefereeResult.TargetIsCreator, result);
		Assert.Contains(host.Id, match.Referees);
	}

	[Fact]
	public async Task RemoveOneRefereeAsync_NotSeated_IsPartedFromMatchChannel()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var referee = MultiplayerTestSupport.MakePlayer(2, "referee");
		_fixture.RegisterAll(host, referee);
		var match = _fixture.CreateMatch(host);
		match.AddReferee(referee.Id);
		var channel = _fixture.ChannelRegistry.GetByName(match.ChatChannelName)!;
		channel.Join(referee.Id);
		referee.JoinChannel(channel.Name);
		Assert.Contains(channel.Name, referee.Channels);
		var control = MakeService();

		var result = await control.RemoveOneRefereeAsync(null, null, match, referee);

		Assert.Equal(MatchControlService.RemoveRefereeResult.Ok, result);
		Assert.DoesNotContain(channel.Name, referee.Channels);
	}

	[Fact]
	public async Task RemoveOneRefereeAsync_StillSeated_StaysInMatchChannel()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var referee = MultiplayerTestSupport.MakePlayer(2, "referee");
		_fixture.RegisterAll(host, referee);
		var match = _fixture.CreateMatch(host);
		match.AddReferee(referee.Id);
		Assert.Equal(MatchMembershipService.JoinResult.Ok,
			await _fixture.MatchMembership.JoinAsync(referee, match, ""));
		var channel = _fixture.ChannelRegistry.GetByName(match.ChatChannelName)!;
		Assert.Contains(channel.Name, referee.Channels);
		var control = MakeService();

		var result = await control.RemoveOneRefereeAsync(null, null, match, referee);

		Assert.Equal(MatchControlService.RemoveRefereeResult.Ok, result);
		Assert.Contains(channel.Name, referee.Channels);
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
	public async Task SetBans_NewlyBannedSeatedPlayer_IsKickedFromMatch()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var target = MultiplayerTestSupport.MakePlayer(2, "target");
		_fixture.RegisterAll(host, target);
		var match = _fixture.CreateMatch(host);
		Assert.Equal(MatchMembershipService.JoinResult.Ok, await _fixture.MatchMembership.JoinAsync(target, match, ""));
		var control = MakeService();

		await control.SetBansAsync(match, [target.Id]);

		Assert.Contains(target.Id, match.BannedIds);
		Assert.Null(target.Match);
	}

	[Fact]
	public async Task AddBans_KicksNewlySeatedPlayer_SkipsAlreadyBanned()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var target = MultiplayerTestSupport.MakePlayer(2, "target");
		_fixture.RegisterAll(host, target);
		var match = _fixture.CreateMatch(host);
		Assert.Equal(MatchMembershipService.JoinResult.Ok, await _fixture.MatchMembership.JoinAsync(target, match, ""));
		var control = MakeService();

		await control.AddBansAsync(match, [target.Id]);

		Assert.Contains(target.Id, match.BannedIds);
		Assert.Null(target.Match);
	}

	[Fact]
	public async Task ForceInvite_TargetBanned_Rejected()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var target = MultiplayerTestSupport.MakePlayer(2, "target");
		_fixture.RegisterAll(host, target);
		var match = _fixture.CreateMatch(host);
		match.AddBan(target.Id);
		var control = MakeService();

		var result = await control.ForceInviteAsync(match, target);

		Assert.Equal(MatchControlService.ForceInviteResult.TargetBanned, result);
		Assert.Null(target.Match);
	}

	[Fact]
	public async Task ForceInvite_TargetInAnotherMatch_Rejected()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var otherHost = MultiplayerTestSupport.MakePlayer(2, "otherhost");
		var target = MultiplayerTestSupport.MakePlayer(3, "target");
		_fixture.RegisterAll(host, otherHost, target);
		var match = _fixture.CreateMatch(host);
		var otherMatch = _fixture.CreateMatch(otherHost);
		Assert.Equal(MatchMembershipService.JoinResult.Ok,
			await _fixture.MatchMembership.JoinAsync(target, otherMatch, ""));
		var control = MakeService();

		var result = await control.ForceInviteAsync(match, target);

		Assert.Equal(MatchControlService.ForceInviteResult.TargetInAnotherMatch, result);
	}

	[Fact]
	public async Task ForceInvite_AlreadyInThisMatch_ReturnsOk()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(host);
		var match = _fixture.CreateMatch(host);
		var control = MakeService();

		var result = await control.ForceInviteAsync(match, host);

		Assert.Equal(MatchControlService.ForceInviteResult.Ok, result);
	}

	[Fact]
	public async Task ForceInvite_BypassesPasswordPrivateAndLock_SeatsPlayer()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var target = MultiplayerTestSupport.MakePlayer(2, "target");
		_fixture.RegisterAll(host, target);
		var match = _fixture.CreateMatch(host);
		match.IsPrivate = true;
		match.IsLocked = true;
		var control = MakeService();

		var result = await control.ForceInviteAsync(match, target);

		Assert.Equal(MatchControlService.ForceInviteResult.Ok, result);
		Assert.Equal(match, target.Match);
	}

	[Fact]
	public async Task ForceInvite_NoFreeSlot_ReturnsNoFreeSlot()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var target = MultiplayerTestSupport.MakePlayer(2, "target");
		_fixture.RegisterAll(host, target);
		var match = _fixture.CreateMatch(host);
		for (var i = 0; i < 16; i++) match.Slots[i].Status = SlotStatus.Locked;
		var control = MakeService();

		var result = await control.ForceInviteAsync(match, target);

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

		var result = await control.SetSlotsAsync(match, entries, true);

		Assert.Equal(MatchControlService.SetSlotsResult.UnknownUserId, result);
	}

	[Fact]
	public async Task SetSlotsAsync_Put_MissingCurrentOccupant_ReturnsPlayerCountMismatch()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var other = MultiplayerTestSupport.MakePlayer(2, "other");
		_fixture.RegisterAll(host, other);
		var match = _fixture.CreateMatch(host);
		Assert.Equal(MatchMembershipService.JoinResult.Ok, await _fixture.MatchMembership.JoinAsync(other, match, ""));
		var control = MakeService();

		// Only re-teams host's slot — doesn't mention `other`, who is also currently seated.
		var hostSlot = match.GetSlotId(host.Id)!.Value;
		var entries = new Dictionary<int, MatchControlService.SlotPatchEntry>
		{
			[hostSlot] = new(host.Id, "Red", null)
		};

		var result = await control.SetSlotsAsync(match, entries, true);

		Assert.Equal(MatchControlService.SetSlotsResult.PlayerCountMismatch, result);
	}

	[Fact]
	public async Task SetSlotsAsync_Patch_DoesNotRequireFullOccupantCoverage()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var other = MultiplayerTestSupport.MakePlayer(2, "other");
		_fixture.RegisterAll(host, other);
		var match = _fixture.CreateMatch(host);
		Assert.Equal(MatchMembershipService.JoinResult.Ok, await _fixture.MatchMembership.JoinAsync(other, match, ""));
		var control = MakeService();

		var hostSlot = match.GetSlotId(host.Id)!.Value;
		var entries = new Dictionary<int, MatchControlService.SlotPatchEntry>
		{
			[hostSlot] = new(host.Id, "Red", null)
		};

		var result = await control.SetSlotsAsync(match, entries, false);

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

		var result = await control.SetSlotsAsync(match, entries, false);

		Assert.Equal(MatchControlService.SetSlotsResult.SlotOccupiedAndLocked, result);
	}

	[Fact]
	public async Task SetSlotsAsync_Swap_SwapsTwoOccupants()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		var other = MultiplayerTestSupport.MakePlayer(2, "other");
		_fixture.RegisterAll(host, other);
		var match = _fixture.CreateMatch(host);
		Assert.Equal(MatchMembershipService.JoinResult.Ok, await _fixture.MatchMembership.JoinAsync(other, match, ""));
		var control = MakeService();

		var hostSlot = match.GetSlotId(host.Id)!.Value;
		var otherSlot = match.GetSlotId(other.Id)!.Value;
		var entries = new Dictionary<int, MatchControlService.SlotPatchEntry>
		{
			[hostSlot] = new(other.Id, null, null),
			[otherSlot] = new(host.Id, null, null)
		};

		var result = await control.SetSlotsAsync(match, entries, true);

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

		var result = await control.SetSlotsAsync(match, entries, false);

		Assert.Equal(MatchControlService.SetSlotsResult.Ok, result);
		Assert.Equal(teamBefore, match.Slots[hostSlot].Team);
	}

	/// <summary>Regression test (ADR-003): abort now queues the round-end write instead of awaiting it inline.</summary>
	[Fact]
	public async Task AbortAsync_InProgress_QueuesRoundEndWriteAndClearsCurrentRoundId()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(host);
		var match = _fixture.CreateMatch(host);
		match.InProgress = true;
		match.CurrentRoundId = 5;
		var control = MakeService();

		var result = await control.AbortAsync(match);

		Assert.Equal(MatchControlService.AbortResult.Ok, result);
		Assert.Null(match.CurrentRoundId);
		Assert.False(match.InProgress);
		Assert.Contains(_fixture.RoundEndOutbox.Enqueued, w => w.MatchId == match.DbId && w.RoundId == 5 && w.Aborted);
	}

	/// <summary>
	///     Regression test (ADR-003): a full round-end outbox must not stop the abort from clearing
	///     match state or broadcasting — only the database write is lost, loudly logged.
	/// </summary>
	[Fact]
	public async Task AbortAsync_OutboxFull_StillClearsStateAndReturnsOk()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(host);
		var match = _fixture.CreateMatch(host);
		match.InProgress = true;
		match.CurrentRoundId = 5;
		_fixture.RoundEndOutbox.ThrowFull = true;
		var control = MakeService();

		var result = await control.AbortAsync(match);

		Assert.Equal(MatchControlService.AbortResult.Ok, result);
		Assert.Null(match.CurrentRoundId);
		Assert.False(match.InProgress);
	}

	/// <summary>
	///     Regression test (ADR-004 slots-stream unification): SetSlotsAsync used to call
	///     PublishSlotsAsync alone, leaving the per-index `slot` and general `main`/`settings`
	///     channels silent for HTTP-driven slot mutations. It now routes through EnqueueStateAsync,
	///     the same path every packet-driven mutation uses, so all of them fire together.
	/// </summary>
	[Fact]
	public async Task SetSlotsAsync_PublishesSlotsAndPerSlotAndMain()
	{
		var host = MultiplayerTestSupport.MakePlayer(1, "host");
		_fixture.RegisterAll(host);
		var match = _fixture.CreateMatch(host);
		var control = MakeService();
		var hostSlot = match.GetSlotId(host.Id)!.Value;
		var entries = new Dictionary<int, MatchControlService.SlotPatchEntry>
		{
			[hostSlot] = new(host.Id, "Red", null)
		};

		var result = await control.SetSlotsAsync(match, entries, false);

		Assert.Equal(MatchControlService.SetSlotsResult.Ok, result);
		Assert.NotEmpty(_fixture.EventBus.SlotsPublishes);
		Assert.NotEmpty(_fixture.EventBus.SlotPublishes);
		Assert.NotEmpty(_fixture.EventBus.MainPublishes);
	}
}