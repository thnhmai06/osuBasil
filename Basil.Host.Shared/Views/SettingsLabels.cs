namespace Basil.Host.Shared.Views;

public static class SettingsLabels
{
	public const int MaxLength = 36;

	public static readonly IReadOnlyDictionary<string, string> All = new Dictionary<string, string>
	{
		["general.motd"] = "Message of the day",
		["locks.accounts"] = "Lock account registration",
		["locks.rooms"] = "Lock room creation",
		["locks.beatmapsets"] = "Lock beatmapset uploads",
		["locks.profiles"] = "Lock profile changes",
		["accounts.defaultPrivileges"] = "Default privileges of new accounts",
		["sessions.retentionPeriod"] = "Ended session retention",
		["rateLimits.login.maxAttempts"] = "Login: failed attempts allowed",
		["rateLimits.login.window"] = "Login: attempt window",
		["rateLimits.login.lockout"] = "Login: lockout time",
		["rateLimits.registration.perServer.count"] = "Registrations per server",
		["rateLimits.registration.perServer.window"] = "Registrations per server: window",
		["rateLimits.connections.streamsPerUser"] = "Open live streams per user",
		["rateLimits.rest.mutations.count"] = "API changes per user",
		["rateLimits.rest.mutations.window"] = "API changes per user: window",
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
		["rateLimits.admission.listingRequests"] = "Listing requests per user",
		["rateLimits.admission.uploadRequests"] = "Uploads per user",
		["rateLimits.admission.liveRequests"] = "Live streams opened per user",
		["rateLimits.admission.pollRequests"] = "osu! client polls per user",
		["rateLimits.admission.uploadBytesPerHour"] = "Upload bytes per user and hour",
		["uploads.oszMaxSize"] = "Largest beatmapset archive",
		["uploads.screenshotMaxSize"] = "Largest screenshot",
		["uploads.screenshotMaxSide"] = "Largest screenshot side",
		["uploads.avatarMaxSize"] = "Largest avatar",
		["uploads.avatarMaxSide"] = "Largest avatar side",
		["beatmaps.performanceTimeout"] = "Calculation time limit",
		["beatmaps.scoreWait"] = "Score performance wait",
		["beatmaps.beatmapWait"] = "Beatmapset difficulty wait",
		["multiplayer.rounds.scoreWait"] = "Round score wait",
		["bot.commandPrefix"] = "BasilBot command prefix",
		["cors.allowedOrigins"] = "Allowed browser origins",
		["menuIcon.url"] = "Main menu icon link",
		["menuIcon.image"] = "Main menu icon image"
	};

	public static string? Of(string path)
	{
		return All.GetValueOrDefault(path);
	}
}