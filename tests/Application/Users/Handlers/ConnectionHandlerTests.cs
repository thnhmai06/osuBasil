using Basil.Application.Users.Handlers;
using Basil.Application.Users.Objects.Events;
using Basil.Domain.Auth;
using Basil.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Basil.Application.Users.Tests.Handlers;

/// <summary>What a closed connection leaves behind among the sessions: it stops spectating.</summary>
public sealed class ConnectionHandlerTests
{
	[Fact]
	public async Task Handle_UserConnectionClosed_StopsSpectating()
	{
		using var app = new TestApplication();
		var host = await RoomFixture.PlayerAsync(app, "Watched");
		var spectator = await RoomFixture.PlayerAsync(app, "Watcher");
		Assert.True((await host.ScopeTo(spectator).SpectateAsync()).IsOk);
		Assert.Contains(spectator, host.Spectators);
		app.Users.Close(spectator, SessionEndReason.ConnectionLost);
		var closed = Assert.Single(app.UserEvents.Events.OfType<UserConnectionClosed>(),
			@event => @event.Connection == spectator);

		await new ConnectionHandler(app.Users, NullLogger<ConnectionHandler>.Instance).Handle(closed,
			TestContext.Current.CancellationToken);

		Assert.DoesNotContain(spectator, host.Spectators);
	}
}