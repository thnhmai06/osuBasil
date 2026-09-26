namespace Basil.ArchitectureTests;

/// <summary>
///     The cross-namespace references <c>Basil.Domain</c> permits, each one a deliberate decision.
/// </summary>
/// <remarks>
///     Mirrors <see cref="SliceAdjacency" />'s shape one level down: a namespace under
///     <c>Basil.Domain</c> is not forbidden from referencing another one -- a round genuinely has a
///     beatmap, a submission genuinely needs the client details captured at login -- because a ban
///     would be unenforceable and blanket permission would make the rule meaningless. Every edge is
///     named here instead, so an undeclared one fails the build.
/// </remarks>
/// <remarks>
///     Measured 2026-09-10, before Task C1 moves any file into <c>Basil.Domain</c>; updated when the
///     stray <c>Login</c> namespace (holding client/session value types that had nothing to do with
///     any one slice, plus one Auth-only audit record) was split up: the audit record
///     (<c>Login</c> record, renamed <c>LoginEvent</c>) merged into <c>Auth</c> since it never needed
///     its own namespace; <c>Country</c> moved into <c>Users</c>, the one slice it actually
///     describes; and <c>ClientDetails</c>/<c>Geolocation</c>/<c>ClientVersion</c> — genuinely shared
///     across Auth, Scores and beyond — got their own <c>Client</c> namespace instead of continuing
///     to borrow Login's. This list is not an empty allowlist grown from scratch: every namespace
///     under <c>Basil.Domain</c>, including ones with no <c>Features/</c> counterpart
///     (<c>Channels</c>, <c>Client</c>, <c>Social</c>), is in the population
///     <see cref="DomainBoundaryTests.Namespaces_Should_Only_Reference_Declared_Namespaces" />
///     checks, and <c>Channels -> Users</c> exists precisely because a namespace without a slice
///     counterpart is not exempt.
/// </remarks>
internal static class DomainAdjacency
{
	public static readonly (string From, string To)[] Allowed =
	[
		// Round.cs describes the beatmap a round is played on.
		("Multiplayer", "Beatmaps"),

		// Round.cs carries the Submission each player produced for the round.
		("Multiplayer", "Scores"),

		// Submission.cs, HitCounts.cs and Mods.cs describe the beatmap a score was set on.
		("Scores", "Beatmaps"),

		// Submission.ValidateClientDetails checks the submission's ClientDetails against the
		// osu! version captured at login.
		("Scores", "Client"),

		// Channel.cs gates read/write access on the UserPrivileges level required to use it.
		("Channels", "Users"),

		// IUserStatRepository.IncrementAsync and Stats.Mode key a user's per-mode stats by GameMode.
		("Users", "Beatmaps"),

		// ScoreReport, ScoreInsertRow and ScoreRow carry the team a player scored for.
		("Scores", "Multiplayer"),

		// LoginForm.From parses the ClientDetails and ClientVersion an osu! client sends at login.
		("Auth", "Client"),

		// IOsuCalculator.Analyze takes the Mods a beatmap is being analyzed under.
		("Beatmaps", "Scores"),

		// MirrorService reads and writes its endpoints through ISettingsRepository.
		("Beatmaps", "Content"),

		// AdminKeyService stores the admin key hash through ISettingsRepository.
		("Auth", "Content"),

		// CredentialVerifier fetches a user's stored password hash through IUserRepository.
		("Auth", "Users")
	];
}