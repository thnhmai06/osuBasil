using Basil.Application.Chat.Objects;
using Basil.Application.Common;
using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Chat.Messages;

/// <summary>Asks Chat to post a message for a connection.</summary>
/// <param name="Actor">The connection that sends the message.</param>
/// <param name="Target">Where the message goes.</param>
/// <param name="Text">The text of the message.</param>
/// <param name="Notice">Whether the message is a notice, which never triggers an automatic reply.</param>
/// <remarks>
///     The answer is <see cref="ChannelOutcome.NotFound" /> when the target is not open, otherwise what posting to the
///     channel answers.
/// </remarks>
public sealed record PostChatMessage(Connection Actor, ChatTarget Target, string Text, bool Notice = false)
	: IRequest<Result<ChannelOutcome>>;
