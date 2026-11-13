namespace Basil.Host.Shared.Views;

/// <summary>What each server setting means, written for the operator who changes it.</summary>
/// <remarks>
///     Paths are the camel-case JSON paths of the settings, beginning with the category name used by
///     <see cref="SettingsCategories" />, for example <c>sessions.maxPerUser.irc</c>. Every setting of
///     <c>ServerSettings</c> has one entry.
/// </remarks>
public static class SettingsDescriptions
{
	/// <summary>Gets the description of each setting, by path.</summary>
	public static readonly IReadOnlyDictionary<string, string> All = new Dictionary<string, string>
	{
		["general.motd"] = "Message of the day shown to a user when they log in; empty for none.",
		["locks"] = "Areas frozen for everyone but their managers: accounts, rooms, beatmapsets and profiles.",
		["accounts.defaultPrivileges"] = "Privileges a new account holds when none are asked for.",
		["sessions.maxPerUser.stable"] = "How many osu! game clients one user may hold open at once.",
		["sessions.maxPerUser.stableTourney"] = "How many osu!tourney clients one user may hold open at once.",
		["sessions.maxPerUser.irc"] = "How many IRC clients one user may hold open at once.",
		["sessions.maxPerUser.api"] =
			"How many HTTP API connections one user may hold open at once; a new one beyond it replaces the oldest.",
		["sessions.apiToken.lifetime"] = "How long an HTTP API token stays valid after its last use.",
		["sessions.retentionPeriod"] = "How long ended sessions are kept before they are deleted.",
		["rateLimits.login.maxAttempts"] = "How many failed login attempts are allowed within the window.",
		["rateLimits.login.window"] = "How long failed login attempts are counted over.",
		["rateLimits.login.lockout"] = "How long a source is refused after too many failed login attempts.",
		["rateLimits.registration.perIp.count"] = "How many accounts one IP address may register within the window.",
		["rateLimits.registration.perIp.window"] = "The window of the per-address registration limit.",
		["rateLimits.registration.perServer.count"] =
			"How many accounts may be registered on the whole server within the window.",
		["rateLimits.registration.perServer.window"] = "The window of the server-wide registration limit.",
		["rateLimits.connections.perIp"] = "How many connections one IP address may hold open at once.",
		["rateLimits.rest.mutations.count"] = "How many changes one user may make through the HTTP API within the window.",
		["rateLimits.rest.mutations.window"] = "The window of the HTTP API change limit.",
		["rateLimits.rest.anonymous.perIp.count"] =
			"How many HTTP API requests without a token one IP address may make within the window.",
		["rateLimits.rest.anonymous.perIp.window"] = "The window of the limit on requests without a token.",
		["rateLimits.chat.count"] =
			"How many chat messages one user may send within the window before it counts as flooding.",
		["rateLimits.chat.window"] = "The window of the chat flood limit.",
		["rateLimits.admission.requests"] = "How many requests a standard user may make within the window.",
		["rateLimits.admission.window"] = "The window of the request limit.",
		["rateLimits.admission.elevatedMultiplier"] = "The factor applied to the request limit for an elevated user.",
		["rateLimits.admission.interactiveConcurrency"] = "How many interactive requests may run at once.",
		["rateLimits.admission.listingConcurrency"] = "How many listing requests may run at once.",
		["rateLimits.admission.uploadConcurrency"] = "How many upload requests may run at once.",
		["rateLimits.admission.liveConcurrency"] = "How many live-stream requests may run at once.",
		["rateLimits.admission.degradedAbove"] =
			"The load, above 0 and at most 1, from which the server reports itself degraded.",
		["rateLimits.admission.shedAbove"] = "The load from which listing and picture work is refused.",
		["rateLimits.admission.hardShedAbove"] = "The load from which every kind of work is refused.",
		["rateLimits.admission.busyRetryAfter"] =
			"How long a caller refused for load or a full work class is told to wait.",
		["rateLimits.admission.assetConcurrency"] = "How many picture requests may run at once.",
		["rateLimits.admission.assetRequests"] = "How many picture requests one address may make within the window.",
		["uploads.oszMaxSize"] = "The largest beatmapset archive, in bytes.",
		["uploads.screenshotMaxSize"] = "The largest screenshot, in bytes.",
		["uploads.screenshotMaxSide"] = "The largest side of a screenshot, in pixels.",
		["uploads.avatarMaxSize"] = "The largest avatar, in bytes.",
		["uploads.avatarMaxSide"] = "The largest side of an avatar, in pixels.",
		["beatmaps.mirror.downloadEndpoint"] =
			"The address beatmapset downloads are sent to; empty for no download mirror.",
		["beatmaps.mirror.searchEndpoint"] = "The address beatmap searches are sent to; empty for no search mirror.",
		["multiplayer.rounds.scoreWait"] = "How long the scores of a finished round are waited for.",
		["bot.commandPrefix"] = "The text a chat message starts with to be a command for BasilBot.",
		["cors.allowedOrigins"] = "The origins that may call the HTTP API from a browser.",
		["menuIcon.url"] = "The address the icon on the main menu links to; empty for none.",
		["menuIcon.image"] = "The image of the icon on the main menu; empty for none."
	};

	/// <summary>Gets the description of one setting.</summary>
	/// <param name="path">The camel-case path of the setting, such as <c>bot.commandPrefix</c>.</param>
	/// <returns>The description, or <see langword="null" /> when no setting has that path.</returns>
	public static string? Of(string path)
	{
		return All.GetValueOrDefault(path);
	}
}
