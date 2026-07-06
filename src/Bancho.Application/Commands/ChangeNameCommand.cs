using System.Text.RegularExpressions;
using Bancho.Application.Abstractions;
using Bancho.Application.Configuration;
using Bancho.Application.Sessions;
using Bancho.Domain;
using Bancho.Protocol;
using Microsoft.Extensions.Options;

namespace Bancho.Application.Commands;

/// <summary>Ported from app/commands.py's changename (regex from app/constants/regexes.py's USERNAME).</summary>
public sealed partial class ChangeNameCommand(
    IUserRepository users,
    PlayerLogoutService logoutService,
    IOptions<RegistrationOptions> registrationOptions) : ICommand
{
    public string Trigger => "changename";

    public IReadOnlyList<string> Aliases => [];

    public Privileges RequiredPriv => Privileges.Supporter;

    public bool Hidden => false;

    public string Description => "Change your username.";

    [GeneratedRegex(@"^[\w \[\]-]{2,15}$")]
    private static partial Regex UsernamePattern();

    public async Task<string?> HandleAsync(CommandContext ctx)
    {
        var name = string.Join(' ', ctx.Args).Trim();

        if (!UsernamePattern().IsMatch(name))
        {
            return "Must be 2-15 characters in length.";
        }

        if (name.Contains('_') && name.Contains(' '))
        {
            return "May contain \"_\" and \" \", but not both.";
        }

        if (registrationOptions.Value.DisallowedNames.Contains(name))
        {
            return "Disallowed username; pick another.";
        }

        if (await users.FetchByNameAsync(name) is not null)
        {
            return "Username already taken by another player.";
        }

        await users.UpdateNameAsync(ctx.Player.Id, name, SafeName.Make(name));

        ctx.Player.Enqueue(ServerPacketWriter.Notification($"Your username has been changed to {name}!"));
        logoutService.Logout(ctx.Player);

        return null;
    }
}
