using Bancho.Application.Sessions;

namespace Bancho.Application.Commands;

/// <summary>
/// Ported from app/commands.py's process_commands: splits trigger + args, looks up by
/// trigger/alias, gates on privilege, invokes. Unknown triggers and insufficient privilege are
/// both silent no-ops (no error message) — matches the low-noise posture of the rest of the
/// chat handlers; unconfirmed against a real client yet, same caveat as other Phase 4 edge cases.
/// </summary>
public sealed class CommandDispatcher : ICommandDispatcher
{
    private readonly Dictionary<string, ICommand> _byTrigger;

    public CommandDispatcher(IEnumerable<ICommand> commands)
    {
        _byTrigger = new Dictionary<string, ICommand>(StringComparer.OrdinalIgnoreCase);
        foreach (var command in commands)
        {
            _byTrigger[command.Trigger] = command;
            foreach (var alias in command.Aliases)
            {
                _byTrigger[alias] = command;
            }
        }
    }

    public async Task<CommandDispatchResult?> DispatchAsync(PlayerSession player, string commandText, ChannelSession? channel, PlayerSession? pmTarget)
    {
        var parts = commandText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        if (!_byTrigger.TryGetValue(parts[0], out var command))
        {
            return null;
        }

        if ((player.Priv & command.RequiredPriv) != command.RequiredPriv)
        {
            return null;
        }

        var ctx = new CommandContext(player, parts[1..], channel, pmTarget);
        var response = await command.HandleAsync(ctx);
        return new CommandDispatchResult(response, command.Hidden);
    }
}
