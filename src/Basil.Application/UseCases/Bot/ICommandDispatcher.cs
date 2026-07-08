using Basil.Application.Sessions;
using Basil.Application.Sessions.Multiplayer;

namespace Basil.Application.UseCases.Bot;

/// <summary>
///     Fresh command-dispatch layer for BanchoBot — deliberately not a resurrection of the deleted
///     ICommand/MpCommandDispatcher architecture (see docs/scope-decisions.md). Returns the bot's
///     reply text, or null when the message isn't a recognized command (or fails a permission check,
///     matching bancho.py's silent-ignore behavior for unauthorized !mp use).
/// </summary>
public interface ICommandDispatcher
{
    /// <param name="matchScope">
    ///     The sender's current match, but ONLY when the message was sent in that match's own chat
    ///     channel — null otherwise (including for private messages, which are never a match channel).
    ///     Every `!mp` subcommand requires this to be non-null, mirroring bancho.py's ensure_match
    ///     check — EXCEPT `!mp make`/`!mp makeprivate`, which create the match and so are the one pair
    ///     of subcommands reachable with a null scope (e.g. via PM to the bot).
    /// </param>
    /// <param name="prefixOptional">
    ///     When true, a message with no command prefix is treated as if it had one (e.g. "help" behaves
    ///     like "!help"). Only safe for private messages to the bot — every DM to the bot is already a
    ///     command-dispatch attempt with no other fallback (see <see cref="SendPrivateMessageHandler" />),
    ///     so relaxing the prefix there doesn't risk swallowing ordinary chat.
    /// </param>
    Task<string?> DispatchAsync(PlayerSession sender, string rawMessage, MatchSession? matchScope,
        bool prefixOptional = false, CancellationToken cancellationToken = default);
}