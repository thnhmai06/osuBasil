using System.Threading.Channels;
using Basil.Application.Contracts.Chat;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Services.Sessions;
using Basil.Application.Storage.Chat;
using Basil.Application.Storage.Multiplayer;
using Basil.Application.Storage.Sessions;
using Basil.Application.Storage.Users;
using Basil.Domain.Chat;
using Basil.Domain.Client;
using Basil.Domain.Social;
using Channel = System.Threading.Channels.Channel;

namespace Basil.Application.Services.Chat;

/// <summary>Opens and closes chat channels and lets connections join, part, post and spectate.</summary>
internal sealed class ChannelService(
	GeneralChannelRegistry generalChannels,
	UserRegistry users,
	IRelationshipRepository relationships,
	TimeProvider time) : IChannelService
{
	private readonly Channel<ChannelEvent> _events = Channel.CreateUnbounded<ChannelEvent>();

	/// <inheritdoc />
	public ChannelReader<ChannelEvent> Events => _events.Reader;

	/// <inheritdoc />
	public void Close(ChannelSession channel)
	{
		using var scope = channel.Enter();
		if (channel.IsClosed) return;
		channel.IsClosed = true;

		var members = channel.Members.ToArray();
		foreach (var member in members)
			channel.RemoveMember(member);

		Emit(new ChannelClosed(channel, members));

		if (channel is GeneralChannelSession general)
			generalChannels.Remove(general);
	}

	/// <inheritdoc />
	public ChannelJoinResult Join(ChannelSession channel, Connection by)
	{
		using var scope = channel.Enter();
		if (channel.IsClosed) return ChannelJoinResult.Closed;
		if (!CanRead(channel, by)) return ChannelJoinResult.NoPermission;
		if (channel.Members.Contains(by)) return ChannelJoinResult.AlreadyMember;

		Connection? replaced = null;
		if (!by.Type.AllowsMany())
		{
			replaced = channel.Members.FirstOrDefault(m => m.User.Equals(by.User) && m.Type == by.Type);
			if (replaced is not null)
			{
				if (replaced.IsOpen) return ChannelJoinResult.AlreadyMember;
				channel.RemoveMember(replaced);
			}
		}

		channel.AddMember(by);
		Emit(new ChannelMemberJoined(channel, by, replaced));
		return ChannelJoinResult.Joined;
	}

	/// <inheritdoc />
	public ChannelPartResult Part(ChannelSession channel, Connection by)
	{
		using var scope = channel.Enter();
		if (!channel.RemoveMember(by)) return ChannelPartResult.NotMember;
		Emit(new ChannelMemberParted(channel, by, false));
		return ChannelPartResult.Parted;
	}

	/// <inheritdoc />
	public async Task<ChannelPostResult> PostAsync(ChannelSession channel, Connection by, string text,
		bool notice = false, CancellationToken cancellationToken = default)
	{
		if (channel.IsClosed) return ChannelPostResult.Closed;

		var now = time.GetUtcNow();
		if (by.User.Value.SilenceEndsAt > now) return ChannelPostResult.Silenced;
		if (string.IsNullOrWhiteSpace(text)) return ChannelPostResult.Empty;
		if (channel is not PmChannelSession && !channel.Members.Contains(by)) return ChannelPostResult.NotMember;
		if (!CanWrite(channel, by)) return ChannelPostResult.NoWritePermission;

		if (channel is PmChannelSession pm)
		{
			if (by.Type is not ConnectionType.Bot)
			{
				var rels = await relationships.ListAsync(pm.Owner.User, cancellationToken);
				var blocked = rels.Any(r => r.Type == RelationshipType.Block && r.Target.Equals(by.User));
				var friend = rels.Any(r => r.Type == RelationshipType.Friend && r.Target.Equals(by.User));
				if (blocked || (pm.Owner.PmPrivate && !friend))
					return ChannelPostResult.Blocked;
			}

			if (pm.Owner.User.Value.SilenceEndsAt > now) return ChannelPostResult.TargetSilenced;
		}

		var truncated = text.Length > ChannelSession.MaxMessageLength;
		var message = new Message(by.User, truncated ? text[..ChannelSession.MaxMessageLength] : text, now, notice);

		Message? awayReply = null;
		if (channel is PmChannelSession pmChannel
		    && pmChannel.Owner.AwayMessage is { } away
		    && !ReferenceEquals(by.Session, pmChannel.Owner)
		    && !notice)
			awayReply = new Message(pmChannel.Owner.User, away, now);

		Emit(new ChannelMessagePosted(channel, message, truncated, awayReply));
		return ChannelPostResult.Posted;
	}

	/// <inheritdoc />
	public void JoinAutoChannels(Connection by)
	{
		foreach (var channel in generalChannels.All)
			if (channel.Channel.AutoJoin)
				Join(channel, by);
	}

	/// <inheritdoc />
	public void PartAll(Connection by)
	{
		foreach (var channel in generalChannels.All)
			Part(channel, by);
	}

	/// <inheritdoc />
	public SpectateResult Spectate(BanchoConnection host, Connection by)
	{
		if (by is not (BanchoConnection or TourneyConnection or BotConnection))
			throw new ArgumentException("Only osu!, osu!tourney clients and BasilBot can spectate.", nameof(by));

		var spectatorChannel = host.SpectatorChannel;
		using var scope = spectatorChannel.Enter();

		if (!host.IsOpen) return SpectateResult.TargetOffline;
		if (by.User.Equals(host.User)) return SpectateResult.Self;
		if (spectatorChannel.Members.Contains(by)) return SpectateResult.AlreadySpectating;

		var hostJoined = spectatorChannel.AddMember(host);
		spectatorChannel.AddMember(by);
		Emit(new ChannelSpectatorJoined(spectatorChannel, by, hostJoined));
		return SpectateResult.Spectating;
	}

	/// <inheritdoc />
	public bool StopSpectating(Connection by)
	{
		var spectatorChannel = users.FindSpectating(by);
		if (spectatorChannel is null) return false;

		using var scope = spectatorChannel.Enter();
		if (ReferenceEquals(by, spectatorChannel.Host) || !spectatorChannel.RemoveMember(by)) return false;

		var hostLeft = !spectatorChannel.Spectators.Any() && spectatorChannel.RemoveMember(spectatorChannel.Host);
		Emit(new ChannelSpectatorLeft(spectatorChannel, by, hostLeft));
		return true;
	}

	/// <inheritdoc />
	public bool ReportSpectatingFailed(Connection by)
	{
		var spectatorChannel = users.FindSpectating(by);
		if (spectatorChannel is null) return false;

		Emit(new ChannelSpectatorFailed(spectatorChannel, by));
		return true;
	}

	/// <inheritdoc />
	public GeneralChannelSession? Open(GeneralChannel channel)
	{
		var session = new GeneralChannelSession(channel);
		if (!generalChannels.Add(session)) return null;
		Emit(new ChannelOpened(session));
		return session;
	}

	/// <summary>Checks whether a connection may read a channel.</summary>
	private static bool CanRead(ChannelSession channel, Connection connection)
	{
		return channel switch
		{
			GeneralChannelSession g => connection.User.Value.Privilege.Has(g.Channel.ReadPrivilege),
			PmChannelSession p => ReferenceEquals(connection.Session, p.Owner) &&
			                      connection.Type is not ConnectionType.Tourney,
			SpectatorChannelSession s => s.Members.Contains(connection),
			RoomChannelSession r => connection.Type is ConnectionType.Bot
			                        || (connection is BanchoConnection player && r.Room.Slots.Find(player) is not null)
			                        || (connection is TourneyConnection observer && r.Room.Observers.Contains(observer))
			                        || RoomRules.IsManager(r.Room, connection.User),
			_ => false
		};
	}

	/// <summary>Checks whether a connection may write to a channel.</summary>
	private bool CanWrite(ChannelSession channel, Connection connection)
	{
		return channel switch
		{
			GeneralChannelSession g => connection.User.Value.Privilege.Has(g.Channel.WritePrivilege),
			PmChannelSession => connection.IsOpen,
			SpectatorChannelSession s => s.Members.Contains(connection),
			RoomChannelSession => CanRead(channel, connection),
			_ => false
		};
	}

	private void Emit(ChannelEvent @event)
	{
		_events.Writer.TryWrite(@event);
	}
}