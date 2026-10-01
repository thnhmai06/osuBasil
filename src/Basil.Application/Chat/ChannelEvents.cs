using Basil.Application.Common.Events;
using Basil.Application.Sessions;
using Basil.Domain.Chat;

namespace Basil.Application.Chat;

/// <summary>Something happened to a chat channel.</summary>
public abstract record ChannelEvent(ChannelSession Channel) : Event;

/// <summary>A chat channel was opened.</summary>
public sealed record ChannelOpened(ChannelSession Channel) : ChannelEvent(Channel);

/// <summary>A chat channel was closed; it emits nothing afterwards.</summary>
public sealed record ChannelClosed(ChannelSession Channel) : ChannelEvent(Channel);

/// <summary>A connection joined or left a chat channel.</summary>
public abstract record ChannelMembershipEvent(ChannelSession Channel) : ChannelEvent(Channel);

/// <summary>A connection joined a chat channel.</summary>
public sealed record ChannelMemberJoined(ChannelSession Channel, Connection Member) : ChannelMembershipEvent(Channel);

/// <summary>A connection left a chat channel.</summary>
/// <param name="Channel">The channel the connection left.</param>
/// <param name="Member">The connection that left.</param>
/// <param name="Kicked">Whether the channel's owner removed the connection.</param>
public sealed record ChannelMemberParted(ChannelSession Channel, Connection Member, bool Kicked)
	: ChannelMembershipEvent(Channel);

/// <summary>Something happened to the messages of a chat channel.</summary>
public abstract record ChannelMessageEvent(ChannelSession Channel) : ChannelEvent(Channel);

/// <summary>A message was posted to a chat channel.</summary>
/// <param name="Channel">The channel the message was posted to.</param>
/// <param name="Message">The message that was posted.</param>
/// <param name="Truncated">Whether the message was cut to <see cref="ChannelSession.MaxMessageLength" /> characters.</param>
public sealed record ChannelMessagePosted(ChannelSession Channel, Message Message, bool Truncated)
	: ChannelMessageEvent(Channel);

/// <summary>A connection started spectating the channel's host.</summary>
/// <param name="Channel">The channel of the player being spectated.</param>
/// <param name="Spectator">The connection that started spectating.</param>
/// <param name="HostJoined">Whether the host joined the channel because this is its first spectator.</param>
public sealed record ChannelSpectatorJoined(ChannelSession Channel, Connection Spectator, bool HostJoined)
	: ChannelMembershipEvent(Channel);

/// <summary>A connection stopped spectating the channel's host.</summary>
/// <param name="Channel">The channel of the player being spectated.</param>
/// <param name="Spectator">The connection that stopped spectating.</param>
/// <param name="HostLeft">Whether the host left the channel because this was its last spectator.</param>
public sealed record ChannelSpectatorLeft(ChannelSession Channel, Connection Spectator, bool HostLeft)
	: ChannelMembershipEvent(Channel);

/// <summary>A spectator reported that it cannot spectate the host, usually because it lacks the beatmap.</summary>
/// <param name="Channel">The channel of the player being spectated.</param>
/// <param name="Spectator">The spectator that cannot spectate.</param>
public sealed record ChannelSpectatorCantSpectate(ChannelSession Channel, Connection Spectator)
	: ChannelEvent(Channel);