using Basil.Application.Sessions;
using Basil.Domain.Users;
using Basil.Infrastructure.Sessions;

namespace Basil.Infrastructure.Tests.Sessions;

/// <summary>
///     Covers <see cref="GameSessionRegistry" />. A UserId may hold at most one
///     <see cref="GameSession" /> at a time; the registry is keyed by login token with UserId and
///     SafeName indices, and <c>TryAdd</c> is the atomic gate that decides the single winner between
///     racing logins.
/// </summary>
public class GameSessionRegistryTests
{
	private static GameSession MakeGame(int id, string name, string token)
	{
		return new GameSession(id, name, token, UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
	}

	[Fact]
	public void TryAdd_FirstForUserId_ReturnsTrue()
	{
		var registry = new GameSessionRegistry();

		Assert.True(registry.TryAdd(MakeGame(1, "cmyui", "token-1")));
	}

	[Fact]
	public void TryAdd_SecondForSameUserId_ReturnsFalse()
	{
		var registry = new GameSessionRegistry();
		registry.TryAdd(MakeGame(1, "cmyui", "token-1"));

		Assert.False(registry.TryAdd(MakeGame(1, "cmyui", "token-2")));
	}

	[Fact]
	public void GetByToken_AfterAdd_ReturnsSession()
	{
		var registry = new GameSessionRegistry();
		var session = MakeGame(1, "cmyui", "token-1");
		registry.TryAdd(session);

		Assert.Same(session, registry.GetByToken("token-1"));
	}

	[Fact]
	public void GetByToken_Unknown_ReturnsNull()
	{
		var registry = new GameSessionRegistry();

		Assert.Null(registry.GetByToken("nonexistent"));
	}

	[Fact]
	public void GetByUserId_ReturnsSession()
	{
		var registry = new GameSessionRegistry();
		var session = MakeGame(42, "cmyui", "token-1");
		registry.TryAdd(session);

		Assert.Same(session, registry.GetByUserId(42));
	}

	[Fact]
	public void GetByUserId_Unknown_ReturnsNull()
	{
		var registry = new GameSessionRegistry();

		Assert.Null(registry.GetByUserId(42));
	}

	[Fact]
	public void GetByName_IsCaseInsensitiveViaSafeName()
	{
		var registry = new GameSessionRegistry();
		var session = MakeGame(1, "Cool Guy", "token-1");
		registry.TryAdd(session);

		Assert.Same(session, registry.GetByName("cool guy"));
		Assert.Same(session, registry.GetByName("COOL_GUY"));
	}

	[Fact]
	public void Remove_SessionNoLongerFound()
	{
		var registry = new GameSessionRegistry();
		var session = MakeGame(1, "cmyui", "token-1");
		registry.TryAdd(session);

		registry.Remove(session);

		Assert.Null(registry.GetByToken("token-1"));
		Assert.Null(registry.GetByUserId(1));
		Assert.Null(registry.GetByName("cmyui"));
	}

	[Fact]
	public void Remove_StaleSessionAfterReplacement_DoesNotRemoveNewSession()
	{
		// A late-arriving cleanup for a session that was already replaced must not evict the
		// session that replaced it — Remove only acts when the registry still holds that exact
		// instance under that token.
		var registry = new GameSessionRegistry();
		var stale = MakeGame(1, "cmyui", "token-1");
		registry.TryAdd(stale);
		registry.Remove(stale);
		var fresh = MakeGame(1, "cmyui", "token-2");
		registry.TryAdd(fresh);

		registry.Remove(stale);

		Assert.Same(fresh, registry.GetByUserId(1));
	}

	[Fact]
	public void All_ReturnsEveryRegisteredSession()
	{
		var registry = new GameSessionRegistry();
		registry.TryAdd(MakeGame(1, "a", "t1"));
		registry.TryAdd(MakeGame(2, "b", "t2"));

		Assert.Equal(2, registry.All.Count());
	}

	[Fact]
	public async Task TryAdd_ConcurrentSameUserId_ExactlyOneSucceeds()
	{
		var registry = new GameSessionRegistry();
		const int attempts = 50;

		var results = await Task.WhenAll(Enumerable.Range(0, attempts)
			.Select(i => Task.Run(() => registry.TryAdd(MakeGame(1, "cmyui", $"token-{i}")))));

		Assert.Equal(1, results.Count(r => r));
		Assert.Single(registry.All);
	}

	[Fact]
	public async Task TryAdd_IsThreadSafe_AllDistinctUsersPresentAfterConcurrentAdds()
	{
		var registry = new GameSessionRegistry();
		const int count = 100;

		await Task.WhenAll(Enumerable.Range(0, count).Select(i =>
			Task.Run(() => registry.TryAdd(MakeGame(i, $"player{i}", $"token-{i}")))));

		Assert.Equal(count, registry.All.Count());
	}
}