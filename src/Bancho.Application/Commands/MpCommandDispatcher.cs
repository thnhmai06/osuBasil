using Bancho.Application.Sessions;
using Bancho.Domain;

namespace Bancho.Application.Commands;

/// <summary>
/// Ported from app/commands.py's process_commands' CommandSet-matching branch (for trigger "mp")
/// plus the ensure_match decorator that wraps every mp_* handler. Registered as a regular
/// ICommand with Trigger "mp" so it slots into the existing flat CommandDispatcher unmodified;
/// all of the real subcommand routing and match-membership gating happens inside HandleAsync.
///
/// ensure_match's three checks are hoisted here (once) rather than duplicated per subcommand,
/// since they're identical for every mp_* function in the Python source: must be in a match; the
/// command must be sent in that match's own chat channel; and the player must be a referee (which
/// includes the host, see MatchSession.IsReferee) or a tourney manager — except for "help", which
/// bypasses only this last check (`f is mp_help.__wrapped__` in the Python source), still subject
/// to the first two.
/// </summary>
public sealed class MpCommandDispatcher : ICommand
{
    private readonly Dictionary<string, IMpSubCommand> _byTrigger;

    public MpCommandDispatcher(IEnumerable<IMpSubCommand> subCommands)
    {
        _byTrigger = new Dictionary<string, IMpSubCommand>(StringComparer.OrdinalIgnoreCase);
        foreach (var subCommand in subCommands)
        {
            _byTrigger[subCommand.Trigger] = subCommand;
            foreach (var alias in subCommand.Aliases)
            {
                _byTrigger[alias] = subCommand;
            }
        }
    }

    public string Trigger => "mp";

    public IReadOnlyList<string> Aliases => [];

    public Privileges RequiredPriv => Privileges.Unrestricted;

    public bool Hidden => false;

    public string? Description => "Multiplayer commands.";

    public async Task<string?> HandleAsync(CommandContext ctx)
    {
        IReadOnlyList<string> args = ctx.Args.Count == 0 ? ["help"] : ctx.Args;
        var subTrigger = args[0];
        var subArgs = args.Skip(1).ToList();

        if (!_byTrigger.TryGetValue(subTrigger, out var subCommand))
        {
            return null;
        }

        if ((ctx.Player.Priv & subCommand.RequiredPriv) != subCommand.RequiredPriv)
        {
            return null;
        }

        var match = ctx.Player.Match;
        if (match is null)
        {
            return null;
        }

        if (ctx.Channel is null || ctx.Channel.Name != match.ChatChannelName)
        {
            return null;
        }

        var isHelp = subCommand.Trigger.Equals("help", StringComparison.OrdinalIgnoreCase);
        if (!isHelp && !match.IsReferee(ctx.Player.Id) && (ctx.Player.Priv & Privileges.TourneyManager) == 0)
        {
            return null;
        }

        return await subCommand.HandleAsync(new MpCommandContext(ctx.Player, subArgs, match));
    }
}
