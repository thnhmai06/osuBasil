using Bancho.Application.UseCases.Multiplayer;
using Bancho.Domain;

namespace Bancho.Application.Commands;

/// <summary>Ported from app/commands.py's mp_freemods.</summary>
public sealed class MpFreemodsCommand(MatchMembershipService matchMembership) : IMpSubCommand
{
    public string Trigger => "freemods";

    public IReadOnlyList<string> Aliases => ["fm", "fmods"];

    public Privileges RequiredPriv => Privileges.Unrestricted;

    public bool Hidden => false;

    public string? Description => "Toggle freemods status for the match.";

    public async Task<string?> HandleAsync(MpCommandContext ctx)
    {
        if (ctx.Args.Count != 1 || ctx.Args[0] is not ("on" or "off"))
        {
            return "Invalid syntax: !mp freemods <on/off>";
        }

        var match = ctx.Match;
        await match.Lock.WaitAsync();
        try
        {
            if (ctx.Args[0] == "on")
            {
                match.Freemods = true;
                foreach (var slot in match.Slots)
                {
                    if (slot.PlayerId is not null)
                    {
                        slot.Mods = match.Mods & ~ModsExtensions.SpeedChangingMods;
                    }
                }

                match.Mods &= ModsExtensions.SpeedChangingMods;
            }
            else
            {
                match.Freemods = false;
                var hostSlot = match.GetHostSlot();
                match.Mods &= ModsExtensions.SpeedChangingMods;
                if (hostSlot is not null)
                {
                    match.Mods |= hostSlot.Mods;
                }

                foreach (var slot in match.Slots)
                {
                    if (slot.PlayerId is not null)
                    {
                        slot.Mods = Mods.NoMod;
                    }
                }
            }

            matchMembership.EnqueueState(match);
        }
        finally
        {
            match.Lock.Release();
        }

        return "Match freemod status updated.";
    }
}
