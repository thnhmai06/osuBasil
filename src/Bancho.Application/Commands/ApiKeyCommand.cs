using Bancho.Application.Abstractions;
using Bancho.Domain;

namespace Bancho.Application.Commands;

/// <summary>Ported from app/commands.py's apikey.</summary>
public sealed class ApiKeyCommand(IUserRepository users) : ICommand
{
    private const string BotName = "BanchoBot";

    public string Trigger => "apikey";

    public IReadOnlyList<string> Aliases => [];

    public Privileges RequiredPriv => Privileges.Unrestricted;

    public bool Hidden => false;

    public string Description => "Generate a new api key & assign it to the player.";

    public async Task<string?> HandleAsync(CommandContext ctx)
    {
        if (ctx.PmTarget is null)
        {
            return $"Command only available in DMs with {BotName}.";
        }

        var apiKey = Guid.NewGuid().ToString();
        await users.UpdateApiKeyAsync(ctx.Player.Id, apiKey);

        return $"API key generated. Copy your api key from (this url)[http://{apiKey}].";
    }
}
