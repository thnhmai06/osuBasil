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
///     Boots the seeded id=0 bot user into an in-memory <see cref="PlayerSession" /> at startup.
/// </summary>
/// <remarks>
///     This gives chat and <c>!mp</c> commands a real sender identity to reply from. It is not a
///     login: no client connection sits behind this session, so the normal handshake is skipped
///     entirely and the session is registered directly with <see cref="IPlayerSessionRegistry" />.
/// </remarks>
public sealed class BotBootstrapService(
	IUserRepository users,
	IPlayerSessionRegistry sessionRegistry,
	IChannelRegistry channelRegistry,
	IOptions<BotOptions> botOptions,
	ILogger<BotBootstrapService> logger)
{
	public const int BotId = 0;
	private const string BotToken = "bancho-bot-session";

	/// <summary>
	///     Creates the bot's session, synchronizing its stored name and country with the configured
	///     options and joining it to every auto-join channel.
	/// </summary>
	/// <param name="cancellationToken">The cancellation token to observe.</param>
	/// <returns>
	///     The bot's <see cref="PlayerSession" />, or <see langword="null" /> when the seeded user row
	///     is missing.
	/// </returns>
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