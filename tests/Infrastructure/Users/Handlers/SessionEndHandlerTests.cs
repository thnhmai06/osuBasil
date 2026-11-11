using System.Diagnostics.Metrics;
using Basil.Application.Authentication.Persistence;
using Basil.Application.Users.Objects.Events;
using Basil.Domain.Authentication;
using Basil.Infrastructure.Users.Handlers;
using Basil.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Basil.Infrastructure.Users.Tests.Handlers;

/// <summary>What a closed connection leaves behind in the session history.</summary>
[Collection("SessionFailureMeter")]
public sealed class SessionEndHandlerTests
{
	[Fact]
	public async Task Handle_UserConnectionClosed_StoresTheEndedSession()
	{
		using var app = new TestApplication();
		var connection = await app.LoginAsync(await app.CreateUserAsync("Leaver", "password"), SessionType.Stable);

		app.Users.Close(connection, SessionEndReason.ConnectionLost);

		Assert.Equal(SessionEndReason.ConnectionLost,
			app.StoredSessions.Single(session => session.Id == connection.Session.Id).Value.End?.Reason);
	}

	[Fact]
	public async Task Handle_WhenTheSessionCannotBeStored_CountsTheFailureAndDoesNotThrow()
	{
		using var app = new TestApplication();
		var connection = await app.LoginAsync(await app.CreateUserAsync("Unstored", "password"), SessionType.Stable);
		app.Users.Close(connection, SessionEndReason.ConnectionLost);
		var closed = Assert.Single(app.UserEvents.Events.OfType<UserConnectionClosed>());
		var sessions = Substitute.For<ISessionRepository>();
		sessions.CreateOrUpdateAsync(default!).ReturnsForAnyArgs(Task.FromException(new InvalidOperationException()));
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

		await new SessionEndHandler(sessions, NullLogger<SessionEndHandler>.Instance).Handle(closed,
			TestContext.Current.CancellationToken);

		Assert.Equal(1, failures);
	}
}