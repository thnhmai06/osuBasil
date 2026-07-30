using Basil.Application.Abstractions.Users;
using Basil.Application.Configuration;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Domain.Login;
using Basil.Domain.Users;
using Microsoft.Extensions.Logging;
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
	IOptions<BotOptions> botOptions,
	ILogger<BotBootstrapService> logger)
{
	public const int BotId = 0;
	private const string BotToken = "bancho-bot-session";

	public async Task<PlayerSession?> BootstrapAsync(CancellationToken cancellationToken = default)
	{
		var user = await users.FetchByIdAsync(BotId, cancellationToken);
		if (user is null)
		{
			logger.LogWarning("BasilBot user row (id=0) missing — chat bot unavailable");
			return null;
		}

		var configuredName = botOptions.Value.Name;
		if (user.Name != configuredName)
			await users.UpdateNameAsync(BotId, configuredName, User.MakeSafeName(configuredName), cancellationToken);

		var configuredCountry = botOptions.Value.Country;
		if (!string.Equals(user.Country.ToAcronym(), configuredCountry, StringComparison.OrdinalIgnoreCase))
		{
			var country = Enum.TryParse<Country>(configuredCountry, true, out var parsedCountry)
				? parsedCountry
				: Country.Xx;
			await users.UpdateCountryAsync(BotId, country, cancellationToken);
		}

		var loginTime = DateTimeOffset.UtcNow;
		var session = new PlayerSession(BotId, configuredName, BotToken, user.Privilege, loginTime)
		{
			IsBot = true
		};

		foreach (var channel in channelRegistry.AutoJoinChannels)
		{
			channel.Join(BotId);
			session.JoinChannel(channel.Name);
		}

		sessionRegistry.Add(session);
		logger.LogInformation("Bot session created: BotId={BotId}", BotId);
		return session;
	}
}