using Basil.Application.Sessions;
using Basil.Application.Sessions.Irc;
using Basil.Domain.Users;
using Basil.Infrastructure.Sessions;

namespace Basil.Infrastructure.Tests.Sessions;

/// <summary>
///     Covers <see cref="IrcSessionRegistry" />. A UserId may hold at most one
///     <see cref="IrcSession" /> at a time; the registry is keyed by login token with UserId and
///     SafeName indices, and <c>TryAdd</c> is the atomic gate that decides the single winner between
///     racing logins.
/// </summary>
public class IrcSessionRegistryTests
{
	private static IrcSession MakeIrc(int id, string name, string token)
	{
		return new IrcSession(id, name, token, UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch)
		{
			Connection = new FakeIrcConnection()
		};
	}

	[Fact]
	public void TryAdd_FirstForUserId_ReturnsTrue()
	{
		var registry = new IrcSessionRegistry();

		Assert.True(registry.TryAdd(MakeIrc(1, "cmyui", "irc-1")));
	}

	[Fact]
	public void TryAdd_SecondForSameUserId_ReturnsFalse()
	{
		var registry = new IrcSessionRegistry();
		registry.TryAdd(MakeIrc(1, "cmyui", "irc-1"));

		Assert.False(registry.TryAdd(MakeIrc(1, "cmyui", "irc-2")));
	}

	[Fact]
	public void GetByToken_AfterAdd_ReturnsSession()
	{
		var registry = new IrcSessionRegistry();
		var session = MakeIrc(1, "cmyui", "irc-1");
		registry.TryAdd(session);

		Assert.Same(session, registry.GetByToken("irc-1"));
	}

	[Fact]
	public void GetByUserId_ReturnsSession()
	{
		var registry = new IrcSessionRegistry();
		var session = MakeIrc(42, "cmyui", "irc-1");
		registry.TryAdd(session);

		Assert.Same(session, registry.GetByUserId(42));
	}

	[Fact]
	public void GetByUserId_Unknown_ReturnsNull()
	{
		var registry = new IrcSessionRegistry();

		Assert.Null(registry.GetByUserId(42));
	}

	[Fact]
	public void GetByName_IsCaseInsensitiveViaSafeName()
	{
		var registry = new IrcSessionRegistry();
		var session = MakeIrc(1, "Cool Guy", "irc-1");
		registry.TryAdd(session);

		Assert.Same(session, registry.GetByName("cool guy"));
		Assert.Same(session, registry.GetByName("COOL_GUY"));
	}

	[Fact]
	public void Remove_SessionNoLongerFound()
	{
		var registry = new IrcSessionRegistry();
		var session = MakeIrc(1, "cmyui", "irc-1");
		registry.TryAdd(session);

		registry.Remove(session);

		Assert.Null(registry.GetByToken("irc-1"));
		Assert.Null(registry.GetByUserId(1));
	}

	[Fact]
	public void Remove_StaleSessionAfterReplacement_DoesNotRemoveNewSession()
	{
		var registry = new IrcSessionRegistry();
		var stale = MakeIrc(1, "cmyui", "irc-1");
		registry.TryAdd(stale);
		registry.Remove(stale);
		var fresh = MakeIrc(1, "cmyui", "irc-2");
		registry.TryAdd(fresh);

		registry.Remove(stale);

		Assert.Same(fresh, registry.GetByUserId(1));
	}

	[Fact]
	public void All_ReturnsEveryRegisteredSession()
	{
		var registry = new IrcSessionRegistry();
		registry.TryAdd(MakeIrc(1, "a", "irc-1"));
		registry.TryAdd(MakeIrc(2, "b", "irc-2"));

		Assert.Equal(2, registry.All.Count());
	}

	[Fact]
	public async Task TryAdd_ConcurrentSameUserId_ExactlyOneSucceeds()
	{
		var registry = new IrcSessionRegistry();
		const int attempts = 50;

		var results = await Task.WhenAll(Enumerable.Range(0, attempts)
			.Select(i => Task.Run(() => registry.TryAdd(MakeIrc(1, "cmyui", $"irc-{i}")))));

		Assert.Equal(1, results.Count(r => r));
		Assert.Single(registry.All);
	}

	private sealed class FakeIrcConnection : IIrcConnection
	{
		public UserSession User => throw new NotImplementedException();
		public void Send(Basil.Protocol.Irc.IrcMessage message) { }
	}
}