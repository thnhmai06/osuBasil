using Basil.Application.Services.Contracts.Chat;
using Basil.Application.Services.Contracts.Sessions;
using Basil.Domain.Chat;

namespace Basil.Application.Services.Contracts.Servers;

/// <summary>The server-wide operations and resources that do not belong to one account or room.</summary>
public sealed class Server
{
	/// <summary>Creates the server and its runtime dependencies.</summary>
	/// <param name="context">The ports through which the server performs its behaviour.</param>
	internal Server(ServerContext context)
	{
		Context = context;
	}

	/// <summary>The runtime context for server behaviour.</summary>
	internal ServerContext Context { get; }

	/// <summary>Scopes server operations to the connection that asks for them.</summary>
	/// <param name="by">The connection acting on the server.</param>
	/// <returns>The server operations available to that connection.</returns>
	public ScopedServer ScopeTo(Connection by)
	{
		return new ScopedServer(this, by);
	}

	/// <summary>Opens a general channel at the server's own request (startup); no rights are needed.</summary>
	/// <param name="channel">The general channel to open.</param>
	/// <returns>Null when a channel with that name is already open.</returns>
	public GeneralChannelSession? OpenChannel(GeneralChannel channel)
	{
		var session = new GeneralChannelSession(channel, Context.Session.Chat);
		if (!Context.Session.Chat.Registry.Add(session)) return null;
		Context.Session.Chat.Events.Emit(new ChannelOpened(session));
		return session;
	}
}
