using Bancho.Application.Sessions;

namespace Bancho.Application.Commands;

/// <summary>
/// Ported from app/commands.py's process_commands — the port consumed by the public/private
/// message handlers. Trigger + arg parsing, command lookup, and privilege gating are all
/// implementation details of whatever implements this (see the concrete CommandDispatcher).
/// </summary>
public interface ICommandDispatcher
{
    /// <param name="commandText">The message text with the command prefix already stripped.</param>
    /// <param name="channel">Set when the command was sent in a channel; null for a DM to the bot.</param>
    /// <param name="pmTarget">Set when the command was sent via DM to the bot; null for a channel message.</param>
    Task<CommandDispatchResult?> DispatchAsync(PlayerSession player, string commandText, ChannelSession? channel, PlayerSession? pmTarget);
}

/// <summary>Ported from process_commands' {"resp": str | None, "hidden": bool} return shape.</summary>
public sealed record CommandDispatchResult(string? Response, bool Hidden);
