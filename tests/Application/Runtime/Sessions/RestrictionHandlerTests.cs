using Basil.Application.Runtime.Sessions;
using Basil.Domain.Users;
using Basil.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Basil.Application.Runtime.Tests.Sessions;

/// <summary>What a restriction costs a user among the sessions: a suspended right to watch stops their spectating.</summary>
public sealed class RestrictionHandlerTests
{
	[Fact]
	public async Task Handle_RestrictedSpectating_StopsTheUsersSpectating()
	{
		using var app = new TestApplication();
		var host = await RoomFixture.PlayerAsync(app, "Watched");
		var spectator = await RoomFixture.PlayerAsync(app, "Watcher");
		Assert.True((await host.ScopeTo(spectator).SpectateAsync()).IsOk);
		var restricted = await RestrictAsync(app, spectator.Record, UserPermissions.SpectatingWatch);

		await Handler(app).Handle(restricted, TestContext.Current.CancellationToken);

		Assert.DoesNotContain(spectator, host.Spectators);
	}

	[Fact]
	public async Task Handle_RestrictedOtherRight_LeavesTheSpectatingAlone()
	{
		using var app = new TestApplication();
		var host = await RoomFixture.PlayerAsync(app, "Watched");
		var spectator = await RoomFixture.PlayerAsync(app, "Watcher");
		Assert.True((await host.ScopeTo(spectator).SpectateAsync()).IsOk);
		var restricted = await RestrictAsync(app, spectator.Record, UserPermissions.ChatPost);

		await Handler(app).Handle(restricted, TestContext.Current.CancellationToken);

		Assert.Contains(spectator, host.Spectators);
	}

	private static RestrictionHandler Handler(TestApplication app)
	{
		return new RestrictionHandler(app.Users, app.Clock, NullLogger<RestrictionHandler>.Instance);
	}

	private static async Task<UserRestricted> RestrictAsync(TestApplication app, UserRecord target,
		UserPermissions permissions)
	{
		var moderator = await app.CreateUserAsync("Moderator", "password", UserPermissionsGroups.All);
		var by = await app.LoginAsync(moderator);
		var (result, _) = await app.Users.Of(target).ScopeTo(by).Restrictions.RestrictAsync(permissions, null,
			cancellationToken: TestContext.Current.CancellationToken);
		Assert.True(result.IsOk);
		return Assert.Single(app.UserEvents.Events.OfType<UserRestricted>(), @event => @event.User == target);
	}
}