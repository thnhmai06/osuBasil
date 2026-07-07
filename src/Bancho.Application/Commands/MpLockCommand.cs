using Bancho.Application.UseCases.Multiplayer;
using Bancho.Domain;

namespace Bancho.Application.Commands;

/// <summary>Ported from app/commands.py's mp_lock.</summary>
public sealed class MpLockCommand(MatchMembershipService matchMembership) : IMpSubCommand
{
    public string Trigger => "lock";

    public IReadOnlyList<string> Aliases => [];

    public Privileges RequiredPriv => Privileges.Unrestricted;

    public bool Hidden => false;

    public string? Description => "Lock all unused slots in the current match.";

    public async Task<string?> HandleAsync(MpCommandContext ctx)
    {
        var match = ctx.Match;
        await match.Lock.WaitAsync();
        try
        {
            foreach (var slot in match.Slots)
            {
                if (slot.Status == SlotStatus.Open)
                {
                    slot.Status = SlotStatus.Locked;
                }
            }

            matchMembership.EnqueueState(match);
        }
        finally
        {
            match.Lock.Release();
        }

        return "All unused slots locked.";
    }
}
