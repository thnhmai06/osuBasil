using Bancho.Application.UseCases.Multiplayer;
using Bancho.Domain;

namespace Bancho.Application.Commands;

/// <summary>
/// Ported from app/commands.py's mp_start. Drops the delayed-start ("!mp start &lt;seconds&gt;")
/// and "!mp start cancel" branches — those need a cancellable scheduler (Python's
/// loop.call_later) that doesn't exist in this port yet and isn't otherwise needed; flagged in
/// note.md as a deferred feature rather than built speculatively.
/// </summary>
public sealed class MpStartCommand(MatchMembershipService matchMembership) : IMpSubCommand
{
    public string Trigger => "start";

    public IReadOnlyList<string> Aliases => ["st"];

    public Privileges RequiredPriv => Privileges.Unrestricted;

    public bool Hidden => false;

    public string? Description => "Start the current multiplayer match, with any players ready.";

    public async Task<string?> HandleAsync(MpCommandContext ctx)
    {
        if (ctx.Args.Count > 1)
        {
            return "Invalid syntax: !mp start <force>";
        }

        var force = ctx.Args.Count == 1 && ctx.Args[0] is "force" or "f";
        if (ctx.Args.Count == 1 && !force)
        {
            return "Delayed match starts aren't supported on this server. Use `!mp start` or `!mp start force`.";
        }

        var match = ctx.Match;
        await match.Lock.WaitAsync();
        try
        {
            if (!force && match.Slots.Any(s => s.Status == SlotStatus.NotReady))
            {
                return "Not all players are ready (`!mp start force` to override).";
            }

            matchMembership.Start(match);
        }
        finally
        {
            match.Lock.Release();
        }

        return "Good luck!";
    }
}
