using Basil.Application.Common;
using Basil.Application.Users.Objects;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Domain.Mechanics;
using Basil.Domain.Users;
using Basil.Testing;

namespace Basil.Application.Multiplayer.Tests.Capabilities.Rooms;

/// <summary>What a caller is expected to get from a room operation.</summary>
public enum RoomExpectation
{
	/// <summary>The operation succeeds and changes the room.</summary>
	Allowed,

	/// <summary>The caller lacks the room privilege; the outcome names the role refusal.</summary>
	RoleDenied,

	/// <summary>The caller may not join rooms at all; the denial names the missing permission.</summary>
	RoomJoinDenied
}

/// <summary>
///     Who may do what in a room. The expected privileges come from AGENTS.md ("Authority") and
///     docs/for-developers/multiplayer.md ("Room roles and privileges"): the creator holds every room privilege, a
///     referee holds every privilege except managing referees, the host holds the in-client host operations, a player
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
	public async Task MoveAsync_RoleDecidesMove(RoomRole role,
		RoomExpectation expected)
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var room = setup.RoomFor(role);
		var caller = setup.ConnectionFor(role);
		var target = setup.Player;
		var before = room.Slots.Find(target.Record)?.Index;
		var events = app.RoomEvents.Events.Count;

		var result = await room.ScopeTo(caller).Slots.MoveAsync(UserReference.ById(target.Record.Id), 5);

		if (expected == RoomExpectation.Allowed)
		{
			Assert.Equal(RoomOutcome.Ok, result);
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
	public async Task ArrangeAsync_RoleDecidesArranging(RoomRole role,
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

		var result = await room.ScopeTo(caller).Slots.ArrangeAsync(arrangement);

		if (expected == RoomExpectation.Allowed)
		{
			Assert.Equal(RoomOutcome.Ok, result);
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
	public async Task SetTeamAsync_RoleDecidesTeams(RoomRole role,
		RoomExpectation expected)
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var room = setup.RoomFor(role);
		var caller = setup.ConnectionFor(role);
		var target = setup.Player;
		await room.ScopeTo(setup.Manager).Settings.ConfigureAsync(
			new RoomSettingsChange(TeamType: GameTeamType.TeamVs));
		var before = room.Slots.Find(target.Record)?.Team;
		var events = app.RoomEvents.Events.Count;

		var result = await room.ScopeTo(caller).Slots.SetTeamAsync(UserReference.ById(target.Record.Id), GameTeam.Blue);

		if (expected == RoomExpectation.Allowed)
		{
			Assert.Equal(RoomOutcome.Ok, result);
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
	public async Task ToggleLockAsync_RoleSlotLock(RoomRole role,
		RoomExpectation expected)
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var room = setup.RoomFor(role);
		var caller = setup.ConnectionFor(role);
		var before = room.Slots.At(6)!.Locked;
		var events = app.RoomEvents.Events.Count;

		var result = await room.ScopeTo(caller).Slots.ToggleLockAsync(6);

		if (expected == RoomExpectation.Allowed)
		{
			Assert.Equal(RoomOutcome.Ok, result);
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
	public async Task SetLockedAsync_RoleRoomLock(RoomRole role,
		RoomExpectation expected)
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var room = setup.RoomFor(role);
		var caller = setup.ConnectionFor(role);
		var before = room.Slots.Locked;
		var events = app.RoomEvents.Events.Count;

		var result = await room.ScopeTo(caller).Settings.SetLockedAsync(true);

		if (expected == RoomExpectation.Allowed)
		{
			Assert.Equal(RoomOutcome.Ok, result);
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
	public async Task ConfigureAsync_RoleDecidesSettings(RoomRole role,
		RoomExpectation expected)
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var room = setup.RoomFor(role);
		var caller = setup.ConnectionFor(role);
		var before = room.Settings.Name;
		var events = app.RoomEvents.Events.Count;

		var result = await room.ScopeTo(caller).Settings.ConfigureAsync(new RoomSettingsChange("renamed"));

		if (expected == RoomExpectation.Allowed)
		{
			Assert.Equal(RoomOutcome.Ok, result);
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
	public async Task StartAsync_RoleDecidesStart(RoomRole role, RoomExpectation expected)
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var room = setup.RoomFor(role);
		var caller = setup.ConnectionFor(role);
		await room.ScopeTo(setup.Manager).Settings.ConfigureAsync(
			new RoomSettingsChange(Beatmap: RoomFixture.Beatmap));
		var before = room.Rounds.LastRound;
		var events = app.RoomEvents.Events.Count;

		var result = await room.ScopeTo(caller).Rounds.StartAsync();

		if (expected == RoomExpectation.Allowed)
		{
			Assert.Equal(RoomOutcome.Ok, result);
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
	public async Task RequireStart_RoleDecidesRounds(RoomRole role,
		RoomExpectation expected)
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var room = setup.RoomFor(role);
		var caller = setup.ConnectionFor(role);

		var result = room.ScopeTo(caller).Rounds.RequireStart();

		if (expected == RoomExpectation.Allowed)
		{
			Assert.Null(result);
			return;
		}

		AssertRefused(result!.Value, expected);
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
	public async Task AbortAsync_RoleDecidesAbort(RoomRole role, RoomExpectation expected)
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var room = setup.RoomFor(role);
		var caller = setup.ConnectionFor(role);
		await room.ScopeTo(setup.Manager).Settings.ConfigureAsync(
			new RoomSettingsChange(Beatmap: RoomFixture.Beatmap));
		await room.ScopeTo(setup.Manager).Rounds.StartAsync();
		var before = room.Rounds.CurrentRound;
		var events = app.RoomEvents.Events.Count;

		var result = await room.ScopeTo(caller).Rounds.AbortAsync();

		if (expected == RoomExpectation.Allowed)
		{
			Assert.Equal(RoomOutcome.Ok, result);
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
	[InlineData(RoomRole.Host, RoomExpectation.Allowed)]
	[InlineData(RoomRole.Player, RoomExpectation.RoleDenied)]
	[InlineData(RoomRole.Outsider, RoomExpectation.RoleDenied)]
	[InlineData(RoomRole.Manager, RoomExpectation.Allowed)]
	[InlineData(RoomRole.NoRoomJoin, RoomExpectation.RoomJoinDenied)]
	[InlineData(RoomRole.SuspendedCreator, RoomExpectation.RoomJoinDenied)]
	public async Task KickAsync_RoleDecidesKick(RoomRole role, RoomExpectation expected)
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var room = setup.RoomFor(role);
		var caller = setup.ConnectionFor(role);
		var target = setup.Player;
		var before = room.Slots.Find(target.Record)?.Index;
		var events = app.RoomEvents.Events.Count;

		var result = await room.ScopeTo(caller).Members.KickAsync(UserReference.ById(target.Record.Id));

		if (expected == RoomExpectation.Allowed)
		{
			Assert.Equal(RoomOutcome.Ok, result);
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
	public async Task BanAsync_RoleDecidesBan(RoomRole role, RoomExpectation expected)
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var room = setup.RoomFor(role);
		var caller = setup.ConnectionFor(role);
		var target = setup.Player;
		var before = room.Members.Banned.Contains(target.Record);
		var events = app.RoomEvents.Events.Count;

		var result = await room.ScopeTo(caller).Members.BanAsync(UserReference.ById(target.Record.Id));

		if (expected == RoomExpectation.Allowed)
		{
			Assert.Equal(RoomOutcome.Ok, result);
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
	public async Task UnbanAsync_RoleDecidesLift(RoomRole role, RoomExpectation expected)
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var room = setup.RoomFor(role);
		var caller = setup.ConnectionFor(role);
		var target = setup.Player;
		await room.ScopeTo(setup.Manager).Members.BanAsync(UserReference.ById(target.Record.Id));
		var before = room.Members.Banned.Contains(target.Record);
		var events = app.RoomEvents.Events.Count;

		var result = await room.ScopeTo(caller).Members
			.UnbanAsync(UserReference.ById(target.Record.Id));

		if (expected == RoomExpectation.Allowed)
		{
			Assert.Equal(RoomOutcome.Ok, result);
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
	public async Task InviteAsync_RoleDecidesInvite(RoomRole role, RoomExpectation expected)
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var room = setup.RoomFor(role);
		var caller = setup.ConnectionFor(role);
		var target = app.Users.Of(setup.Outsider.Record);
		var events = app.RoomEvents.Events.Count;

		var result = await room.ScopeTo(caller).Members.InviteAsync(target);

		if (expected == RoomExpectation.Allowed)
		{
			Assert.Equal(RoomOutcome.Ok, result);
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
	public async Task AddRefereeAsync_RoleDecidesReferee(RoomRole role,
		RoomExpectation expected)
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var room = setup.RoomFor(role);
		var caller = setup.ConnectionFor(role);
		var target = setup.Outsider.Record;
		var before = room.Authority.Referees.Contains(target);
		var events = app.RoomEvents.Events.Count;

		var result = await room.ScopeTo(caller).Authority.AddRefereeAsync(UserReference.ById(target.Id));

		if (expected == RoomExpectation.Allowed)
		{
			Assert.Equal(RoomOutcome.Ok, result);
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
	public async Task RemoveRefereeAsync_RoleDecidesRemoval(RoomRole role,
		RoomExpectation expected)
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var room = setup.RoomFor(role);
		var caller = setup.ConnectionFor(role);
		var target = setup.Referee.Record;
		var before = room.Authority.Referees.Contains(target);
		var events = app.RoomEvents.Events.Count;

		var result = await room.ScopeTo(caller).Authority.RemoveRefereeAsync(UserReference.ById(target.Id));

		if (expected == RoomExpectation.Allowed)
		{
			Assert.Equal(RoomOutcome.Ok, result);
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
	public async Task SetHostAsync_RoleDecidesHost(RoomRole role, RoomExpectation expected)
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var room = setup.RoomFor(role);
		var caller = setup.ConnectionFor(role);
		var before = room.Authority.Host;
		var events = app.RoomEvents.Events.Count;

		var result = await room.ScopeTo(caller).Authority.SetHostAsync(setup.Player);

		if (expected == RoomExpectation.Allowed)
		{
			Assert.Equal(RoomOutcome.Ok, result);
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
	public async Task CloseAsync_RoleDecidesClose(RoomRole role, RoomExpectation expected)
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var room = setup.RoomFor(role);
		var caller = setup.ConnectionFor(role);
		var events = app.RoomEvents.Events.Count;
		var lobbyEvents = app.LobbyEvents.Events.Count;

		var result = await room.ScopeTo(caller).CloseAsync();

		if (expected == RoomExpectation.Allowed)
		{
			Assert.Equal(RoomOutcome.Ok, result);
			Assert.True(room.IsClosed);
			Assert.IsType<LobbyRoomClosed>(app.LobbyEvents.Events[^1]);
			return;
		}

		AssertRefused(result, expected);
		Assert.False(room.IsClosed);
		Assert.Equal(events, app.RoomEvents.Events.Count);
		Assert.Equal(lobbyEvents, app.LobbyEvents.Events.Count);
	}

	private static void AssertRefused(RoomOutcome result, RoomExpectation expected)
	{
		Assert.False(result == RoomOutcome.Ok);

		if (expected == RoomExpectation.RoomJoinDenied)
		{
			Assert.Equal(RoomOutcome.Denied, result);
			return;
		}

		Assert.Equal(RoomOutcome.RoleDenied, result);
		Assert.NotEqual(RoomOutcome.Denied, result);
	}

	#region Outcomes about the target

	[Fact]
	public async Task ForTheCreator_IsRefused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Creator).Authority.AddRefereeAsync(UserReference.ById(setup.Creator.Record.Id));

		Assert.Equal(RoomOutcome.IsCreator, result);
		Assert.DoesNotContain(setup.Creator.Record, setup.Room.Authority.Referees);
	}

	[Fact]
	public async Task AddRefereeAsync_ExistingRefereeRefused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Creator).Authority.AddRefereeAsync(UserReference.ById(setup.Referee.Record.Id));

		Assert.Equal(RoomOutcome.AlreadyReferee, result);
	}

	[Fact]
	public async Task AddRefereeAsync_NinthRefereeRefused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		for (var index = 1; index < RoomAuthority.MaxReferees; index++)
		{
			var user = await RoomFixture.PlayerAsync(app, $"Referee{index}");
			Assert.True((await setup.Room.ScopeTo(setup.Creator).Authority.AddRefereeAsync(UserReference.ById(user.Record.Id))) == RoomOutcome.Ok);
		}

		var ninth = await RoomFixture.PlayerAsync(app, "Ninth");
		var result = await setup.Room.ScopeTo(setup.Creator).Authority.AddRefereeAsync(UserReference.ById(ninth.Record.Id));

		Assert.Equal(RoomOutcome.TooManyReferees, result);
		Assert.Equal(RoomAuthority.MaxReferees, setup.Room.Authority.Referees.Count);
	}

	[Fact]
	public async Task RemoveRefereeAsync_NonRefereeRefused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Creator).Authority.RemoveRefereeAsync(UserReference.ById(setup.Outsider.Record.Id));

		Assert.Equal(RoomOutcome.NotReferee, result);
	}

	[Fact]
	public async Task SetHostAsync_UnseatedRefused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Creator).Authority.SetHostAsync(setup.Outsider);

		Assert.Equal(RoomOutcome.NotInRoom, result);
		Assert.Same(setup.Host, setup.Room.Authority.Host);
	}

	#endregion
}