using Basil.Application.Abstractions.Users;
using Basil.Application.Configurations;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Domain.Login;
using Basil.Domain.Users;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Basil.Application.Services.Bot;

/// <summary>
///     Boots the seeded id=0 bot user into an in-memory <see cref="GameSession" /> at startup.
/// </summary>
/// <remarks>
///     This gives chat and <c>!mp</c> commands a real sender identity to reply from, and — because it
///     is a <see cref="GameSession" /> — lets it hold <see cref="GameSession.Spectating" />
///     relationships so its watch of every online userSession can be exposed over SSE. It is not a
///     login: no client connection sits behind this session, and it never occupies a multiplayer
///     slot (see the <c>IsBot</c> guards in <c>MatchMembershipService</c>). The normal handshake is
///     skipped entirely and the session is registered directly with
///     <see cref="ISessionRegistry{TSession}" />.
/// </remarks>
public sealed class BotBootstrapService(
	IUserRepository users,
	ISessionRegistry<GameSession> sessionRegistry,
	IChannelRegistry channelRegistry,
	ChannelMembershipService channelMembership,
	IOptions<BotOptions> botOptions,
	ILogger<BotBootstrapService> logger)
{
	public const int BotId = SystemUserIds.BasilBot;
	private const string BotToken = "bancho-bot-session";

	/// <summary>
	///     Creates the bot's session, synchronizing its stored name and country with the configured
	///     options and joining it to every auto-join channel.
	/// </summary>
	/// <param name="cancellationToken">The cancellation token to observe.</param>
	/// <returns>
	///     The bot's <see cref="GameSession" />, or <see langword="null" /> when the seeded user row
	///     is missing.
	/// </returns>
	public async Task<GameSession?> BootstrapAsync(CancellationToken cancellationToken = default)
	{
		var user = await users.FetchByIdAsync(BotId, cancellationToken);
		if (user is null)
		{
			logger.LogError("BasilBot user row (id={Id}) missing — chat bot unavailable", BotId);
			return null;
		}

		var configuredName = botOptions.Value.Name;
		if (user.Name != configuredName)
			await users.UpdateNameAsync(BotId, configuredName, User.MakeSafeName(configuredName), cancellationToken);

		var configuredCountry = botOptions.Value.Country;
		var country = Enum.TryParse<Country>(configuredCountry, true, out var parsedCountry)
			? parsedCountry
			: Country.Xx;
		if (user.Country != country)
			await users.UpdateCountryAsync(BotId, country, cancellationToken);

		var loginTime = DateTimeOffset.UtcNow;
		var session = new GameSession(BotId, configuredName, BotToken, user.Privilege, loginTime)
		{
			IsBot = true,
			Country = country
		};

		foreach (var channel in channelRegistry.AutoJoinChannels)
			channelMembership.Join(session, channel);

		sessionRegistry.TryAdd(session);
		logger.LogInformation("Bot session created: BotId={BotId}", BotId);
		return session;
	}
}
