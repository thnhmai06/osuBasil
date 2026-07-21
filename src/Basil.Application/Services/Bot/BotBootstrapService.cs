using Basil.Application.Abstractions.Users;
using Basil.Application.Configuration;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Domain.Login;
using Basil.Domain.Users;
using Microsoft.Extensions.Options;

namespace Basil.Application.Services.Bot;

/// <summary>
///     Boots the seeded id=0 BanchoBot row into an in-memory PlayerSession at startup, so chat/mp
///     commands have a real sender identity to reply from. Not a login — there is no client
///     connection behind this session, so it skips the normal handshake entirely.
/// </summary>
public sealed class BotBootstrapService(
    IUserRepository users,
    IPlayerSessionRegistry sessionRegistry,
    IChannelRegistry channelRegistry,
    IOptions<BotOptions> botOptions)
{
    public const int BotId = 0;
    private const string BotToken = "bancho-bot-session";

    public async Task<PlayerSession?> BootstrapAsync(CancellationToken cancellationToken = default)
    {
        var user = await users.FetchByIdAsync(BotId, cancellationToken);
        if (user is null) return null;

        var configuredName = botOptions.Value.Name;
        if (user.Name != configuredName)
            await users.UpdateNameAsync(BotId, configuredName, User.MakeSafeName(configuredName), cancellationToken);

        var configuredCountry = botOptions.Value.Country;
        if (!string.Equals(user.Country.ToAcronym(), configuredCountry, StringComparison.OrdinalIgnoreCase))
            await users.UpdateCountryAsync(BotId, configuredCountry, cancellationToken);

        var loginTime = DateTimeOffset.UtcNow;
        var session = new PlayerSession(BotId, configuredName, BotToken, user.Priv, loginTime)
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