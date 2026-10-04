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

namespace Basil.Application.Services.Chat;

/// <summary>Opens and closes chat channels and lets connections join, part, post and spectate.</summary>
internal sealed class ChannelService(
	GeneralChannelRegistry generalChannels,
	ChannelEventStream events,
	ChannelSpectatorService spectators,
	IRelationshipRepository relationships,
	TimeProvider time) : IChannelService
{
	/// <inheritdoc />
	public ChannelReader<ChannelEvent> Events => events.Reader;

	/// <inheritdoc />
	public IChannelSpectatorService Spectators => spectators;

	/// <inheritdoc />
	public void Close(ChannelSession channel)
	{
		using var scope = channel.Enter();
		if (channel.IsClosed) return;
		channel.IsClosed = true;

		var members = channel.Members.ToArray();
		foreach (var member in members)
			channel.RemoveMember(member);

		events.Emit(new ChannelClosed(channel, members));

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
		events.Emit(new ChannelMemberJoined(channel, by, replaced));
		return ChannelJoinResult.Joined;
	}

	/// <inheritdoc />
	public ChannelPartResult Part(ChannelSession channel, Connection by)
	{
		using var scope = channel.Enter();
		if (!channel.RemoveMember(by)) return ChannelPartResult.NotMember;
		events.Emit(new ChannelMemberParted(channel, by, false));
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
		if (channel is PmChannelSession { Owner.AwayMessage: { } away } pmChannel
		    && !ReferenceEquals(by.Session, pmChannel.Owner)
		    && !notice)
			awayReply = new Message(pmChannel.Owner.User, away, now);

		events.Emit(new ChannelMessagePosted(channel, message, truncated, awayReply));
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
	public GeneralChannelSession? Open(GeneralChannel channel)
	{
		var session = new GeneralChannelSession(channel);
		if (!generalChannels.Add(session)) return null;
		events.Emit(new ChannelOpened(session));
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
	private static bool CanWrite(ChannelSession channel, Connection connection)
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
}
