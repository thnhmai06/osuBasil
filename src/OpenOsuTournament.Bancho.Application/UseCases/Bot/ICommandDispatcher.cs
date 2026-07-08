using OpenOsuTournament.Bancho.Application.Sessions;
using OpenOsuTournament.Bancho.Application.Sessions.Multiplayer;

namespace OpenOsuTournament.Bancho.Application.UseCases.Bot;

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
    ///     `!mp` commands require this to be non-null, mirroring bancho.py's ensure_match check.
    /// </param>
    Task<string?> DispatchAsync(PlayerSession sender, string rawMessage, MatchSession? matchScope,
        CancellationToken cancellationToken = default);
}
