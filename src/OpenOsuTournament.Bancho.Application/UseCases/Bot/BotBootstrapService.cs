using Microsoft.Extensions.Options;
using OpenOsuTournament.Bancho.Application.Abstractions;
using OpenOsuTournament.Bancho.Application.Abstractions.Channels;
using OpenOsuTournament.Bancho.Application.Abstractions.Users;
using OpenOsuTournament.Bancho.Application.Configuration;
using OpenOsuTournament.Bancho.Application.Sessions;
using OpenOsuTournament.Bancho.Application.Sessions.Channels;
using OpenOsuTournament.Bancho.Domain.Users;

namespace OpenOsuTournament.Bancho.Application.UseCases.Bot;

/// <summary>
///     Boots the seeded id=1 BanchoBot row into an in-memory PlayerSession at startup, so chat/mp
///     commands have a real sender identity to reply from. Not a login — there is no client
///     connection behind this session, so it skips the normal handshake entirely.
/// </summary>
public sealed class BotBootstrapService(
    IUserRepository users,
    IPlayerSessionRegistry sessionRegistry,
    IChannelRegistry channelRegistry,
    IOptions<BotOptions> botOptions,
    IClock clock)
{
    public const int BotId = 1;
    private const string BotToken = "bancho-bot-session";

    public async Task<PlayerSession?> BootstrapAsync(CancellationToken cancellationToken = default)
    {
        var user = await users.FetchByIdAsync(BotId, cancellationToken);
        if (user is null) return null;

        var configuredName = botOptions.Value.Name;
        if (user.Name != configuredName)
            await users.UpdateNameAsync(BotId, configuredName, SafeName.Make(configuredName), cancellationToken);

        var loginTime = clock.UtcNow.ToUnixTimeSeconds();
        var session = new PlayerSession(BotId, configuredName, BotToken, (Privileges)user.Priv, loginTime)
        {
            IsBot = true
        };

        foreach (var channel in channelRegistry.AutoJoinChannels)
        {
            channel.Join(BotId);
            session.JoinChannel(channel.Name);
        }

        sessionRegistry.Add(session);
        return session;
    }
}
