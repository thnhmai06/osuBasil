using Basil.Application.Runtime.Multiplayer;
using Basil.Application.Runtime.Multiplayer.Events;
using Basil.Application.Runtime.Multiplayer.Rooms;
using Basil.Application.Runtime.Tests.Support;
using Basil.Domain.Mechanics;
using Basil.Domain.Permissions;

namespace Basil.Application.Runtime.Tests.Multiplayer.Rooms;

/// <summary>What a caller is expected to get from a room operation.</summary>
public enum RoomExpectation
{
	/// <summary>The operation succeeds and changes the room.</summary>
	Allowed,

	/// <summary>The caller lacks the room right; the outcome names the role refusal.</summary>
	RoleDenied,

	/// <summary>The caller may not join rooms at all; the denial names the missing permission.</summary>
	RoomJoinDenied
}

/// <summary>
///     Who may do what in a room. The expected rights come from AGENTS.md ("Authority") and
///     docs/for-developers/multiplayer.md ("Room roles and permissions"): the creator holds every room right, a
///     referee holds every right except managing referees, the host holds the in-client host operations, a player
///     may chat and invite, and a user who may manage every room acts with the creator's authority wherever they
///     are.
/// </summary>
public sealed class ScopedRoomAuthorityTests
{
	[Theory]
	[InlineData(RoomRole.Creator, RoomExpectation.Allowed)]
	[InlineData(RoomRole.Referee, RoomExpectation.Allowed)]
	[InlineData(RoomRole.Host, RoomExpectation.RoleDenied)]
	[InlineData(RoomRole.Player, RoomExpectation.RoleDenied)]
	[InlineData(RoomRole.Outsider, RoomExpectation.RoleDenied)]
	[InlineData(RoomRole.Manager, RoomExpectation.Allowed)]
	[InlineData(RoomRole.NoRoomJoin, RoomExpectation.RoomJoinDenied)]
	[InlineData(RoomRole.SuspendedCreator, RoomExpectation.RoomJoinDenied)]
	public async Task MoveAsync_TheCallersRoleDecidesWhoMayMoveAnotherPlayer(RoomRole role,
		RoomExpectation expected)
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var room = setup.RoomFor(role);
		var caller = setup.ConnectionFor(role);
		var target = setup.Player;
		var before = room.Slots.Find(target.Record)?.Index;
		var events = app.RoomEvents.Events.Count;

		var result = await room.ScopeTo(caller).Slots.MoveAsync(target.Record, 5,
			TestContext.Current.CancellationToken);

		if (expected == RoomExpectation.Allowed)
		{
			Assert.Equal(RoomOutcome.Ok, result.Outcome);
			Assert.Equal(5, room.Slots.Find(target.Record)!.Index);
			Assert.Equal(5, Assert.IsType<RoomPlayerMoved>(app.RoomEvents.Events[^1]).To);
			return;
		}

