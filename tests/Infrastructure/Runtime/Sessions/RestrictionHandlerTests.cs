using Basil.Application.Runtime.Multiplayer.Events;
using Basil.Application.Runtime.Sessions;
using Basil.Testing;
using Basil.Domain.Permissions;
using Basil.Domain.Users;
using Basil.Infrastructure.Runtime.Sessions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Basil.Infrastructure.Runtime.Tests.Sessions;

/// <summary>
///     What a restriction costs a user in multiplayer: a suspended right to join rooms takes away the seat and the
///     referee entries, while a restriction that does not cover that right, or a manager who needs no such right,
///     leaves them where they are. The expected values come from docs/for-agents/identity-permissions.md.
/// </summary>
public sealed class RestrictionHandlerTests
{
	[Fact]
	public async Task HandleAsync_RestrictedRoomJoin_RemovesTheOnlineUserFromTheirRoom()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var handler = Handler(app);
		var restricted = await RestrictAsync(app, setup.Player.Record, Permissions.RoomJoin);

		await handler.HandleAsync(restricted, TestContext.Current.CancellationToken);

		Assert.Null(setup.Room.Slots.Find(setup.Player));
	}

	[Fact]
	public async Task HandleAsync_RestrictedRoomJoin_RemovesTheOfflineRefereeFromOpenRooms()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var handler = Handler(app);
		app.Users.Close(setup.Referee, ConnectionCloseReason.ConnectionLost);
		var restricted = await RestrictAsync(app, setup.Referee.Record, Permissions.RoomJoin);

		await handler.HandleAsync(restricted, TestContext.Current.CancellationToken);

		Assert.DoesNotContain(setup.Referee.Record, setup.Room.Authority.Referees);
	}

	[Fact]
	public async Task HandleAsync_RestrictedRoomJoin_LeavesAnOfflineManagerOnTheRefereeList()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var handler = Handler(app);
		Assert.True((await setup.Room.ScopeTo(setup.Creator).Authority
			.AddRefereeAsync(setup.Manager.Record, TestContext.Current.CancellationToken)).IsOk);
		app.Users.Close(setup.Manager, ConnectionCloseReason.ConnectionLost);
		var restricted = await RestrictAsync(app, setup.Manager.Record, Permissions.RoomJoin);

		await handler.HandleAsync(restricted, TestContext.Current.CancellationToken);

		Assert.Contains(setup.Manager.Record, setup.Room.Authority.Referees);
	}

	[Fact]
	public async Task HandleAsync_RestrictedOtherRight_LeavesTheSeatAlone()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		var handler = Handler(app);
		var restricted = await RestrictAsync(app, setup.Player.Record, Permissions.ChatPost);

		await handler.HandleAsync(restricted, TestContext.Current.CancellationToken);

		Assert.NotNull(setup.Room.Slots.Find(setup.Player));
	}

	private static RestrictionHandler Handler(TestApplication app)
	{
		return new RestrictionHandler(app.Users, app.Lobby, app.Clock,
			NullLogger<RestrictionHandler>.Instance);
	}

	/// <summary>Suspends one right of a user as a moderator who outranks everyone and reports the published event.</summary>
	/// <param name="app">The application the users belong to.</param>
	/// <param name="target">The user to restrict.</param>
	/// <param name="permissions">The right to suspend.</param>
	/// <returns>The restriction event the server published.</returns>
	private static async Task<UserRestricted> RestrictAsync(TestApplication app, UserRecord target,
		Permissions permissions)
	{
		var moderator = await app.CreateUserAsync("Moderator", "password", PermissionsGroups.All);
		var by = await app.LoginAsync(moderator);
		var (result, _) = await app.Users.Of(target).ScopeTo(by).Restrictions.RestrictAsync(permissions, null,
			cancellationToken: TestContext.Current.CancellationToken);
		Assert.True(result.IsOk);
		return Assert.Single(app.UserEvents.Events.OfType<UserRestricted>(),
			@event => @event.User == target);
	}
}
