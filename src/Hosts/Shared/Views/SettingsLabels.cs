namespace Basil.Host.Shared.Views;

/// <summary>The name of each server setting as the operator reads it, by path.</summary>
/// <remarks>
///     Paths are the camel-case JSON paths of the settings, beginning with the category name used by
///     <see cref="SettingsCategories" />, as in <see cref="SettingsDescriptions" />; <c>menuIcon</c> is not a category
///     but its fields are listed. Every setting of <c>ServerSettings</c> has one entry.
/// </remarks>
public static class SettingsLabels
{
	/// <summary>The most characters a label may have, so that it fits the label column of a settings form.</summary>
	public const int MaxLength = 36;

	/// <summary>Gets the human-readable name of each setting, by path.</summary>
	public static readonly IReadOnlyDictionary<string, string> All = new Dictionary<string, string>
	{
		["general.motd"] = "Message of the day",
		["locks.accounts"] = "Lock account registration",
		["locks.rooms"] = "Lock room creation",
		["locks.beatmapsets"] = "Lock beatmapset uploads",
		["locks.profiles"] = "Lock profile changes",
		["accounts.defaultPrivileges"] = "Default privileges of new accounts",
		["sessions.maxPerUser.stable"] = "Max sessions per user (osu!)",
		["sessions.maxPerUser.stableTourney"] = "Max sessions per user (osu!tourney)",
		["sessions.maxPerUser.irc"] = "Max sessions per user (IRC)",
		["sessions.maxPerUser.api"] = "Max sessions per user (HTTP API)",
		["sessions.apiToken.lifetime"] = "API token lifetime",
		["sessions.retentionPeriod"] = "Ended session retention",
		["rateLimits.login.maxAttempts"] = "Login: failed attempts allowed",
		["rateLimits.login.window"] = "Login: attempt window",
		["rateLimits.login.lockout"] = "Login: lockout time",
		["rateLimits.registration.perIp.count"] = "Registrations per IP address",
		["rateLimits.registration.perIp.window"] = "Registrations per IP address: window",
		["rateLimits.registration.perServer.count"] = "Registrations per server",
		["rateLimits.registration.perServer.window"] = "Registrations per server: window",
		["rateLimits.connections.perIp"] = "Open connections per IP address",
		["rateLimits.rest.mutations.count"] = "API changes per user",
		["rateLimits.rest.mutations.window"] = "API changes per user: window",
		["rateLimits.rest.anonymous.perIp.count"] = "Anonymous API requests per IP",
		["rateLimits.rest.anonymous.perIp.window"] = "Anonymous API requests: window",
		["rateLimits.chat.count"] = "Chat flood: messages allowed",
		["rateLimits.chat.window"] = "Chat flood: window",
		["rateLimits.admission.requests"] = "Requests per user",
		["rateLimits.admission.window"] = "Requests per user: window",
		["rateLimits.admission.elevatedMultiplier"] = "Elevated user limit multiplier",
		["rateLimits.admission.interactiveConcurrency"] = "Concurrent interactive requests",
		["rateLimits.admission.listingConcurrency"] = "Concurrent listing requests",
		["rateLimits.admission.uploadConcurrency"] = "Concurrent upload requests",
		["rateLimits.admission.liveConcurrency"] = "Concurrent live-stream requests",
		["rateLimits.admission.degradedAbove"] = "Degraded above load",
		["rateLimits.admission.shedAbove"] = "Refuse listings above load",
		["rateLimits.admission.hardShedAbove"] = "Refuse everything above load",
		["rateLimits.admission.busyRetryAfter"] = "Retry-after when busy",
		["rateLimits.admission.assetConcurrency"] = "Concurrent picture requests",
		["rateLimits.admission.assetRequests"] = "Picture requests per IP address",
		["uploads.oszMaxSize"] = "Largest beatmapset archive",
		["uploads.screenshotMaxSize"] = "Largest screenshot",
		["uploads.screenshotMaxSide"] = "Largest screenshot side",
		["uploads.avatarMaxSize"] = "Largest avatar",
		["uploads.avatarMaxSide"] = "Largest avatar side",
		["beatmaps.mirror.downloadEndpoint"] = "Beatmap download mirror",
		["beatmaps.mirror.searchEndpoint"] = "Beatmap search mirror",
		["multiplayer.rounds.scoreWait"] = "Round score wait",
		["bot.commandPrefix"] = "BasilBot command prefix",
		["cors.allowedOrigins"] = "Allowed browser origins",
		["menuIcon.url"] = "Main menu icon link",
		["menuIcon.image"] = "Main menu icon image"
	};

	/// <summary>Gets the name of one setting.</summary>
	/// <param name="path">The camel-case path of the setting, such as <c>bot.commandPrefix</c>.</param>
	/// <returns>The name, or <see langword="null" /> when no setting has that path.</returns>
	public static string? Of(string path)
	{
		return All.GetValueOrDefault(path);
	}
}