		AssertRefused(result, expected);
		Assert.Equal(before, room.Slots.Find(target.Record)?.Index);
		Assert.Equal(events, app.RoomEvents.Events.Count);
	}

	[Theory]
	[InlineData(RoomRole.Creator, RoomExpectation.Allowed)]
	[InlineData(RoomRole.Referee, RoomExpectation.Allowed)]
	[InlineData(RoomRole.Host, RoomExpectation.RoleDenied)]
	[InlineData(RoomRole.Player, RoomExpectation.RoleDenied)]
	[InlineData(RoomRole.Outsider, RoomExpectation.RoleDenied)]
	[InlineData(RoomRole.Manager, RoomExpectation.Allowed)]
	[InlineData(RoomRole.NoRoomJoin, RoomExpectation.RoomJoinDenied)]
	[InlineData(RoomRole.SuspendedCreator, RoomExpectation.RoomJoinDenied)]
	public async Task ArrangeAsync_TheCallersRoleDecidesWhoMayArrangeTheSlots(RoomRole role,
		RoomExpectation expected)
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var room = setup.RoomFor(role);
		var caller = setup.ConnectionFor(role);
		var target = setup.Player;
		var before = room.Slots.Find(target.Record)?.Index;
		var events = app.RoomEvents.Events.Count;
		var arrangement = new[]
		{
			new SlotArrangement(1, setup.Creator.Record, null, false),
			new SlotArrangement(2, setup.Host.Record, null, false),
			new SlotArrangement(5, target.Record, null, false)
		};

		var result = await room.ScopeTo(caller).Slots.ArrangeAsync(arrangement,
			TestContext.Current.CancellationToken);

		if (expected == RoomExpectation.Allowed)
		{
			Assert.Equal(RoomOutcome.Ok, result.Outcome);
			Assert.Equal(5, room.Slots.Find(target.Record)!.Index);
			Assert.IsType<RoomSlotsArranged>(app.RoomEvents.Events[^1]);
			return;
		}

		AssertRefused(result, expected);
		Assert.Equal(before, room.Slots.Find(target.Record)?.Index);
		Assert.Equal(events, app.RoomEvents.Events.Count);
	}

	[Theory]
	[InlineData(RoomRole.Creator, RoomExpectation.Allowed)]
	[InlineData(RoomRole.Referee, RoomExpectation.Allowed)]
	[InlineData(RoomRole.Host, RoomExpectation.RoleDenied)]
	[InlineData(RoomRole.Player, RoomExpectation.RoleDenied)]
	[InlineData(RoomRole.Outsider, RoomExpectation.RoleDenied)]
	[InlineData(RoomRole.Manager, RoomExpectation.Allowed)]
	[InlineData(RoomRole.NoRoomJoin, RoomExpectation.RoomJoinDenied)]
	[InlineData(RoomRole.SuspendedCreator, RoomExpectation.RoomJoinDenied)]
	public async Task SetTeamAsync_TheCallersRoleDecidesWhoMayPutAPlayerOnATeam(RoomRole role,
		RoomExpectation expected)
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var room = setup.RoomFor(role);
		var caller = setup.ConnectionFor(role);
		var target = setup.Player;
		await room.ScopeTo(setup.Manager).Settings.ConfigureAsync(
			new RoomSettingsChange(TeamType: GameTeamType.TeamVs), TestContext.Current.CancellationToken);
		var before = room.Slots.Find(target.Record)?.Team;
		var events = app.RoomEvents.Events.Count;

		var result = await room.ScopeTo(caller).Slots.SetTeamAsync(target.Record, GameTeam.Blue,
			TestContext.Current.CancellationToken);

		if (expected == RoomExpectation.Allowed)
		{
			Assert.Equal(RoomOutcome.Ok, result.Outcome);
			Assert.Equal(GameTeam.Blue, room.Slots.Find(target.Record)!.Team);
			Assert.IsType<RoomSlotTeamChanged>(app.RoomEvents.Events[^1]);
			return;
		}

		AssertRefused(result, expected);
		Assert.Equal(before, room.Slots.Find(target.Record)?.Team);
		Assert.Equal(events, app.RoomEvents.Events.Count);
	}

	[Theory]
	[InlineData(RoomRole.Creator, RoomExpectation.Allowed)]
	[InlineData(RoomRole.Referee, RoomExpectation.Allowed)]
	[InlineData(RoomRole.Host, RoomExpectation.Allowed)]
	[InlineData(RoomRole.Player, RoomExpectation.RoleDenied)]
	[InlineData(RoomRole.Outsider, RoomExpectation.RoleDenied)]
	[InlineData(RoomRole.Manager, RoomExpectation.Allowed)]
	[InlineData(RoomRole.NoRoomJoin, RoomExpectation.RoomJoinDenied)]
	[InlineData(RoomRole.SuspendedCreator, RoomExpectation.RoomJoinDenied)]
	public async Task ToggleLockAsync_TheCallersRoleDecidesWhoMayLockASlot(RoomRole role,
		RoomExpectation expected)
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var room = setup.RoomFor(role);
		var caller = setup.ConnectionFor(role);
		var before = room.Slots.At(6)!.Locked;
		var events = app.RoomEvents.Events.Count;

		var result = await room.ScopeTo(caller).Slots.ToggleLockAsync(6, TestContext.Current.CancellationToken);

		if (expected == RoomExpectation.Allowed)
		{
			Assert.Equal(RoomOutcome.Ok, result.Outcome);
			Assert.True(room.Slots.At(6)!.Locked);
			Assert.IsType<RoomSlotLockChanged>(app.RoomEvents.Events[^1]);
			return;
		}

		AssertRefused(result, expected);
		Assert.Equal(before, room.Slots.At(6)!.Locked);
		Assert.Equal(events, app.RoomEvents.Events.Count);
	}

	[Theory]
	[InlineData(RoomRole.Creator, RoomExpectation.Allowed)]
	[InlineData(RoomRole.Referee, RoomExpectation.Allowed)]
	[InlineData(RoomRole.Host, RoomExpectation.RoleDenied)]
	[InlineData(RoomRole.Player, RoomExpectation.RoleDenied)]
	[InlineData(RoomRole.Outsider, RoomExpectation.RoleDenied)]
	[InlineData(RoomRole.Manager, RoomExpectation.Allowed)]
	[InlineData(RoomRole.NoRoomJoin, RoomExpectation.RoomJoinDenied)]
	[InlineData(RoomRole.SuspendedCreator, RoomExpectation.RoomJoinDenied)]
	public async Task SetLockedAsync_TheCallersRoleDecidesWhoMayLockTheRoom(RoomRole role,
		RoomExpectation expected)
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var room = setup.RoomFor(role);
		var caller = setup.ConnectionFor(role);
		var before = room.Slots.Locked;
		var events = app.RoomEvents.Events.Count;

		var result = await room.ScopeTo(caller).Settings.SetLockedAsync(true, TestContext.Current.CancellationToken);

		if (expected == RoomExpectation.Allowed)
		{
			Assert.Equal(RoomOutcome.Ok, result.Outcome);
			Assert.True(room.Slots.Locked);
			Assert.IsType<RoomLockChanged>(app.RoomEvents.Events[^1]);
			return;
		}

		AssertRefused(result, expected);
		Assert.Equal(before, room.Slots.Locked);
		Assert.Equal(events, app.RoomEvents.Events.Count);
	}

	[Theory]
	[InlineData(RoomRole.Creator, RoomExpectation.Allowed)]
	[InlineData(RoomRole.Referee, RoomExpectation.Allowed)]
	[InlineData(RoomRole.Host, RoomExpectation.Allowed)]
	[InlineData(RoomRole.Player, RoomExpectation.RoleDenied)]
	[InlineData(RoomRole.Outsider, RoomExpectation.RoleDenied)]
	[InlineData(RoomRole.Manager, RoomExpectation.Allowed)]
	[InlineData(RoomRole.NoRoomJoin, RoomExpectation.RoomJoinDenied)]
	[InlineData(RoomRole.SuspendedCreator, RoomExpectation.RoomJoinDenied)]
	public async Task ConfigureAsync_TheCallersRoleDecidesWhoMayChangeTheSettings(RoomRole role,
		RoomExpectation expected)
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var room = setup.RoomFor(role);
		var caller = setup.ConnectionFor(role);
		var before = room.Settings.Name;
		var events = app.RoomEvents.Events.Count;

		var result = await room.ScopeTo(caller).Settings.ConfigureAsync(new RoomSettingsChange(Name: "renamed"),
			TestContext.Current.CancellationToken);

		if (expected == RoomExpectation.Allowed)
		{
			Assert.Equal(RoomOutcome.Ok, result.Outcome);
			Assert.Equal("renamed", room.Settings.Name);
			Assert.IsType<RoomSettingsChanged>(app.RoomEvents.Events[^1]);
			return;
		}

		AssertRefused(result, expected);
		Assert.Equal(before, room.Settings.Name);
		Assert.Equal(events, app.RoomEvents.Events.Count);
	}

	[Theory]
	[InlineData(RoomRole.Creator, RoomExpectation.Allowed)]
	[InlineData(RoomRole.Referee, RoomExpectation.Allowed)]
	[InlineData(RoomRole.Host, RoomExpectation.Allowed)]
	[InlineData(RoomRole.Player, RoomExpectation.RoleDenied)]
	[InlineData(RoomRole.Outsider, RoomExpectation.RoleDenied)]
	[InlineData(RoomRole.Manager, RoomExpectation.Allowed)]
	[InlineData(RoomRole.NoRoomJoin, RoomExpectation.RoomJoinDenied)]
	[InlineData(RoomRole.SuspendedCreator, RoomExpectation.RoomJoinDenied)]
	public async Task StartAsync_TheCallersRoleDecidesWhoMayStartARound(RoomRole role, RoomExpectation expected)
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var room = setup.RoomFor(role);
		var caller = setup.ConnectionFor(role);
		await room.ScopeTo(setup.Manager).Settings.ConfigureAsync(
			new RoomSettingsChange(Beatmap: RoomFixture.Beatmap), TestContext.Current.CancellationToken);
		var before = room.Rounds.LastRound;
		var events = app.RoomEvents.Events.Count;

		var result = await room.ScopeTo(caller).Rounds.StartAsync(TestContext.Current.CancellationToken);

		if (expected == RoomExpectation.Allowed)
		{
			Assert.Equal(RoomOutcome.Ok, result.Outcome);
			Assert.NotNull(room.Rounds.LastRound);
			Assert.Equal(1, room.Rounds.LastRound!.Index);
			Assert.IsType<RoomRoundStarted>(app.RoomEvents.Events[^1]);
			return;
		}

		AssertRefused(result, expected);
		Assert.Equal(before, room.Rounds.LastRound);
		Assert.Equal(events, app.RoomEvents.Events.Count);
	}

	[Theory]
	[InlineData(RoomRole.Creator, RoomExpectation.Allowed)]
	[InlineData(RoomRole.Referee, RoomExpectation.Allowed)]
	[InlineData(RoomRole.Host, RoomExpectation.Allowed)]
	[InlineData(RoomRole.Player, RoomExpectation.RoleDenied)]
	[InlineData(RoomRole.Outsider, RoomExpectation.RoleDenied)]
	[InlineData(RoomRole.Manager, RoomExpectation.Allowed)]
	[InlineData(RoomRole.NoRoomJoin, RoomExpectation.RoomJoinDenied)]
	[InlineData(RoomRole.SuspendedCreator, RoomExpectation.RoomJoinDenied)]
	public async Task CancelCountdownAsync_TheCallersRoleDecidesWhoMayCancelTheCountdown(RoomRole role,
		RoomExpectation expected)
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var room = setup.RoomFor(role);
		var caller = setup.ConnectionFor(role);
		await room.ScopeTo(setup.Manager).Rounds.StartCountdownAsync(TimeSpan.FromMinutes(5), false,
			TestContext.Current.CancellationToken);
		var before = room.Rounds.Countdown;
		var events = app.RoomEvents.Events.Count;

		var result = await room.ScopeTo(caller).Rounds.CancelCountdownAsync(TestContext.Current.CancellationToken);

		if (expected == RoomExpectation.Allowed)
		{
			Assert.Equal(RoomOutcome.Ok, result.Outcome);
			Assert.Null(room.Rounds.Countdown);
			Assert.Equal(RoomCountdownCancelReason.Aborted, Assert.IsType<RoomCountdownCancelled>(app.RoomEvents.Events[^1]).Reason);
			return;
		}

		AssertRefused(result, expected);
		Assert.Same(before, room.Rounds.Countdown);
		Assert.Equal(events, app.RoomEvents.Events.Count);
	}

	[Theory]
	[InlineData(RoomRole.Creator, RoomExpectation.Allowed)]
	[InlineData(RoomRole.Referee, RoomExpectation.Allowed)]
	[InlineData(RoomRole.Host, RoomExpectation.Allowed)]
	[InlineData(RoomRole.Player, RoomExpectation.RoleDenied)]
	[InlineData(RoomRole.Outsider, RoomExpectation.RoleDenied)]
	[InlineData(RoomRole.Manager, RoomExpectation.Allowed)]
	[InlineData(RoomRole.NoRoomJoin, RoomExpectation.RoomJoinDenied)]
	[InlineData(RoomRole.SuspendedCreator, RoomExpectation.RoomJoinDenied)]
	public async Task AbortAsync_TheCallersRoleDecidesWhoMayAbortTheRound(RoomRole role, RoomExpectation expected)
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var room = setup.RoomFor(role);
		var caller = setup.ConnectionFor(role);
		await room.ScopeTo(setup.Manager).Settings.ConfigureAsync(
			new RoomSettingsChange(Beatmap: RoomFixture.Beatmap), TestContext.Current.CancellationToken);
		await room.ScopeTo(setup.Manager).Rounds.StartAsync(TestContext.Current.CancellationToken);
		var before = room.Rounds.CurrentRound;
		var events = app.RoomEvents.Events.Count;

		var result = await room.ScopeTo(caller).Rounds.AbortAsync(TestContext.Current.CancellationToken);

		if (expected == RoomExpectation.Allowed)
		{
			Assert.Equal(RoomOutcome.Ok, result.Outcome);
			Assert.Null(room.Rounds.CurrentRound);
			Assert.IsType<RoomRoundAborted>(app.RoomEvents.Events[^1]);
			return;
		}

		AssertRefused(result, expected);
		Assert.Equal(before, room.Rounds.CurrentRound);
		Assert.Equal(events, app.RoomEvents.Events.Count);
	}

	[Theory]
	[InlineData(RoomRole.Creator, RoomExpectation.Allowed)]
	[InlineData(RoomRole.Referee, RoomExpectation.Allowed)]
	[InlineData(RoomRole.Host, RoomExpectation.RoleDenied)]
	[InlineData(RoomRole.Player, RoomExpectation.RoleDenied)]
	[InlineData(RoomRole.Outsider, RoomExpectation.RoleDenied)]
	[InlineData(RoomRole.Manager, RoomExpectation.Allowed)]
	[InlineData(RoomRole.NoRoomJoin, RoomExpectation.RoomJoinDenied)]
	[InlineData(RoomRole.SuspendedCreator, RoomExpectation.RoomJoinDenied)]
	public async Task SeatAsync_TheCallersRoleDecidesWhoMaySeatAPlayer(RoomRole role, RoomExpectation expected)
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var room = setup.RoomFor(role);
		var caller = setup.ConnectionFor(role);
		var target = setup.Outsider;
		var before = room.Slots.Find(target.Record)?.Index;
		var events = app.RoomEvents.Events.Count;

		var result = await room.ScopeTo(caller).Members.SeatAsync(target, TestContext.Current.CancellationToken);

		if (expected == RoomExpectation.Allowed)
		{
			Assert.Equal(RoomOutcome.Ok, result.Outcome);
			Assert.NotNull(room.Slots.Find(target.Record));
			Assert.IsType<RoomPlayerJoined>(app.RoomEvents.Events[^1]);
			return;
		}

		AssertRefused(result, expected);
		Assert.Equal(before, room.Slots.Find(target.Record)?.Index);
		Assert.Equal(events, app.RoomEvents.Events.Count);
	}

	[Theory]
	[InlineData(RoomRole.Creator, RoomExpectation.Allowed)]
	[InlineData(RoomRole.Referee, RoomExpectation.Allowed)]
	[InlineData(RoomRole.Host, RoomExpectation.Allowed)]
	[InlineData(RoomRole.Player, RoomExpectation.RoleDenied)]
	[InlineData(RoomRole.Outsider, RoomExpectation.RoleDenied)]
	[InlineData(RoomRole.Manager, RoomExpectation.Allowed)]
	[InlineData(RoomRole.NoRoomJoin, RoomExpectation.RoomJoinDenied)]
	[InlineData(RoomRole.SuspendedCreator, RoomExpectation.RoomJoinDenied)]
	public async Task KickAsync_TheCallersRoleDecidesWhoMayKickAPlayer(RoomRole role, RoomExpectation expected)
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var room = setup.RoomFor(role);
		var caller = setup.ConnectionFor(role);
		var target = setup.Player;
		var before = room.Slots.Find(target.Record)?.Index;
		var events = app.RoomEvents.Events.Count;

		var result = await room.ScopeTo(caller).Members.KickAsync(target.Record, TestContext.Current.CancellationToken);

		if (expected == RoomExpectation.Allowed)
		{
			Assert.Equal(RoomOutcome.Ok, result.Outcome);
			Assert.Null(room.Slots.Find(target.Record));
			Assert.IsType<RoomPlayerKicked>(app.RoomEvents.Events[^1]);
			return;
		}

		AssertRefused(result, expected);
		Assert.Equal(before, room.Slots.Find(target.Record)?.Index);
		Assert.Equal(events, app.RoomEvents.Events.Count);
	}

	[Theory]
	[InlineData(RoomRole.Creator, RoomExpectation.Allowed)]
	[InlineData(RoomRole.Referee, RoomExpectation.Allowed)]
	[InlineData(RoomRole.Host, RoomExpectation.RoleDenied)]
	[InlineData(RoomRole.Player, RoomExpectation.RoleDenied)]
	[InlineData(RoomRole.Outsider, RoomExpectation.RoleDenied)]
	[InlineData(RoomRole.Manager, RoomExpectation.Allowed)]
	[InlineData(RoomRole.NoRoomJoin, RoomExpectation.RoomJoinDenied)]
	[InlineData(RoomRole.SuspendedCreator, RoomExpectation.RoomJoinDenied)]
	public async Task BanAsync_TheCallersRoleDecidesWhoMayBanAPlayer(RoomRole role, RoomExpectation expected)
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var room = setup.RoomFor(role);
		var caller = setup.ConnectionFor(role);
		var target = setup.Player;
		var before = room.Members.Banned.Contains(target.Record);
		var events = app.RoomEvents.Events.Count;

		var result = await room.ScopeTo(caller).Members.BanAsync(target.Record, TestContext.Current.CancellationToken);

		if (expected == RoomExpectation.Allowed)
		{
			Assert.Equal(RoomOutcome.Ok, result.Outcome);
			Assert.True(room.Members.Banned.Contains(target.Record));
			Assert.Null(room.Slots.Find(target.Record));
			Assert.IsType<RoomPlayerBanned>(app.RoomEvents.Events[^1]);
			return;
		}

		AssertRefused(result, expected);
		Assert.Equal(before, room.Members.Banned.Contains(target.Record));
		Assert.Equal(events, app.RoomEvents.Events.Count);
	}

	[Theory]
	[InlineData(RoomRole.Creator, RoomExpectation.Allowed)]
	[InlineData(RoomRole.Referee, RoomExpectation.Allowed)]
	[InlineData(RoomRole.Host, RoomExpectation.RoleDenied)]
	[InlineData(RoomRole.Player, RoomExpectation.RoleDenied)]
	[InlineData(RoomRole.Outsider, RoomExpectation.RoleDenied)]
	[InlineData(RoomRole.Manager, RoomExpectation.Allowed)]
	[InlineData(RoomRole.NoRoomJoin, RoomExpectation.RoomJoinDenied)]
	[InlineData(RoomRole.SuspendedCreator, RoomExpectation.RoomJoinDenied)]
	public async Task UnbanAsync_TheCallersRoleDecidesWhoMayLiftABan(RoomRole role, RoomExpectation expected)
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var room = setup.RoomFor(role);
		var caller = setup.ConnectionFor(role);
		var target = setup.Player;
		await room.ScopeTo(setup.Manager).Members.BanAsync(target.Record, TestContext.Current.CancellationToken);
		var before = room.Members.Banned.Contains(target.Record);
		var events = app.RoomEvents.Events.Count;

		var result = await room.ScopeTo(caller).Members
			.UnbanAsync(target.Record, TestContext.Current.CancellationToken);

		if (expected == RoomExpectation.Allowed)
		{
			Assert.Equal(RoomOutcome.Ok, result.Outcome);
			Assert.False(room.Members.Banned.Contains(target.Record));
			Assert.IsType<RoomPlayerUnbanned>(app.RoomEvents.Events[^1]);
			return;
		}

		AssertRefused(result, expected);
		Assert.Equal(before, room.Members.Banned.Contains(target.Record));
		Assert.Equal(events, app.RoomEvents.Events.Count);
	}

	[Theory]
	[InlineData(RoomRole.Creator, RoomExpectation.Allowed)]
	[InlineData(RoomRole.Referee, RoomExpectation.Allowed)]
	[InlineData(RoomRole.Host, RoomExpectation.Allowed)]
	[InlineData(RoomRole.Player, RoomExpectation.Allowed)]
	[InlineData(RoomRole.Outsider, RoomExpectation.RoleDenied)]
	[InlineData(RoomRole.Manager, RoomExpectation.Allowed)]
	[InlineData(RoomRole.NoRoomJoin, RoomExpectation.RoomJoinDenied)]
	[InlineData(RoomRole.SuspendedCreator, RoomExpectation.RoomJoinDenied)]
	public async Task InviteAsync_TheCallersRoleDecidesWhoMayInviteAUser(RoomRole role, RoomExpectation expected)
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var room = setup.RoomFor(role);
		var caller = setup.ConnectionFor(role);
		var target = app.Users.Of(setup.Outsider.Record);
		var events = app.RoomEvents.Events.Count;

		var result = await room.ScopeTo(caller).Members.InviteAsync(target, TestContext.Current.CancellationToken);

		if (expected == RoomExpectation.Allowed)
		{
			Assert.Equal(RoomOutcome.Ok, result.Outcome);
			var invited = Assert.IsType<RoomPlayerInvited>(app.RoomEvents.Events[^1]);
			Assert.Equal(setup.Outsider.Record, invited.Player);
			return;
		}

		AssertRefused(result, expected);
		Assert.Equal(events, app.RoomEvents.Events.Count);
	}

	[Theory]
	[InlineData(RoomRole.Creator, RoomExpectation.Allowed)]
	[InlineData(RoomRole.Referee, RoomExpectation.RoleDenied)]
	[InlineData(RoomRole.Host, RoomExpectation.RoleDenied)]
	[InlineData(RoomRole.Player, RoomExpectation.RoleDenied)]
	[InlineData(RoomRole.Outsider, RoomExpectation.RoleDenied)]
	[InlineData(RoomRole.Manager, RoomExpectation.Allowed)]
	[InlineData(RoomRole.NoRoomJoin, RoomExpectation.RoomJoinDenied)]
	[InlineData(RoomRole.SuspendedCreator, RoomExpectation.RoomJoinDenied)]
	public async Task AddRefereeAsync_TheCallersRoleDecidesWhoMayMakeAReferee(RoomRole role,
		RoomExpectation expected)
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var room = setup.RoomFor(role);
		var caller = setup.ConnectionFor(role);
		var target = setup.Outsider.Record;
		var before = room.Authority.Referees.Contains(target);
		var events = app.RoomEvents.Events.Count;

		var result = await room.ScopeTo(caller).Authority.AddRefereeAsync(target,
			TestContext.Current.CancellationToken);

		if (expected == RoomExpectation.Allowed)
		{
			Assert.Equal(RoomOutcome.Ok, result.Outcome);
			Assert.True(room.Authority.Referees.Contains(target));
			Assert.IsType<RoomRefereeAdded>(app.RoomEvents.Events[^1]);
			return;
		}

		AssertRefused(result, expected);
		Assert.Equal(before, room.Authority.Referees.Contains(target));
		Assert.Equal(events, app.RoomEvents.Events.Count);
	}

	[Theory]
	[InlineData(RoomRole.Creator, RoomExpectation.Allowed)]
	[InlineData(RoomRole.Referee, RoomExpectation.RoleDenied)]
	[InlineData(RoomRole.Host, RoomExpectation.RoleDenied)]
	[InlineData(RoomRole.Player, RoomExpectation.RoleDenied)]
	[InlineData(RoomRole.Outsider, RoomExpectation.RoleDenied)]
	[InlineData(RoomRole.Manager, RoomExpectation.Allowed)]
	[InlineData(RoomRole.NoRoomJoin, RoomExpectation.RoomJoinDenied)]
	[InlineData(RoomRole.SuspendedCreator, RoomExpectation.RoomJoinDenied)]
	public async Task RemoveRefereeAsync_TheCallersRoleDecidesWhoMayRemoveAReferee(RoomRole role,
		RoomExpectation expected)
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var room = setup.RoomFor(role);
		var caller = setup.ConnectionFor(role);
		var target = setup.Referee.Record;
		var before = room.Authority.Referees.Contains(target);
		var events = app.RoomEvents.Events.Count;

		var result = await room.ScopeTo(caller).Authority.RemoveRefereeAsync(target,
			TestContext.Current.CancellationToken);

		if (expected == RoomExpectation.Allowed)
		{
			Assert.Equal(RoomOutcome.Ok, result.Outcome);
			Assert.False(room.Authority.Referees.Contains(target));
			Assert.IsType<RoomRefereeRemoved>(app.RoomEvents.Events[^1]);
			return;
		}

		AssertRefused(result, expected);
		Assert.Equal(before, room.Authority.Referees.Contains(target));
		Assert.Equal(events, app.RoomEvents.Events.Count);
	}

	[Theory]
	[InlineData(RoomRole.Creator, RoomExpectation.Allowed)]
	[InlineData(RoomRole.Referee, RoomExpectation.Allowed)]
	[InlineData(RoomRole.Host, RoomExpectation.Allowed)]
	[InlineData(RoomRole.Player, RoomExpectation.RoleDenied)]
	[InlineData(RoomRole.Outsider, RoomExpectation.RoleDenied)]
	[InlineData(RoomRole.Manager, RoomExpectation.Allowed)]
	[InlineData(RoomRole.NoRoomJoin, RoomExpectation.RoomJoinDenied)]
	[InlineData(RoomRole.SuspendedCreator, RoomExpectation.RoomJoinDenied)]
	public async Task SetHostAsync_TheCallersRoleDecidesWhoMayGiveTheHost(RoomRole role, RoomExpectation expected)
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var room = setup.RoomFor(role);
		var caller = setup.ConnectionFor(role);
		var before = room.Authority.Host;
		var events = app.RoomEvents.Events.Count;

		var result = await room.ScopeTo(caller).Authority.SetHostAsync(setup.Player,
			TestContext.Current.CancellationToken);

		if (expected == RoomExpectation.Allowed)
		{
			Assert.Equal(RoomOutcome.Ok, result.Outcome);
			Assert.Same(setup.Player, room.Authority.Host);
			Assert.IsType<RoomHostChanged>(app.RoomEvents.Events[^1]);
			return;
		}

		AssertRefused(result, expected);
		Assert.Same(before, room.Authority.Host);
		Assert.Equal(events, app.RoomEvents.Events.Count);
	}

	[Theory]
	[InlineData(RoomRole.Creator, RoomExpectation.Allowed)]
	[InlineData(RoomRole.Referee, RoomExpectation.Allowed)]
	[InlineData(RoomRole.Host, RoomExpectation.RoleDenied)]
	[InlineData(RoomRole.Player, RoomExpectation.RoleDenied)]
	[InlineData(RoomRole.Outsider, RoomExpectation.RoleDenied)]
	[InlineData(RoomRole.Manager, RoomExpectation.Allowed)]
	[InlineData(RoomRole.NoRoomJoin, RoomExpectation.RoomJoinDenied)]
	[InlineData(RoomRole.SuspendedCreator, RoomExpectation.RoomJoinDenied)]
	public async Task CloseAsync_TheCallersRoleDecidesWhoMayCloseTheRoom(RoomRole role, RoomExpectation expected)
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var room = setup.RoomFor(role);
		var caller = setup.ConnectionFor(role);
		var events = app.RoomEvents.Events.Count;
		var lobbyEvents = app.LobbyEvents.Events.Count;

		var result = await room.ScopeTo(caller).CloseAsync(TestContext.Current.CancellationToken);

		if (expected == RoomExpectation.Allowed)
		{
			Assert.Equal(RoomOutcome.Ok, result.Outcome);
			Assert.True(room.IsClosed);
			Assert.IsType<LobbyRoomClosed>(app.LobbyEvents.Events[^1]);
			return;
		}

		AssertRefused(result, expected);
		Assert.False(room.IsClosed);
		Assert.Equal(events, app.RoomEvents.Events.Count);
		Assert.Equal(lobbyEvents, app.LobbyEvents.Events.Count);
	}

	#region Outcomes about the target

	[Fact]
	public async Task AddRefereeAsync_ForTheCreator_IsRefused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Creator).Authority.AddRefereeAsync(setup.Creator.Record,
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.IsCreator, result.Outcome);
		Assert.DoesNotContain(setup.Creator.Record, setup.Room.Authority.Referees);
	}

	[Fact]
	public async Task AddRefereeAsync_ForAnExistingReferee_IsRefused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Creator).Authority.AddRefereeAsync(setup.Referee.Record,
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.AlreadyReferee, result.Outcome);
	}

	[Fact]
	public async Task AddRefereeAsync_ForTheNinthReferee_IsRefused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		for (var index = 1; index < RoomAuthority.MaxReferees; index++)
		{
			var user = await RoomFixture.PlayerAsync(app, $"Referee{index}");
			Assert.True((await setup.Room.ScopeTo(setup.Creator).Authority.AddRefereeAsync(user.Record,
				TestContext.Current.CancellationToken)).IsOk);
		}

		var ninth = await RoomFixture.PlayerAsync(app, "Ninth");
		var result = await setup.Room.ScopeTo(setup.Creator).Authority.AddRefereeAsync(ninth.Record,
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.TooManyReferees, result.Outcome);
		Assert.Equal(RoomAuthority.MaxReferees, setup.Room.Authority.Referees.Count);
	}

	[Fact]
	public async Task RemoveRefereeAsync_ForSomebodyWhoIsNotAReferee_IsRefused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Creator).Authority.RemoveRefereeAsync(setup.Outsider.Record,
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.NotReferee, result.Outcome);
	}

	[Fact]
	public async Task SetHostAsync_ForSomebodyWhoIsNotSeated_IsRefused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Creator).Authority.SetHostAsync(setup.Outsider,
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.NotInRoom, result.Outcome);
		Assert.Same(setup.Host, setup.Room.Authority.Host);
	}

	#endregion

	private static void AssertRefused(Result<RoomOutcome> result, RoomExpectation expected)
	{
		Assert.False(result.IsOk);

		if (expected == RoomExpectation.RoomJoinDenied)
		{
			Assert.Equal(Permissions.RoomJoin, result.Missing);
			return;
		}

		Assert.Equal(RoomOutcome.RoleDenied, result.Outcome);
		Assert.Null(result.Missing);
	}
}