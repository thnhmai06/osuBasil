using Basil.Application.Servers.Objects;
using Basil.Application.Shared.Privileges;
using Basil.Application.Sessions.Objects;
using Basil.Domain.Users;

namespace Basil.Application.Servers.Capabilities;

public readonly struct ScopedServer
{
	public Session Actor { get; }

	public UserPrivileges Privileges { get; }

	internal bool Lacks(UserPrivileges required)
	{
		return ScopedPrivileges.Lacks(Privileges, required);
	}

	internal ScopedServer(Server server, Session actor)
	{
		Actor = actor;
		Privileges = actor.EffectivePrivileges(server.Context.Connections.Clock.GetUtcNow());
		Server = server;
	}

	public Server Server { get; }

	public ScopedServerChannels Channels => new(this);

	public ScopedServerSettings Settings => new(this);

	public ScopedServerFaq Faq => new(this);

	public ScopedServerBanners Banners => new(this);

	public ScopedServerSeasonals Seasonals => new(this);

	public ScopedServerIcon Icon => new(this);

	public ScopedServerScreenshots Screenshots => new(this);

	public ScopedServerBeatmapsets Beatmapsets => new(this);

	public ServerActionResult MayChangeMenu()
	{
		return Lacks(UserPrivileges.Owner) ? ServerActionResult.Denied : ServerActionResult.Ok;
	}

	public ServerActionResult MayAnnounce()
	{
		return Denied() ?? ServerActionResult.Ok;
	}

	public (ServerActionResult Result, int Sent) Notify(string text, UserRecord to)
	{
		return Denied() is { } denied ? (denied, 0) : (ServerActionResult.Ok, Server.Notify(text, to));
	}

	public async Task<(ServerActionResult Result, int Sent, IReadOnlyList<int> Missing)> NotifyAsync(string text,
		IReadOnlyCollection<int> to, CancellationToken ct = default)
	{
		if (Denied() is { } denied) return (denied, 0, []);

		var (sent, missing) = await Server.NotifyAsync(text, to, ct);
		return (ServerActionResult.Ok, sent, missing);
	}

	public (ServerActionResult Result, int Sent) NotifyAll(string text)
	{
		return Denied() is { } denied ? (denied, 0) : (ServerActionResult.Ok, Server.NotifyAll(text));
	}

	private ServerActionResult? Denied()
	{
		return Lacks(UserPrivileges.Moderator)
			? ServerActionResult.Denied
			: null;
	}
}