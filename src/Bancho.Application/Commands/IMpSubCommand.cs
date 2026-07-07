using Bancho.Application.Sessions;
using Bancho.Domain;

namespace Bancho.Application.Commands;

/// <summary>
/// A single "!mp" subcommand. Ported from app/commands.py's mp_*-decorated functions — trigger,
/// aliases, and required privilege are exposed as properties (registered via DI as
/// IEnumerable&lt;IMpSubCommand&gt;), mirroring how CommandSet.add's decorator built up
/// mp_commands.commands, but without the reflection-based trigger-from-function-name trick.
/// </summary>
public interface IMpSubCommand
{
    /// <summary>The subcommand name following "mp ", e.g. "start" for "!mp start".</summary>
    string Trigger { get; }

    IReadOnlyList<string> Aliases { get; }

    Privileges RequiredPriv { get; }

    bool Hidden { get; }

    /// <summary>Ported from the mp_* function's docstring — shown by !mp help. Null excludes the subcommand from that listing.</summary>
    string? Description { get; }

    Task<string?> HandleAsync(MpCommandContext ctx);
}

/// <summary>Ported from app/commands.py's ensure_match wrapper's (ctx, match) callback signature.</summary>
public sealed record MpCommandContext(PlayerSession Player, IReadOnlyList<string> Args, MatchSession Match);
