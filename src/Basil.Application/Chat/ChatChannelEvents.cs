using Basil.Domain.Chat;
using Basil.Application.Common.Events;
using Basil.Application.Sessions;

namespace Basil.Application.Chat;

/// <summary>Something happened to a chat channel.</summary>
public abstract record ChatChannelEvent(ChatChannelSession Channel) : Event;

/// <summary>A chat channel was opened.</summary>
public sealed record ChatChannelOpened(ChatChannelSession Channel) : ChatChannelEvent(Channel);

/// <summary>A chat channel was closed; it emits nothing afterwards.</summary>
public sealed record ChatChannelClosed(ChatChannelSession Channel) : ChatChannelEvent(Channel);

/// <summary>A connection joined or left a chat channel.</summary>
public abstract record ChatChannelMembershipEvent(ChatChannelSession Channel) : ChatChannelEvent(Channel);

/// <summary>A connection joined a chat channel.</summary>
public sealed record MemberJoined(ChatChannelSession Channel, Connection Member) : ChatChannelMembershipEvent(Channel);

/// <summary>A connection left a chat channel.</summary>
/// <param name="Channel">The channel the connection left.</param>
/// <param name="Member">The connection that left.</param>
/// <param name="Kicked">Whether the channel's owner removed the connection.</param>
public sealed record MemberParted(ChatChannelSession Channel, Connection Member, bool Kicked)
	: ChatChannelMembershipEvent(Channel);

/// <summary>A message was posted to a chat channel.</summary>
public abstract record MessageEvent(ChatChannelSession Channel) : ChatChannelEvent(Channel);

/// <summary>A message was posted to a chat channel.</summary>
/// <param name="Channel">The channel the message was posted to.</param>
/// <param name="Message">The message that was posted.</param>
/// <param name="Truncated">Whether the message was cut to <see cref="ChatChannelSession.MaxMessageLength" /> characters.</param>
public sealed record MessagePosted(ChatChannelSession Channel, ChatMessage Message, bool Truncated)
	: MessageEvent(Channel);