using Basil.Application.Servers.Objects;
using Basil.Application.Shared.Privileges;
using Basil.Application.Users.Objects.Sessions;
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

	public (ServerActionResult Result, int Sent) Notify(string text, UserRecord to)
	{
		return Denied() is { } denied ? (denied, 0) : (ServerActionResult.Ok, Server.Notify(text, to));
	}

	public (ServerActionResult Result, int Sent) Notify(string text, IReadOnlyCollection<int> to)
	{
		return Denied() is { } denied ? (denied, 0) : (ServerActionResult.Ok, Server.Notify(text, to));
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