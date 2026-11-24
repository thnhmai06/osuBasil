using Basil.Application.Chat.Objects;
using Basil.Application.Servers.Capabilities;
using Basil.Application.Sessions.Objects;
using Basil.Application.Sessions.Objects.Events;
using Basil.Domain.Chat;
using Basil.Domain.Users;
using User = Basil.Application.Users.Objects.User;

namespace Basil.Application.Servers.Objects;

public sealed partial class Server
{
	internal Server(ServerContext context)
	{
		Context = context;
	}

	internal ServerContext Context { get; }

	public ScopedServer ScopeTo(Session actor)
	{
		return new ScopedServer(this, actor);
	}

	public async Task<ServerRecovery> RecoverAsync(CancellationToken ct = default)
	{
		var openedChannels = 0;
		foreach (var channel in await Context.Channels.ListAsync(ct))
			if (OpenChannel(channel) is not null)
				openedChannels++;

		var closedConnections = await Context.Maintenance.CloseStoppedAsync();
		await Context.Bot.EnsureBotAsync(ct);
		var botOnline = await Context.Bot.ConnectBotAsync(ct) is not null;

		var closedMatches = await Context.Lobby.CloseUnfinishedAsync();
		return new ServerRecovery(openedChannels, botOnline, closedMatches, closedConnections);
	}

	public GeneralChannel? OpenChannel(GeneralChannelRecord record)
	{
		return Context.Chat.OpenGeneral(record);
	}

	public async Task<bool> SayAsync(Channel channel, string text, bool notice = false,
		CancellationToken ct = default)
	{
		if (channel.IsClosed || channel is PmChannel { Owner.IsOnline: false } || string.IsNullOrEmpty(text) ||
		    !Message.IsValidContent(text))
			return false;

		var bot = await Context.Users.GetAsync(BasilBot.Id, ct);
		if (bot is null || channel.IsClosed) return false;

		channel.PostMessage(bot, text, notice, Context.Connections.Clock.GetUtcNow(), null);
		return true;
	}

	public Task<bool> WhisperAsync(UserSessions to, string text, bool notice = false,
		CancellationToken ct = default)
	{
		if (!to.IsOnline) return Task.FromResult(false);
		return SayAsync(Context.PmChannels.Of(to), text, notice, ct);
	}

	public Channel? FindChannel(string name)
	{
		if (Context.Chat.Registry.All.TryGetValue(name, out var general))
			return general.IsClosed ? null : general;

		if (!RoomChannelRules.TryParse(name, out var roomId)
		    || !Context.Lobby.Rooms.TryGetValue(roomId, out var room)
		    || room.IsClosed)
			return null;

		var roomChannel = Context.RoomChannels.Of(room);
		return room.IsClosed || roomChannel.IsClosed ? null : roomChannel;
	}

	public int Notify(string text, UserRecord to)
	{
		return NotifyCore(text, [to]);
	}

	public int Notify(string text, IReadOnlyCollection<UserRecord> to)
	{
		return NotifyCore(text, to);
	}

	public int Notify(string text, IReadOnlyCollection<int> userIds)
	{
		return NotifyCore(text, [.. Context.Connections.Registry.Online.Keys.Where(user => userIds.Contains(user.Id))]);
	}

	public int NotifyAll(string text)
	{
		return NotifyCore(text, null);
	}

	private int NotifyCore(string text, IReadOnlyCollection<UserRecord>? to)
	{
		if (string.IsNullOrEmpty(text)) return 0;

		var context = Context.Connections;
		var recipients = (to is null
				? context.Registry.OpenStableClients()
				: to.Select(context.Registry.OpenStableClient).OfType<StableSession>())
			.ToList();
		if (recipients.Count == 0) return 0;

		context.Events.Publish(new SessionNotificationSent(recipients, text));
		return recipients.Count;
	}
}