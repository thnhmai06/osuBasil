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

/// <summary>Something happened to the messages of a chat channel.</summary>
public abstract record MessageEvent(ChatChannelSession Channel) : ChatChannelEvent(Channel);

/// <summary>A message was posted to a chat channel.</summary>
/// <param name="Channel">The channel the message was posted to.</param>
/// <param name="Message">The message that was posted.</param>
/// <param name="Truncated">Whether the message was cut to <see cref="ChatChannelSession.MaxMessageLength" /> characters.</param>
public sealed record MessagePosted(ChatChannelSession Channel, ChatMessage Message, bool Truncated)
	: MessageEvent(Channel);

/// <summary>A connection started spectating the channel's host.</summary>
/// <param name="Channel">The channel of the player being spectated.</param>
/// <param name="Spectator">The connection that started spectating.</param>
/// <param name="HostJoined">Whether the host joined the channel because this is its first spectator.</param>
public sealed record SpectatorJoined(ChatChannelSession Channel, Connection Spectator, bool HostJoined)
	: ChatChannelMembershipEvent(Channel);

/// <summary>A connection stopped spectating the channel's host.</summary>
/// <param name="Channel">The channel of the player being spectated.</param>
/// <param name="Spectator">The connection that stopped spectating.</param>
/// <param name="HostLeft">Whether the host left the channel because this was its last spectator.</param>
public sealed record SpectatorLeft(ChatChannelSession Channel, Connection Spectator, bool HostLeft)
	: ChatChannelMembershipEvent(Channel);

/// <summary>A spectator reported that it cannot spectate the host, usually because it lacks the beatmap.</summary>
/// <param name="Channel">The channel of the player being spectated.</param>
/// <param name="Spectator">The spectator that cannot spectate.</param>
public sealed record SpectatorCantSpectate(ChatChannelSession Channel, Connection Spectator)
	: ChatChannelEvent(Channel);