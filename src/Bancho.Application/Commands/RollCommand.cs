using Bancho.Domain;

namespace Bancho.Application.Commands;

/// <summary>Ported from app/commands.py's roll.</summary>
public sealed class RollCommand : ICommand
{
    private const int MaxRollCeiling = 0x7FFF;

    public string Trigger => "roll";

    public IReadOnlyList<string> Aliases => [];

    public Privileges RequiredPriv => Privileges.Unrestricted;

    public bool Hidden => false;

    public string Description => "Roll an n-sided die where n is the number you write (100 default).";

    public Task<string?> HandleAsync(CommandContext ctx)
    {
        var maxRoll = ctx.Args.Count > 0 && int.TryParse(ctx.Args[0], out var parsed)
            ? Math.Min(parsed, MaxRollCeiling)
            : 100;

        if (maxRoll == 0)
        {
            return Task.FromResult<string?>("Roll what?");
        }

        var points = Random.Shared.Next(0, maxRoll);
        return Task.FromResult<string?>($"{ctx.Player.Name} rolls {points} points!");
    }
}
