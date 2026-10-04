using Basil.Application.Contracts.Events;
using Basil.Application.Storage.Chat;
using Basil.Application.Storage.Sessions;

namespace Basil.Application.Contracts.Chat;

/// <summary>Something happened to a chat channel.</summary>
public abstract record ChannelEvent(ChannelSession Channel) : Event;

/// <summary>A chat channel was opened.</summary>
public sealed record ChannelOpened(ChannelSession Channel) : ChannelEvent(Channel);

/// <summary>A chat channel was closed; it emits nothing afterwards.</summary>
/// <param name="Channel">The channel that closed.</param>
/// <param name="Members">The connections that were members when the channel closed; they are no longer members.</param>
public sealed record ChannelClosed(ChannelSession Channel, IReadOnlyList<Connection> Members) : ChannelEvent(Channel);

/// <summary>A connection joined or left a chat channel.</summary>
public abstract record ChannelMembershipEvent(ChannelSession Channel) : ChannelEvent(Channel);

/// <summary>A connection joined a chat channel.</summary>
/// <param name="Channel">The channel the connection joined.</param>
/// <param name="Member">The connection that joined.</param>
/// <param name="Replaced">
///     A closed connection of the same user and kind that the new member replaced, or
///     <see langword="null" />.
/// </param>
public sealed record ChannelMemberJoined(ChannelSession Channel, Connection Member, Connection? Replaced)
	: ChannelMembershipEvent(Channel);

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
/// <param name="AwayReply">
///     The recipient's away message, sent back to the author's private-message channel, or
///     <see langword="null" /> when none was sent.
/// </param>
public sealed record ChannelMessagePosted(ChannelSession Channel, Message Message, bool Truncated, Message? AwayReply)
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

/// <summary>A spectator reported that it failed to spectate the host, usually because it lacks the beatmap.</summary>
/// <param name="Channel">The channel of the player being spectated.</param>
/// <param name="Spectator">The spectator that failed to spectate.</param>
public sealed record ChannelSpectatorFailed(ChannelSession Channel, Connection Spectator)
	: ChannelEvent(Channel);