using Basil.Application.Storage.Contracts.Chat;
using Basil.Application.Storage.Contracts.Sessions;
using Basil.Domain.Users;

namespace Basil.Application.Services.Contracts.Chat;

/// <summary>Posts messages to chat channels.</summary>
public interface IChannelMessagesService
{
	/// <summary>Posts a message to a channel.</summary>
	/// <param name="channel">The channel to post to.</param>
	/// <param name="by">The connection posting.</param>
	/// <param name="text">The text of the message.</param>
	/// <param name="notice">Whether the message is a notice, which never triggers an automatic reply.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome of the post.</returns>
	/// <remarks>
	///     A message longer than <see cref="ChannelSession.MaxMessageLength" /> characters is cut. A private message is a
	///     post into the recipient's private-message channel; the recipient's away message is sent back to the author, carried
	///     by the same <see cref="ChannelMessagePosted" />. Posting needs <see cref="Permissions.ChatPost" />, a private
	///     message <see cref="Permissions.ChatPostPrivate" />; a private message passes blocks and friends-only
	///     settings with <see cref="Permissions.ChatMessageAnyone" />.
	/// </remarks>
	Task<ChannelPostResult> PostAsync(ChannelSession channel, Connection by, string text, bool notice = false,
		CancellationToken cancellationToken = default);
}