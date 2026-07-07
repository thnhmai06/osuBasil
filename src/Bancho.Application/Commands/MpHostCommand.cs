using Bancho.Application.Sessions;
using Bancho.Application.UseCases.Multiplayer;
using Bancho.Domain;
using Bancho.Protocol;

namespace Bancho.Application.Commands;

/// <summary>Ported from app/commands.py's mp_host.</summary>
public sealed class MpHostCommand(IPlayerSessionRegistry sessionRegistry, MatchMembershipService matchMembership) : IMpSubCommand
{
    public string Trigger => "host";

    public IReadOnlyList<string> Aliases => [];

    public Privileges RequiredPriv => Privileges.Unrestricted;

    public bool Hidden => false;

    public string? Description => "Set the current match's current host by id.";

    public async Task<string?> HandleAsync(MpCommandContext ctx)
    {
        if (ctx.Args.Count != 1)
        {
            return "Invalid syntax: !mp host <name>";
        }

        var target = sessionRegistry.GetByName(ctx.Args[0]);
        if (target is null)
        {
            return "Could not find a user by that name.";
        }

        var match = ctx.Match;
        await match.Lock.WaitAsync();
        try
        {
            if (target.Id == match.HostId)
            {
                return "They're already host, silly!";
            }

            if (match.GetSlot(target.Id) is null)
            {
                return "Found no such player in the match.";
            }

            match.HostId = target.Id;
            target.Enqueue(ServerPacketWriter.MatchTransferHost());
            matchMembership.EnqueueState(match, lobby: true);
        }
        finally
        {
            match.Lock.Release();
        }

        return "Match host updated.";
    }
}
