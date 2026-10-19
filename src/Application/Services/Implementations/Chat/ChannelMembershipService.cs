using Basil.Application.Services.Contracts.Chat;
using Basil.Application.Services.Contracts.Sessions;
using Basil.Application.Services.Implementations.Sessions;

namespace Basil.Application.Services.Implementations.Chat;

/// <summary>Joins connections to chat channels, parts them and closes a channel by removing every member.</summary>
internal sealed class ChannelMembershipService(
	IGeneralChannelRegistry generalChannels,
	ChannelEventStream events,
	TimeProvider time) : IChannelMembershipService
{
	/// <inheritdoc />
	public ChannelJoinResult Join(ChannelSession channel, Connection by)
	{
		using var scope = channel.Enter();
		if (channel.IsClosed) return ChannelJoinResult.Closed;
		if (!ChannelRules.CanRead(channel, by, time.GetUtcNow())) return ChannelJoinResult.NoPermission;
		if (channel.Members.Contains(by)) return ChannelJoinResult.AlreadyMember;

		Connection? replaced = null;
		if (!by.Type.AllowsMany())
		{
			replaced = channel.Members.FirstOrDefault(m => m.User.Equals(by.User) && m.Type == by.Type);
			if (replaced is not null)
			{
				if (replaced.IsOpen) return ChannelJoinResult.AlreadyMember;
				channel.MutableMembers.Remove(replaced);
			}
		}

		channel.MutableMembers.Add(by);
		events.Emit(new ChannelMemberJoined(channel, by, replaced));
		return ChannelJoinResult.Joined;
	}

	/// <inheritdoc />
	public ChannelPartResult Part(ChannelSession channel, Connection by)
	{
		using var scope = channel.Enter();
		if (!channel.MutableMembers.Remove(by)) return ChannelPartResult.NotMember;
		events.Emit(new ChannelMemberParted(channel, by, false));
		return ChannelPartResult.Parted;
	}

	/// <inheritdoc />
	public void JoinAutoChannels(Connection by)
	{
		foreach (var channel in generalChannels.All.Values)
			if (channel.Channel.AutoJoin)
				Join(channel, by);
	}

	/// <inheritdoc />
	public void PartAll(Connection by)
	{
		foreach (var channel in generalChannels.All.Values)
			Part(channel, by);
	}

	/// <summary>Closes a channel, removing every member.</summary>
	/// <param name="channel">The channel to close.</param>
	/// <remarks>
	///     Announces one <see cref="ChannelClosed" /> carrying every member that was removed. A general channel also
	///     stops being listed. Closing a closed channel does nothing.
	/// </remarks>
	internal void Close(ChannelSession channel)
	{
		using var scope = channel.Enter();
		if (channel.IsClosed) return;
		channel.IsClosed = true;

		var members = channel.Members.ToArray();
		foreach (var member in members)
			channel.MutableMembers.Remove(member);

		events.Emit(new ChannelClosed(channel, members));

		if (channel is GeneralChannelSession general)
			generalChannels.Remove(general);
	}
}
