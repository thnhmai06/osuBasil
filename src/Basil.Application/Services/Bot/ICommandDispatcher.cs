using Basil.Application.Sessions;
using Basil.Application.Sessions.Multiplayer;

namespace Basil.Application.Services.Bot;

/// <summary>
///     Dispatches chat commands, sending each reply through the provided
///     <see cref="ICommandReplySink" />.
/// </summary>
/// <remarks>
///     This is the bot account's command-dispatch layer, used by both channel chat and DMs. The
///     dispatcher sends its own reply text through <c>sink</c> as it runs and returns only whether
///     the message was a recognized, successfully-run command. Unrecognized text and unauthorized
///     <c>!mp</c> subcommands are ignored silently by convention, so ordinary chat produces no error
///     noise.
/// </remarks>
public interface ICommandDispatcher
{
	/// <param name="sender">The player who sent the message.</param>
	/// <param name="rawMessage">The message text exactly as sent, prefix included if present.</param>
	/// <param name="matchScope">
	///     The sender's current match, but only when the message was sent in that match's own chat
	///     channel; <see langword="null" /> otherwise, including for private messages, which are never
	///     a match channel. Every <c>!mp</c> subcommand requires this to be non-null, except
	///     <c>!mp make</c>, <c>!mp makeprivate</c>, <c>!mp join</c>, <c>!mp in</c>, and
	///     <c>!mp help</c>, which do not operate on an existing match at all.
	/// </param>
	/// <param name="channelName">
	///     The resolved internal channel name the message was sent in (e.g. <c>#lobby</c>, or a match's
	///     <see cref="MatchSession.ChatChannelName" />), or <see langword="null" /> for a private
	///     message to the bot. Drives the channel-eligibility rules: <c>#lobby</c> only reaches
	///     <c>!mp make</c> and <c>!mp makeprivate</c>, and <c>!mp in</c> is rejected from inside the
	///     sender's own match channel (see <see cref="CommandDispatcher" />).
	/// </param>
	/// <param name="sink">The destination for this dispatch's reply, if any.</param>
	/// <param name="prefixOptional">
	///     When <see langword="true" />, a message with no command prefix is treated as if it had one
	///     (for example "help" behaves like "!help"). Only safe for private messages to the bot: every
	///     DM to the bot is already a command-dispatch attempt with no other fallback (see
	///     <see cref="Basil.Application.PacketHandlers.Channels.SendPrivateMessageHandler" />), so
	///     relaxing the prefix there does not risk swallowing ordinary chat.
	/// </param>
	/// <param name="cancellationToken">
	///     Propagated to the repository or service calls the matched command needs.
	/// </param>
	Task<bool> DispatchAsync(PlayerSession sender, string rawMessage, MatchSession? matchScope, string? channelName,
		ICommandReplySink sink, bool prefixOptional = false, CancellationToken cancellationToken = default);
}