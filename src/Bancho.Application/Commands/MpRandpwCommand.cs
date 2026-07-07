using System.Security.Cryptography;
using Bancho.Domain;

namespace Bancho.Application.Commands;

/// <summary>Ported from app/commands.py's mp_randpw (secrets.token_hex(8) -> 16 lowercase hex chars).</summary>
public sealed class MpRandpwCommand : IMpSubCommand
{
    public string Trigger => "randpw";

    public IReadOnlyList<string> Aliases => [];

    public Privileges RequiredPriv => Privileges.Unrestricted;

    public bool Hidden => false;

    public string? Description => "Randomize the current match's password.";

    public async Task<string?> HandleAsync(MpCommandContext ctx)
    {
        var match = ctx.Match;
        await match.Lock.WaitAsync();
        try
        {
            match.Password = RandomNumberGenerator.GetHexString(16, lowercase: true);
        }
        finally
        {
            match.Lock.Release();
        }

        return "Match password randomized.";
    }
}
