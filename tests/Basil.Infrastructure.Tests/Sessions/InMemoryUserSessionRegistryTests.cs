using Basil.Application.Sessions;
using Basil.Application.Sessions.Irc;
using Basil.Domain.Users;
using Basil.Infrastructure.Sessions;

namespace Basil.Infrastructure.Tests.Sessions;

/// <summary>
///     Covers the dual-session registry: a UserId may hold at most one <see cref="GameSession" /> and
///     at most one <see cref="IrcSession" /> at a time, registered/looked-up independently.
/// </summary>
public class InMemoryUserSessionRegistryTests
{
	private static GameSession MakeGame(int id, string name, string token)
	{
		return new GameSession(id, name, token, UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
	}

	private static IrcSession MakeIrc(int id, string name, string token)
	{
		return new IrcSession(id, name, token, UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch)
		{
			Connection = new FakeIrcConnection()
		};
	}

	[Fact]
	public void TryAddGameSession_FirstForUserId_ReturnsTrue()
	{
		var registry = new InMemoryUserSessionRegistry();

		Assert.True(registry.TryAddGameSession(MakeGame(1, "cmyui", "token-1")));
	}

	[Fact]
	public void TryAddGameSession_SecondForSameUserId_ReturnsFalse()
	{
		var registry = new InMemoryUserSessionRegistry();
		registry.TryAddGameSession(MakeGame(1, "cmyui", "token-1"));

		Assert.False(registry.TryAddGameSession(MakeGame(1, "cmyui", "token-2")));
	}

	[Fact]
	public void TryAddIrcSession_SecondForSameUserId_ReturnsFalse()
	{
		var registry = new InMemoryUserSessionRegistry();
		registry.TryAddIrcSession(MakeIrc(1, "cmyui", "irc-1"));

		Assert.False(registry.TryAddIrcSession(MakeIrc(1, "cmyui", "irc-2")));
	}

	[Fact]
	public void TryAddGameSession_AndIrcSession_SameUserId_BothSucceed()
	{
		var registry = new InMemoryUserSessionRegistry();

		Assert.True(registry.TryAddGameSession(MakeGame(1, "cmyui", "token-1")));
		Assert.True(registry.TryAddIrcSession(MakeIrc(1, "cmyui", "irc-1")));
		Assert.Equal(2, registry.GetSessionsByUserId(1).Count);
	}

	[Fact]
	public void GetGameByToken_AfterAdd_ReturnsSession()
	{
		var registry = new InMemoryUserSessionRegistry();
		var session = MakeGame(1, "cmyui", "token-1");
		registry.TryAddGameSession(session);

		Assert.Same(session, registry.GetGameByToken("token-1"));
	}

	[Fact]
	public void GetGameByToken_Unknown_ReturnsNull()
	{
		var registry = new InMemoryUserSessionRegistry();

		Assert.Null(registry.GetGameByToken("nonexistent"));
	}

	[Fact]
	public void GetGameByUserId_ReturnsSession()
	{
		var registry = new InMemoryUserSessionRegistry();
		var session = MakeGame(42, "cmyui", "token-1");
		registry.TryAddGameSession(session);

		Assert.Same(session, registry.GetGameByUserId(42));
	}

	[Fact]
	public void GetGameByUserId_OnlyIrcSessionPresent_ReturnsNull()
	{
		var registry = new InMemoryUserSessionRegistry();
		registry.TryAddIrcSession(MakeIrc(1, "cmyui", "irc-1"));

		Assert.Null(registry.GetGameByUserId(1));
	}

	[Fact]
	public void GetIrcByUserId_OnlyGameSessionPresent_ReturnsNull()
	{
		var registry = new InMemoryUserSessionRegistry();
		registry.TryAddGameSession(MakeGame(1, "cmyui", "token-1"));

		Assert.Null(registry.GetIrcByUserId(1));
	}

	[Fact]
	public void GetGameByName_IsCaseInsensitiveViaSafeName()
	{
		var registry = new InMemoryUserSessionRegistry();
		var session = MakeGame(1, "Cool Guy", "token-1");
		registry.TryAddGameSession(session);

		Assert.Same(session, registry.GetGameByName("cool guy"));
		Assert.Same(session, registry.GetGameByName("COOL_GUY"));
	}

	[Fact]
	public void GetIrcByName_IsCaseInsensitiveViaSafeName()
	{
		var registry = new InMemoryUserSessionRegistry();
		var session = MakeIrc(1, "Cool Guy", "irc-1");
		registry.TryAddIrcSession(session);

		Assert.Same(session, registry.GetIrcByName("cool guy"));
	}

	[Fact]
	public void GetSessionsByUserId_NoSessions_ReturnsEmpty()
	{
		var registry = new InMemoryUserSessionRegistry();

		Assert.Empty(registry.GetSessionsByUserId(999));
	}

	[Fact]
	public void Remove_SessionNoLongerFound()
	{
		var registry = new InMemoryUserSessionRegistry();
		var session = MakeGame(1, "cmyui", "token-1");
		registry.TryAddGameSession(session);

		registry.Remove(session);

		Assert.Null(registry.GetGameByToken("token-1"));
		Assert.Null(registry.GetGameByUserId(1));
	}

	[Fact]
	public void Remove_StaleSessionAfterReplacement_DoesNotRemoveNewSession()
	{
		// A late-arriving cleanup for a session that was already replaced must not evict the
		// session that replaced it — Remove only acts when the registry still holds that exact
		// instance under that token.
		var registry = new InMemoryUserSessionRegistry();
		var stale = MakeGame(1, "cmyui", "token-1");
		registry.TryAddGameSession(stale);
		registry.Remove(stale);
		var fresh = MakeGame(1, "cmyui", "token-2");
		registry.TryAddGameSession(fresh);

		registry.Remove(stale);

		Assert.Same(fresh, registry.GetGameByUserId(1));
	}

	[Fact]
	public void All_ReturnsEverySession()
	{
		var registry = new InMemoryUserSessionRegistry();
		registry.TryAddGameSession(MakeGame(1, "a", "t1"));
		registry.TryAddIrcSession(MakeIrc(2, "b", "irc-2"));

		Assert.Equal(2, registry.All.Count);
	}

	[Fact]
	public void GameSessions_ExcludesIrcSessions()
	{
		var registry = new InMemoryUserSessionRegistry();
		registry.TryAddGameSession(MakeGame(1, "a", "t1"));
		registry.TryAddIrcSession(MakeIrc(2, "b", "irc-2"));

		var games = registry.GameSessions;
		Assert.Single(games);
		Assert.Equal(1, games.Single().Id);
	}

	[Fact]
	public void IrcSessions_ExcludesGameSessions()
	{
		var registry = new InMemoryUserSessionRegistry();
		registry.TryAddGameSession(MakeGame(1, "a", "t1"));
		registry.TryAddIrcSession(MakeIrc(2, "b", "irc-2"));

		var ircs = registry.IrcSessions;
		Assert.Single(ircs);
		Assert.Equal(2, ircs.Single().Id);
	}

	[Fact]
	public async Task TryAddGameSession_ConcurrentSameUserId_ExactlyOneSucceeds()
	{
		var registry = new InMemoryUserSessionRegistry();
		const int attempts = 50;

		var results = await Task.WhenAll(Enumerable.Range(0, attempts)
			.Select(i => Task.Run(() => registry.TryAddGameSession(MakeGame(1, "cmyui", $"token-{i}")))));

		Assert.Equal(1, results.Count(r => r));
		Assert.Single(registry.GameSessions);
	}

	[Fact]
	public async Task TryAddGameSession_IsThreadSafe_AllDistinctUsersPresentAfterConcurrentAdds()
	{
		var registry = new InMemoryUserSessionRegistry();
		const int count = 100;

		await Task.WhenAll(Enumerable.Range(0, count).Select(i =>
			Task.Run(() => registry.TryAddGameSession(MakeGame(i, $"player{i}", $"token-{i}")))));

		Assert.Equal(count, registry.All.Count);
	}

	private sealed class FakeIrcConnection : IIrcConnection
	{
		public UserSession User => throw new NotImplementedException();
		public void Send(Basil.Protocol.Irc.IrcMessage message) { }
	}
}
