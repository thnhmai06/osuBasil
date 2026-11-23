using Basil.Application.Shared.Events;
using Basil.Application.Sessions.Objects;
using Basil.Domain.Chat;

namespace Basil.Application.Chat.Objects.Events;

public abstract record ChannelEvent(Channel Channel) : Event;

public sealed record ChannelOpened(Channel Channel) : ChannelEvent(Channel);

public sealed record ChannelClosed(Channel Channel, IReadOnlyList<Session> Members) : ChannelEvent(Channel);

public abstract record ChannelMembershipEvent(Channel Channel) : ChannelEvent(Channel);

public sealed record ChannelMemberJoined(Channel Channel, Session Member, Session? Replaced)
	: ChannelMembershipEvent(Channel);

public sealed record ChannelMemberParted(Channel Channel, Session Member, bool Kicked)
	: ChannelMembershipEvent(Channel);

public abstract record ChannelMessageEvent(Channel Channel) : ChannelEvent(Channel);

public sealed record ChannelMessagePosted(
	Channel Channel,
	Message Message,
	bool Truncated,
	Message? AwayReply,
	Session? Actor = null,
	bool BypassesBlocks = false)
	: ChannelMessageEvent(Channel);

public sealed record ChannelConfigured(
	Channel Channel,
	GeneralChannelChange Change,
	IReadOnlyList<Session> Removed)
	: ChannelEvent(Channel);