using Basil.Application.Shared.Events;
using Basil.Application.Sessions.Objects;
using Basil.Domain.Chat;
using Basil.Domain.Sessions;

using Session = Basil.Application.Sessions.Objects.Session;

namespace Basil.Application.Chat.Objects.Events;

[EventLane(EventLane.Chat)]
public abstract record ChatEvent : Event;

public abstract record ChannelEvent(Channel Channel) : ChatEvent;

/// <summary>An account went offline: the sessions of other users that shared a channel with it are told once, however many channels they shared.</summary>
/// <param name="Session">The last session of the account to close.</param>
/// <param name="Reason">Why that session ended.</param>
/// <param name="Audience">The sessions of the other users that shared a channel with the account.</param>
public sealed record ChatQuit(Session Session, SessionEndReason Reason, IReadOnlyList<Session> Audience) : ChatEvent;

public sealed record ChannelOpened(Channel Channel) : ChannelEvent(Channel);

public sealed record ChannelClosed(Channel Channel, IReadOnlyList<Session> Members) : ChannelEvent(Channel);

public abstract record ChannelMembershipEvent(Channel Channel) : ChannelEvent(Channel);

/// <param name="FirstOfUser">Whether no other session of the member's user was in the channel before this one.</param>
public sealed record ChannelMemberJoined(Channel Channel, Session Member, Session? Replaced, bool FirstOfUser)
	: ChannelMembershipEvent(Channel);

/// <param name="LastOfUser">Whether no other session of the member's user remains in the channel.</param>
/// <param name="UserOffline">Whether the member's user went offline and the departure was already announced by <see cref="ChatQuit"/>; no PART is shown then.</param>
public sealed record ChannelMemberParted(Channel Channel, Session Member, bool Kicked, bool LastOfUser, bool UserOffline)
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