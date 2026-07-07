using Bancho.Application.Sessions;
using Bancho.Domain;

namespace Bancho.Application.Commands;

/// <summary>Ported from app/commands.py's mp_listref (Match.refs = host + added referees).</summary>
public sealed class MpListRefCommand(IPlayerSessionRegistry sessionRegistry) : IMpSubCommand
{
    public string Trigger => "listref";

    public IReadOnlyList<string> Aliases => [];

    public Privileges RequiredPriv => Privileges.Unrestricted;

    public bool Hidden => false;

    public string? Description => "List all referees from the current match.";

    public Task<string?> HandleAsync(MpCommandContext ctx)
    {
        var match = ctx.Match;
        var refIds = new[] { match.HostId }.Concat(match.Referees).Distinct();
        var names = refIds.Select(id => sessionRegistry.GetById(id)?.Name ?? $"player #{id}");
        return Task.FromResult<string?>(string.Join(", ", names) + ".");
    }
}
