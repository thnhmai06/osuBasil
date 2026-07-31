using Basil.Application.Sessions;
using Basil.Application.Sessions.Multiplayer;

namespace Basil.Application.Services.Bot;

/// <summary>
///     Fresh command-dispatch layer for BanchoBot — deliberately not a resurrection of the deleted
///     ICommand/MpCommandDispatcher architecture (see docs/working-scopes.md). Sends its own reply
///     through <c>sink</c> as it runs — see <see cref="ICommandReplySink" /> — and
///     returns only whether the message was a recognized, successfully-run command (matching
///     bancho.py's silent-ignore behavior for unrecognized text or an unauthorized `!mp` subcommand).
/// </summary>
public interface ICommandDispatcher
{
	/// <param name="sender">The player who sent the message.</param>
	/// <param name="rawMessage">The message text exactly as sent, prefix included if present.</param>
	/// <param name="matchScope">
	///     The sender's current match, but ONLY when the message was sent in that match's own chat
	///     channel — null otherwise (including for private messages, which are never a match channel).
	///     Every `!mp` subcommand requires this to be non-null, mirroring bancho.py's ensure_match
	///     check — EXCEPT `!mp make`/`!mp makeprivate`/`!mp join`/`!mp in`/`!mp help`, which don't
	///     operate on an existing scope at all.
	/// </param>
	/// <param name="channelName">
	///     The resolved internal channel name the message was sent in (e.g. <c>#lobby</c>, or a match's
	///     <see cref="MatchSession.ChatChannelName" />) — null for a private message to the bot. Drives
	///     the channel-eligibility rules: `#lobby` only reaches `!mp make`/`!mp makeprivate`, and `!mp in`
	///     is rejected from inside the sender's own match channel (see <see cref="CommandDispatcher" />).
	/// </param>
	/// <param name="sink">Where this dispatch's reply (if any) is sent — see <see cref="ICommandReplySink" />.</param>
	/// <param name="prefixOptional">
	///     When true, a message with no command prefix is treated as if it had one (e.g. "help" behaves
	///     like "!help"). Only safe for private messages to the bot — every DM to the bot is already a
	///     command-dispatch attempt with no other fallback (see
	///     <see cref="Basil.Application.PacketHandlers.Channels.SendPrivateMessageHandler" />), so
	///     relaxing the prefix there doesn't risk swallowing ordinary chat.
	/// </param>
	/// <param name="cancellationToken">Propagated to whatever repository/service calls the matched command needs.</param>
	Task<bool> DispatchAsync(PlayerSession sender, string rawMessage, MatchSession? matchScope, string? channelName,
		ICommandReplySink sink, bool prefixOptional = false, CancellationToken cancellationToken = default);
}
