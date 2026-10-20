using Basil.Application.Objects.Chat;
using Basil.Application.Objects.Sessions;
using Basil.Domain.Chat;
using Basil.Domain.Users;

namespace Basil.Application.Objects.Servers;

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
		Context.Session.Chat.Events.Publish(new ChannelOpened(session));
		return session;
	}

	/// <summary>Sends a message to an open channel as BasilBot.</summary>
	/// <param name="channel">The channel to speak in.</param>
	/// <param name="text">The message text.</param>
	/// <param name="notice">Whether the message is a notice.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>
	///     <see langword="true" /> when the message was posted; otherwise, <see langword="false" /> when the channel is
	///     closed, the text is empty or whitespace, or BasilBot's record is unavailable.
	/// </returns>
	/// <remarks>
	///     This system operation needs no rights because the server itself calls it. Chat commands must use the scope of
	///     the connection that typed them, not this method, to act for a user.
	/// </remarks>
	public async Task<bool> SayAsync(ChannelSession channel, string text, bool notice = false,
		CancellationToken ct = default)
	{
		if (channel.IsClosed || string.IsNullOrWhiteSpace(text)) return false;

		var bot = await Context.Users.GetAsync(BasilBot.Id, ct);
		if (bot is null || channel.IsClosed) return false;

		channel.PostMessage(bot, text, notice, Context.Session.Clock.GetUtcNow(), null);
		return true;
	}

	/// <summary>Sends a private message to an online user as BasilBot.</summary>
	/// <param name="to">The online recipient.</param>
	/// <param name="text">The message text.</param>
	/// <param name="notice">Whether the message is a notice.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns><see langword="true" /> when the message was posted; otherwise, <see langword="false" />.</returns>
	/// <remarks>
	///     This system operation needs no rights because the server itself calls it. Chat commands must use the scope of
	///     the connection that typed them, not this method, to act for a user. Messages are not delivered offline.
	/// </remarks>
	public Task<bool> WhisperAsync(UserSession to, string text, bool notice = false, CancellationToken ct = default)
	{
		return SayAsync(to.PmChannel, text, notice, ct);
	}

	/// <summary>Finds an open channel the bot may speak in by the name clients use.</summary>
	/// <param name="name">The channel name.</param>
	/// <returns>The open channel, or <see langword="null" /> when no matching channel is open.</returns>
	/// <remarks>
	///     This system operation needs no rights because the server itself calls it. Chat commands must not use it to
	///     act for a user.
	/// </remarks>
	public ChannelSession? FindChannel(string name)
	{
		if (Context.Session.Chat.Registry.All.TryGetValue(name, out var general))
			return general.IsClosed ? null : general;

		if (!name.StartsWith("#mp_", StringComparison.Ordinal)
		    || !int.TryParse(name.AsSpan(4), out var roomId)
		    || !Context.Lobby.Rooms.TryGetValue(roomId, out var room)
		    || room.Channel.IsClosed)
			return null;

		return room.Channel;
	}
}