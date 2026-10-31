using Basil.Application.Users.Objects.Connections;
using Basil.Application.Common.Events;
using Basil.Domain.Chat;

namespace Basil.Application.Chat.Objects.Events;

/// <summary>Something happened to a chat channel.</summary>
public abstract record ChannelEvent(Channel Channel) : Event;

/// <summary>A chat channel was opened.</summary>
public sealed record ChannelOpened(Channel Channel) : ChannelEvent(Channel);

/// <summary>A chat channel was closed; it emits nothing afterwards.</summary>
/// <param name="Channel">The channel that closed.</param>
/// <param name="Members">The connections that were members when the channel closed; they are no longer members.</param>
public sealed record ChannelClosed(Channel Channel, IReadOnlyList<Connection> Members) : ChannelEvent(Channel);

/// <summary>A connection joined or left a chat channel.</summary>
public abstract record ChannelMembershipEvent(Channel Channel) : ChannelEvent(Channel);

/// <summary>A connection joined a chat channel.</summary>
/// <param name="Channel">The channel the connection joined.</param>
/// <param name="Member">The connection that joined.</param>
/// <param name="Replaced">
///     A closed connection of the same user and kind that the new member replaced, or
///     <see langword="null" />.
/// </param>
public sealed record ChannelMemberJoined(Channel Channel, Connection Member, Connection? Replaced)
	: ChannelMembershipEvent(Channel);

/// <summary>A connection left a chat channel.</summary>
/// <param name="Channel">The channel the connection left.</param>
/// <param name="Member">The connection that left.</param>
/// <param name="Kicked">Whether the channel's owner removed the connection.</param>
public sealed record ChannelMemberParted(Channel Channel, Connection Member, bool Kicked)
	: ChannelMembershipEvent(Channel);

/// <summary>Something happened to the messages of a chat channel.</summary>
public abstract record ChannelMessageEvent(Channel Channel) : ChannelEvent(Channel);

/// <summary>A message was posted to a chat channel.</summary>
/// <param name="Channel">The channel the message was posted to.</param>
/// <param name="Message">The message that was posted.</param>
/// <param name="Truncated">Whether the message was cut to <see cref="Channel.MaxMessageLength" /> characters.</param>
/// <param name="AwayReply">
///     The recipient's away message, sent back to the author's private-message channel, or
///     <see langword="null" /> when none was sent.
/// </param>
/// <param name="By">
///     The connection that posted the message, or <see langword="null" /> for a message the server itself said.
/// </param>
public sealed record ChannelMessagePosted(
	Channel Channel,
	Message Message,
	bool Truncated,
	Message? AwayReply,
	Connection? By = null)
	: ChannelMessageEvent(Channel);

/// <summary>A general channel's settings changed.</summary>
/// <param name="Channel">The channel.</param>
/// <param name="Change">The settings that changed.</param>
/// <param name="Removed">The members removed because they may no longer read the channel.</param>
public sealed record ChannelConfigured(
	Channel Channel,
	GeneralChannelChange Change,
	IReadOnlyList<Connection> Removed)
	: ChannelEvent(Channel);