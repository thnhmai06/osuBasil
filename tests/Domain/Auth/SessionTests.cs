using Basil.Domain.Auth;
using Basil.Domain.Users;

namespace Basil.Domain.Tests.Auth;

public sealed class SessionTests
{
	[Fact]
	public void MarkActive_EndSession_RecordsLifecycleAndStopsFurtherActivity()
	{
		var startedAt = new DateTimeOffset(2026, 10, 28, 10, 0, 0, TimeSpan.Zero);
		var activeAt = startedAt.AddMinutes(5);
		var endedAt = activeAt.AddMinutes(1);
		var session = new Session
		{
			Id = Guid.Parse("9e0d20f2-3630-4308-a500-055e9120da77"),
			User = new UserRecord { Id = 2, Value = new UserData { Name = "session_user" } },
			Type = ConnectionType.Osu,
			Ip = System.Net.IPAddress.Loopback,
			StartedAt = startedAt
		};

		session.MarkActive(activeAt);
		session.End(endedAt, SessionEndReason.LoggedOut);
		session.MarkActive(endedAt.AddMinutes(1));

		Assert.Equal(activeAt, session.LastActiveAt);
		Assert.Equal(endedAt, session.EndedAt);
		Assert.Equal(SessionEndReason.LoggedOut, session.EndReason);
	}
}
