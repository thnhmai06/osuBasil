using Bancho.Application.Sessions;
using Bancho.Domain;
using Bancho.Protocol;

namespace Bancho.Application.Commands;

/// <summary>Ported from app/commands.py's mp_invite.</summary>
public sealed class MpInviteCommand(IPlayerSessionRegistry sessionRegistry) : IMpSubCommand
{
    public string Trigger => "invite";

    public IReadOnlyList<string> Aliases => ["inv"];

    public Privileges RequiredPriv => Privileges.Unrestricted;

    public bool Hidden => false;

    public string? Description => "Invite a player to the current match by name.";

    public Task<string?> HandleAsync(MpCommandContext ctx)
    {
        if (ctx.Args.Count != 1)
        {
            return Task.FromResult<string?>("Invalid syntax: !mp invite <name>");
        }

        var target = sessionRegistry.GetByName(ctx.Args[0]);
        if (target is null)
        {
            return Task.FromResult<string?>("Could not find a user by that name.");
        }

        if (target.Id == CommandTargetResolver.BotId)
        {
            return Task.FromResult<string?>("I'm too busy!");
        }

        if (target.Id == ctx.Player.Id)
        {
            return Task.FromResult<string?>("You can't invite yourself!");
        }

        target.Enqueue(ServerPacketWriter.MatchInvite(ctx.Player.Id, ctx.Player.Name, ctx.Match.Embed, target.Name));
        return Task.FromResult<string?>($"Invited {target.Name} to the match.");
    }
}
