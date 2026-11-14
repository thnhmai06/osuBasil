namespace Basil.Host.Shared.Views;

/// <summary>What each server setting does, its unit or format and its default, written for the operator who changes it.</summary>
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
		["general.motd"] =
			"Message shown to a user when they log in. Text; empty for none. Default: none.",
		["locks.accounts"] =
			"Restricts account registration to account managers. On or off. Default: off.",
		["locks.rooms"] =
			"Restricts room creation to room managers. On or off. Default: off.",
		["locks.beatmapsets"] =
			"Restricts beatmapset upload and deletion to beatmapset managers. On or off. Default: on.",
		["locks.profiles"] =
			"Restricts profile and password changes to account managers. On or off. Default: off.",
		["accounts.defaultPrivileges"] =
			"Privileges a new account holds when none are asked for. Comma-separated names from player, moderator, supporter, owner, developer, tournament. Default: player, supporter.",
		["sessions.maxPerUser.stable"] =
			"How many osu! game clients one user may hold open at once. Whole number, at least 1. Default: 1.",
		["sessions.maxPerUser.stableTourney"] =
			"How many osu!tourney clients one user may hold open at once. Whole number, at least 1. Default: 10.",
		["sessions.maxPerUser.irc"] =
			"How many IRC clients one user may hold open at once. Whole number, at least 1. Default: 5.",
		["sessions.maxPerUser.api"] =
			"How many HTTP API connections one user may hold open at once; a new one beyond it replaces the oldest. Whole number, at least 1. Default: 10.",
		["sessions.apiToken.lifetime"] =
			"How long an HTTP API token stays valid after its last use. Duration as [d.]hh:mm:ss. Default: 7.00:00:00 (7 days).",
		["sessions.retentionPeriod"] =
			"How long ended sessions are kept before they are deleted. Duration as [d.]hh:mm:ss. Default: 90.00:00:00 (90 days).",
		["rateLimits.login.maxAttempts"] =
			"How many failed login attempts are allowed within the window. Whole number, at least 1. Default: 5.",
		["rateLimits.login.window"] =
			"How long failed login attempts are counted over. Duration as [d.]hh:mm:ss. Default: 00:15:00 (15 minutes).",
		["rateLimits.login.lockout"] =
			"How long a source is refused after too many failed login attempts. Duration as [d.]hh:mm:ss. Default: 00:15:00 (15 minutes).",
		["rateLimits.registration.perIp.count"] =
			"How many accounts one IP address may register within the window. Whole number, at least 1. Default: 1.",
		["rateLimits.registration.perIp.window"] =
			"The window of the per-address registration limit. Duration as [d.]hh:mm:ss. Default: 1.00:00:00 (24 hours).",
		["rateLimits.registration.perServer.count"] =
			"How many accounts may be registered on the whole server within the window. Whole number, at least 1. Default: 100.",
		["rateLimits.registration.perServer.window"] =
			"The window of the server-wide registration limit. Duration as [d.]hh:mm:ss. Default: 01:00:00 (1 hour).",
		["rateLimits.connections.perIp"] =
			"How many connections one IP address may hold open at once. Whole number, at least 1. Default: 20.",
		["rateLimits.rest.mutations.count"] =
			"How many changes one user may make through the HTTP API within the window. Whole number, at least 1. Default: 60.",
		["rateLimits.rest.mutations.window"] =
			"The window of the HTTP API change limit. Duration as [d.]hh:mm:ss. Default: 00:01:00 (1 minute).",
		["rateLimits.rest.anonymous.perIp.count"] =
			"How many HTTP API requests without a token one IP address may make within the window. Whole number, at least 1. Default: 120.",
		["rateLimits.rest.anonymous.perIp.window"] =
			"The window of the limit on requests without a token. Duration as [d.]hh:mm:ss. Default: 00:01:00 (1 minute).",
		["rateLimits.chat.count"] =
			"How many chat messages one user may send within the window before it counts as flooding. Whole number, at least 1. Default: 10.",
		["rateLimits.chat.window"] =
			"The window of the chat flood limit. Duration as [d.]hh:mm:ss. Default: 00:00:05 (5 seconds).",
		["rateLimits.admission.requests"] =
			"How many requests a standard user may make within the window. Whole number, at least 1. Default: 120.",
		["rateLimits.admission.window"] =
			"The window of the request limit. Duration as [d.]hh:mm:ss. Default: 00:01:00 (1 minute).",
		["rateLimits.admission.elevatedMultiplier"] =
			"The factor applied to the request limit for an elevated user. Whole number, at least 1. Default: 4.",
		["rateLimits.admission.interactiveConcurrency"] =
			"How many interactive requests may run at once. Whole number, at least 1. Default: 256.",
		["rateLimits.admission.listingConcurrency"] =
			"How many listing requests may run at once. Whole number, at least 1. Default: 32.",
		["rateLimits.admission.uploadConcurrency"] =
			"How many upload requests may run at once. Whole number, at least 1. Default: 8.",
		["rateLimits.admission.liveConcurrency"] =
			"How many live-stream requests may run at once. Whole number, at least 1. Default: 1024.",
		["rateLimits.admission.degradedAbove"] =
			"The load from which the server reports itself degraded. Number above 0 and below the shed load. Default: 0.5.",
		["rateLimits.admission.shedAbove"] =
			"The load from which listing and picture work is refused. Number above the degraded load and below the hard shed load. Default: 0.7.",
		["rateLimits.admission.hardShedAbove"] =
			"The load from which every kind of work is refused. Number above the shed load and at most 1. Default: 0.9.",
		["rateLimits.admission.busyRetryAfter"] =
			"How long a caller refused for load or a full work class is told to wait. Duration as [d.]hh:mm:ss. Default: 00:00:05 (5 seconds).",
		["rateLimits.admission.assetConcurrency"] =
			"How many picture requests may run at once. Whole number, at least 1. Default: 64.",
		["rateLimits.admission.assetRequests"] =
			"How many picture requests one address may make within the window. Whole number, at least 1. Default: 1200.",
		["uploads.oszMaxSize"] =
			"The largest beatmapset archive accepted. Bytes, at least 1. Default: 104857600 (100 MiB).",
		["uploads.screenshotMaxSize"] =
			"The largest screenshot accepted. Bytes, at least 1. Default: 10485760 (10 MiB).",
		["uploads.screenshotMaxSide"] =
			"The largest side of a screenshot. Pixels, at least 1. Default: 8192.",
		["uploads.avatarMaxSize"] =
			"The largest avatar accepted. Bytes, at least 1. Default: 5242880 (5 MiB).",
		["uploads.avatarMaxSide"] =
			"The largest side of an avatar. Pixels, at least 1. Default: 4096.",
		["beatmaps.mirror.downloadEndpoint"] =
			"The address beatmapset downloads are sent to. Absolute http or https address; empty for no download mirror. Default: none.",
		["beatmaps.mirror.searchEndpoint"] =
			"The address beatmap searches are sent to. Absolute http or https address; empty for no search mirror. Default: none.",
		["multiplayer.rounds.scoreWait"] =
			"How long the scores of a finished round are waited for. Duration as [d.]hh:mm:ss. Default: 00:00:10 (10 seconds).",
		["bot.commandPrefix"] =
			"The text a chat message starts with to be a command for BasilBot. Text, not empty. Default: !",
		["cors.allowedOrigins"] =
			"The origins that may call the HTTP API from a browser. Comma-separated origins such as https://example.com; empty for none. Default: none.",
		["menuIcon.url"] =
			"The address the icon on the main menu links to. Absolute http or https address; empty for none. Default: none.",
		["menuIcon.image"] =
			"The image of the icon on the main menu. An absolute http or https address, or the path of a stored file relative to the data directory; empty for none. Default: none."
	};

	/// <summary>Gets the description of one setting.</summary>
	/// <param name="path">The camel-case path of the setting, such as <c>bot.commandPrefix</c>.</param>
	/// <returns>The description, or <see langword="null" /> when no setting has that path.</returns>
	public static string? Of(string path)
	{
		return All.GetValueOrDefault(path);
	}
}
