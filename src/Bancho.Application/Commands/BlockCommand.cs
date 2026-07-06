using Bancho.Application.Abstractions;
using Bancho.Application.Sessions;
using Bancho.Domain;

namespace Bancho.Application.Commands;

/// <summary>Ported from app/commands.py's block.</summary>
public sealed class BlockCommand(IPlayerSessionRegistry sessionRegistry, IUserRepository users, IRelationshipRepository relationships) : ICommand
{
    public string Trigger => "block";

    public IReadOnlyList<string> Aliases => [];

    public Privileges RequiredPriv => Privileges.Unrestricted;

    public bool Hidden => true;

    public string Description => "Block another user from communicating with you.";

    public async Task<string?> HandleAsync(CommandContext ctx)
    {
        var target = await CommandTargetResolver.ResolveAsync(sessionRegistry, users, string.Join(' ', ctx.Args));
        if (target is null)
        {
            return "User not found.";
        }

        var (targetId, targetName) = target.Value;
        if (targetId == CommandTargetResolver.BotId || targetId == ctx.Player.Id)
        {
            return "What?";
        }

        var existing = await relationships.FetchOneAsync(ctx.Player.Id, targetId);
        if (existing?.Type == RelationshipType.Block)
        {
            return $"{targetName} already blocked!";
        }

        if (existing?.Type == RelationshipType.Friend)
        {
            await relationships.DeleteAsync(ctx.Player.Id, targetId);
        }

        await relationships.CreateAsync(ctx.Player.Id, targetId, RelationshipType.Block);
        return $"Added {targetName} to blocked users.";
    }
}
