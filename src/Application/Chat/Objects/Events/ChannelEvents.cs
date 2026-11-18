using Basil.Application.Shared.Events;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Chat;

namespace Basil.Application.Chat.Objects.Events;

public abstract record ChannelEvent(Channel Channel) : Event;

public sealed record ChannelOpened(Channel Channel) : ChannelEvent(Channel);

public sealed record ChannelClosed(Channel Channel, IReadOnlyList<Connection> Members) : ChannelEvent(Channel);

public abstract record ChannelMembershipEvent(Channel Channel) : ChannelEvent(Channel);

public sealed record ChannelMemberJoined(Channel Channel, Connection Member, Connection? Replaced)
	: ChannelMembershipEvent(Channel);

public sealed record ChannelMemberParted(Channel Channel, Connection Member, bool Kicked)
	: ChannelMembershipEvent(Channel);

public abstract record ChannelMessageEvent(Channel Channel) : ChannelEvent(Channel);

public sealed record ChannelMessagePosted(
	Channel Channel,
	Message Message,
	bool Truncated,
	Message? AwayReply,
	Connection? Actor = null,
	bool BypassesBlocks = false)
	: ChannelMessageEvent(Channel);

public sealed record ChannelConfigured(
	Channel Channel,
	GeneralChannelChange Change,
	IReadOnlyList<Connection> Removed)
	: ChannelEvent(Channel);