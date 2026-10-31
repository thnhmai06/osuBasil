using Basil.Application.Chat.Registry;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Events;
using Basil.Application.Users.Registry;
using Basil.Domain.Chat;
using Basil.Domain.Users;
using Basil.Application.Servers.Capabilities;
using Basil.Application.Chat.Objects;

namespace Basil.Application.Servers.Objects;

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

	/// <summary>
	///     Recovers the runtime when the server itself calls it at startup: opens the stored general channels, makes
	///     sure BasilBot's account exists and brings it online, and ends the matches left unfinished; it needs no rights.
	/// </summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     What the recovery did: how many general channels were opened, whether BasilBot is online and how many
	///     matches were ended.
	/// </returns>
	public async Task<ServerRecovery> RecoverAsync(CancellationToken cancellationToken = default)
	{
		var openedChannels = 0;
		foreach (var channel in await Context.Channels.ListAsync(cancellationToken))
			if (OpenChannel(channel) is not null)
				openedChannels++;

		var registry = Context.Connections.Registry;
		await registry.EnsureBotAsync(cancellationToken);
		var botOnline = await registry.ConnectBotAsync(cancellationToken) is not null;

		var closedMatches = await Context.Lobby.CloseUnfinishedAsync(cancellationToken);
		return new ServerRecovery(openedChannels, botOnline, closedMatches);
	}

	/// <summary>Opens a general channel at the server's own request (startup); no rights are needed.</summary>
	/// <param name="record">The channel record to open.</param>
	/// <returns>Null when a channel with that name is already open.</returns>
	public GeneralChannel? OpenChannel(GeneralChannelRecord record)
	{
		return Context.Chat.OpenGeneral(record);
	}

	/// <summary>Sends a message to an open channel as BasilBot.</summary>
	/// <param name="channel">The channel to speak in.</param>
	/// <param name="text">The message text.</param>
	/// <param name="notice">Whether the message is a notice.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     <see langword="true" /> when the message was posted; otherwise, <see langword="false" /> when the channel is
	///     closed, the text is empty or holds a control character, or BasilBot's record is unavailable.
	/// </returns>
	/// <remarks>
	///     This system operation needs no rights because the server itself calls it. Chat commands must use the scope of
	///     the connection that typed them, not this method, to act for a user.
	/// </remarks>
	public async Task<bool> SayAsync(Channel channel, string text, bool notice = false,
		CancellationToken cancellationToken = default)
	{
		if (channel.IsClosed || channel is PmChannel { Owner.IsOnline: false } || string.IsNullOrEmpty(text) ||
		    !Message.IsValidContent(text))
			return false;

		var bot = await Context.Users.GetAsync(BasilBot.Id, cancellationToken);
		if (bot is null || channel.IsClosed) return false;

		channel.PostMessage(bot, text, notice, Context.Connections.Clock.GetUtcNow(), null);
		return true;
	}

	/// <summary>Sends a private message to an online user as BasilBot.</summary>
	/// <param name="to">The recipient.</param>
	/// <param name="text">The message text.</param>
	/// <param name="notice">Whether the message is a notice.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns><see langword="true" /> when the message was posted; otherwise, <see langword="false" />.</returns>
	/// <remarks>
	///     This system operation needs no rights because the server itself calls it. Chat commands must use the scope of
	///     the connection that typed them, not this method, to act for a user. Messages are not delivered offline.
	/// </remarks>
	public Task<bool> WhisperAsync(User to, string text, bool notice = false,
		CancellationToken cancellationToken = default)
	{
		if (!to.IsOnline) return Task.FromResult(false);
		return SayAsync(Context.PmChannels.Of(to), text, notice, cancellationToken);
	}

	/// <summary>Finds an open channel the bot may speak in by the name clients use.</summary>
	/// <param name="name">The channel name.</param>
	/// <returns>The open channel, or <see langword="null" /> when no matching channel is open.</returns>
	/// <remarks>
	///     This system operation needs no rights because the server itself calls it. Chat commands must not use it to
	///     act for a user.
	/// </remarks>
	public Channel? FindChannel(string name)
	{
		if (Context.Chat.Registry.All.TryGetValue(name, out var general))
			return general.IsClosed ? null : general;

		if (!name.StartsWith("#mp_", StringComparison.Ordinal)
		    || !int.TryParse(name.AsSpan(4), out var roomId)
		    || !Context.Lobby.Rooms.TryGetValue(roomId, out var room)
		    || room.IsClosed)
			return null;

		var roomChannel = Context.RoomChannels.Of(room);
		return room.IsClosed || roomChannel.IsClosed ? null : roomChannel;
	}

	/// <summary>
	///     The server itself calls this system operation, which needs no rights, to notify one user's open osu! client.
	/// </summary>
	/// <param name="text">The text to show.</param>
	/// <param name="to">The user to notify.</param>
	/// <returns>The number of open osu! clients sent the notification.</returns>
	/// <remarks>This system operation needs no rights because the server itself calls it.</remarks>
	public int Notify(string text, UserRecord to)
	{
		return NotifyCore(text, [to]);
	}

	/// <summary>
	///     The server itself calls this system operation, which needs no rights, to notify the specified users' open osu!
	///     clients.
	/// </summary>
	/// <param name="text">The text to show.</param>
	/// <param name="to">The users to notify.</param>
	/// <returns>The number of open osu! clients sent the notification.</returns>
	/// <remarks>This system operation needs no rights because the server itself calls it.</remarks>
	public int Notify(string text, IReadOnlyCollection<UserRecord> to)
	{
		return NotifyCore(text, to);
	}

	/// <summary>
	///     The server itself calls this system operation, which needs no rights, to notify every open osu! client.
	/// </summary>
	/// <param name="text">The text to show.</param>
	/// <returns>The number of open osu! clients sent the notification.</returns>
	/// <remarks>This system operation needs no rights because the server itself calls it.</remarks>
	public int NotifyAll(string text)
	{
		return NotifyCore(text, null);
	}

	private int NotifyCore(string text, IReadOnlyCollection<UserRecord>? to)
	{
		if (string.IsNullOrEmpty(text)) return 0;

		var context = Context.Connections;
		var recipients = (to is null
				? context.Registry.OpenBanchoClients()
				: to.Select(context.Registry.OpenBanchoClient).OfType<BanchoConnection>())
			.ToList();
		if (recipients.Count == 0) return 0;

		context.Events.Publish(new UserNotificationSent(recipients, text));
		return recipients.Count;
	}
}