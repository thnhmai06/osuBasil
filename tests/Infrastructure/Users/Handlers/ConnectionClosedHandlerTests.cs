using System.Diagnostics.Metrics;
using Basil.Application.Authentication.Persistence;
using Basil.Application.Users.Objects.Events;
using Basil.Domain.Authentication;
using Basil.Infrastructure.Users.Handlers;
using Basil.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Basil.Infrastructure.Users.Tests.Handlers;

/// <summary>What a closed connection leaves behind in the trail of its session.</summary>
[Collection("SessionFailureMeter")]
public sealed class ConnectionClosedHandlerTests
{
	[Fact]
	public async Task UserConnectionClosed_EndStored()
	{
		using var app = new TestApplication();
		var connection = await app.LoginAsync(await app.CreateUserAsync("Leaver", "password"), SessionType.Stable);
		app.Clock.Advance(TimeSpan.FromSeconds(7));

		app.Users.Close(connection, SessionEndReason.ConnectionLost);

		var entry = Assert.Single(app.StoredConnections);
		Assert.Equal(app.Clock.GetUtcNow(), entry.Value.ClosedAt);
		Assert.Equal(SessionEndReason.ConnectionLost, entry.Value.CloseReason);
	}

	[Fact]
	public async Task ServerConnection_StoresNothing()
	{
		using var app = new TestApplication();
		await app.SeedBotAsync();
		await app.Server.RecoverAsync(TestContext.Current.CancellationToken);
		var bot = app.Users.Online.Values.Single().Connections.First();
		var connections = Substitute.For<IConnectionRepository>();

		await new ConnectionClosedHandler(connections, app.Clock, NullLogger<ConnectionClosedHandler>.Instance)
			.Handle(new UserConnectionClosed(bot, SessionEndReason.LoggedOut, false),
				TestContext.Current.CancellationToken);

		await connections.DidNotReceiveWithAnyArgs().CloseAsync(default, default, default, default);
	}

	[Fact]
	public async Task UnstorableClosing_CountedNoThrow()
	{
		using var app = new TestApplication();
		var connection = await app.LoginAsync(await app.CreateUserAsync("Unstored", "password"), SessionType.Stable);
		app.Users.Close(connection, SessionEndReason.ConnectionLost);
		var closed = Assert.Single(app.UserEvents.Events.OfType<UserConnectionClosed>());
		var connections = Substitute.For<IConnectionRepository>();
		connections.CloseAsync(default, default, default, default).ReturnsForAnyArgs(
			Task.FromException(new InvalidOperationException()));
		var counter = SessionEndHandler.UpdateFailures;
		long failures = 0;
		using var listener = new MeterListener();
		listener.InstrumentPublished = (instrument, meterListener) =>
		{
			if (ReferenceEquals(instrument, counter))
				meterListener.EnableMeasurementEvents(instrument);
		};
		listener.SetMeasurementEventCallback<long>((_, value, _, _) => failures += value);
		listener.Start();

		await new ConnectionClosedHandler(connections, app.Clock, NullLogger<ConnectionClosedHandler>.Instance)
			.Handle(closed, TestContext.Current.CancellationToken);

		Assert.Equal(1, failures);
	}
}