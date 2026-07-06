using Bancho.Application.Sessions;
using Bancho.Domain;

namespace Bancho.Application.Commands;

/// <summary>
/// A single bot command. Ported from app/commands.py's @command-decorated functions — trigger,
/// aliases, and required privilege are exposed as properties here (registered via DI as
/// IEnumerable&lt;ICommand&gt;) instead of bancho.py's decorator + reflection registry.
/// </summary>
public interface ICommand
{
    /// <summary>The command name without the server's command prefix, e.g. "help".</summary>
    string Trigger { get; }

    IReadOnlyList<string> Aliases { get; }

    Privileges RequiredPriv { get; }

    /// <summary>When true, the response is only shown to staff members instead of the whole channel.</summary>
    bool Hidden { get; }

    /// <summary>Ported from the command function's docstring — shown by !help. Null excludes the command from !help's listing.</summary>
    string? Description { get; }

    Task<string?> HandleAsync(CommandContext ctx);
}

/// <summary>Ported from app/commands.py's Context dataclass (player, trigger, args, recipient).</summary>
public sealed record CommandContext(PlayerSession Player, IReadOnlyList<string> Args, ChannelSession? Channel, PlayerSession? PmTarget);
