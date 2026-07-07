using Bancho.Domain;

namespace Bancho.Application.Commands;

/// <summary>Ported from app/commands.py's mp_endscrim.</summary>
public sealed class MpEndScrimCommand : IMpSubCommand
{
    public string Trigger => "endscrim";

    public IReadOnlyList<string> Aliases => ["end"];

    public Privileges RequiredPriv => Privileges.Unrestricted;

    public bool Hidden => false;

    public string? Description => "End the current matches ongoing scrim.";

    public async Task<string?> HandleAsync(MpCommandContext ctx)
    {
        var match = ctx.Match;
        await match.Lock.WaitAsync();
        try
        {
            if (!match.IsScrimming)
            {
                return "Not currently scrimming!";
            }

            match.IsScrimming = false;
            match.ResetScrim();
        }
        finally
        {
            match.Lock.Release();
        }

        return "Scrimmage ended.";
    }
}
