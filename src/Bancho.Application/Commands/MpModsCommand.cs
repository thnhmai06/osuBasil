using Bancho.Application.UseCases.Multiplayer;
using Bancho.Domain;

namespace Bancho.Application.Commands;

/// <summary>
/// Ported from app/commands.py's mp_mods. Diverges from Python in one place for safety: Python
/// does `slot = match.get_slot(ctx.player); assert slot is not None`, which would crash for a
/// tourney manager running this while not occupying a slot (referees are guaranteed a slot by
/// mp_addref's own check, but tourney managers aren't). This port simply skips the per-slot mods
/// update when the caller has no slot, rather than crashing.
/// </summary>
public sealed class MpModsCommand(MatchMembershipService matchMembership) : IMpSubCommand
{
    public string Trigger => "mods";

    public IReadOnlyList<string> Aliases => [];

    public Privileges RequiredPriv => Privileges.Unrestricted;

    public bool Hidden => false;

    public string? Description => "Set the current match's mods, from string form.";

    public async Task<string?> HandleAsync(MpCommandContext ctx)
    {
        if (ctx.Args.Count != 1 || ctx.Args[0].Length % 2 != 0)
        {
            return "Invalid syntax: !mp mods <mods>";
        }

        var match = ctx.Match;
        var mods = ModsExtensions.FromModString(ctx.Args[0]).FilterInvalidCombos(match.Mode.AsVanilla());

        await match.Lock.WaitAsync();
        try
        {
            if (match.Freemods)
            {
                if (ctx.Player.Id == match.HostId)
                {
                    match.Mods = mods & ModsExtensions.SpeedChangingMods;
                }

                var slot = match.GetSlot(ctx.Player.Id);
                if (slot is not null)
                {
                    slot.Mods = mods & ~ModsExtensions.SpeedChangingMods;
                }
            }
            else
            {
                match.Mods = mods;
            }

            matchMembership.EnqueueState(match);
        }
        finally
        {
            match.Lock.Release();
        }

        return "Match mods updated.";
    }
}
